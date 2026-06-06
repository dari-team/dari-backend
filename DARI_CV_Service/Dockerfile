# HuggingFace Space (Docker SDK) for the Dari CLIP visual-search service.
# Builds a CPU-only image, bakes the CLIP weights in so cold starts don't
# re-download ~1.7GB, and serves the FastAPI app on the HF-required port 7860.

FROM python:3.11-slim

# HuggingFace Spaces run the container as a non-root user (uid 1000).
RUN useradd -m -u 1000 user
USER user
ENV HOME=/home/user \
    PATH=/home/user/.local/bin:$PATH \
    HF_HOME=/home/user/.cache/huggingface

WORKDIR /home/user/app

# Install CPU-only PyTorch first (avoids pulling ~2GB of unused CUDA libraries),
# then the remaining dependencies.
COPY --chown=user requirements.txt .
RUN pip install --no-cache-dir --upgrade pip && \
    pip install --no-cache-dir torch torchvision --index-url https://download.pytorch.org/whl/cpu && \
    pip install --no-cache-dir -r requirements.txt

# Bake the CLIP weights into the image so the container starts fast (loads from
# the image layer instead of downloading from the Hub on every cold start).
RUN python -c "from transformers import CLIPModel, CLIPProcessor; CLIPModel.from_pretrained('openai/clip-vit-large-patch14'); CLIPProcessor.from_pretrained('openai/clip-vit-large-patch14')"

# Copy the app code (main.py + anything else in this folder).
COPY --chown=user . .

EXPOSE 7860
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "7860"]
