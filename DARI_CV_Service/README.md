---
title: Dari CV Service
emoji: 🔍
colorFrom: blue
colorTo: indigo
sdk: docker
app_port: 7860
pinned: false
---

# Dari CV Service

FastAPI service that serves CLIP (`openai/clip-vit-large-patch14`) image
embeddings for the Dari visual-search feature.

Endpoints:
- `GET  /health` — liveness + model name
- `POST /encode` — multipart image file → 768-d embedding
- `POST /encode-url` — `{ "url": "..." }` → 768-d embedding

The .NET backend calls this service via the `CVService:BaseUrl` setting.
