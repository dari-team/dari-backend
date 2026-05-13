from fastapi import FastAPI, File, UploadFile, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from transformers import CLIPProcessor, CLIPModel
from PIL import Image
import torch
import numpy as np
import io

app = FastAPI(title="Dari CV Service", version="1.1.0")

# CLIP model name — ViT-L/14 (768-d) gives meaningfully better Recall@1/5 than
# ViT-B/32 (512-d) at the cost of ~3x inference and ~1.7GB model download.
CLIP_MODEL_NAME = "openai/clip-vit-large-patch14"

# Allow calls from your .NET backend
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# Load CLIP model once on startup (takes ~10s first time, cached after)
print(f"Loading CLIP model: {CLIP_MODEL_NAME} ...")
model = CLIPModel.from_pretrained(CLIP_MODEL_NAME)
processor = CLIPProcessor.from_pretrained(CLIP_MODEL_NAME)
model.eval()
print("CLIP model ready.")


def encode_image(image: Image.Image) -> list[float]:
    """Convert a PIL image to a 512-dimensional embedding vector."""
    inputs = processor(images=image, return_tensors="pt")
    with torch.no_grad():
        outputs = model.vision_model(**inputs)
        features = outputs.pooler_output
        features = model.visual_projection(features)
        # Normalize to unit vector (important for cosine similarity)
        features = features / features.norm(dim=-1, keepdim=True)
    return features[0].tolist()


@app.get("/health")
def health_check():
    return {"status": "ok", "model": CLIP_MODEL_NAME}


@app.post("/encode")
async def encode(file: UploadFile = File(...)):
    """
    Accepts an image file, returns its 512-d CLIP embedding.
    Used for: encoding user's uploaded query photo.
    """
    if file.content_type and not file.content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="File must be an image.")
    
    contents = await file.read()
    image = Image.open(io.BytesIO(contents)).convert("RGB")
    embedding = encode_image(image)
    
    return {"embedding": embedding, "dimensions": len(embedding)}


@app.post("/encode-url")
async def encode_from_url(payload: dict):
    """
    Accepts an image URL, returns its 512-d CLIP embedding.
    Used for: indexing existing listing images by URL.
    """
    import httpx

    url = payload.get("url")
    if not url:
        raise HTTPException(status_code=400, detail="Missing 'url' field.")
    
    async with httpx.AsyncClient(verify=False) as client:
        response = await client.get(url, timeout=15.0)
        if response.status_code != 200:
            raise HTTPException(status_code=400, detail="Could not fetch image from URL.")
    
    image = Image.open(io.BytesIO(response.content)).convert("RGB")
    embedding = encode_image(image)
    
    return {"embedding": embedding, "dimensions": len(embedding)}