"""Speech-to-text service for CreativeTwin (Whisper large-v3-turbo via faster-whisper).

    .venv-stt/bin/python stt/stt_service.py        # serves http://127.0.0.1:8766

POST /transcribe   multipart form: file=<wav/any audio>   ->  {"text": "...", "seconds": 0.31}
GET  /health

The model stays loaded in VRAM between requests (~3-6 GB), so a short utterance takes
well under a second on a 5090. Kept separate from server.py so the AI job server stays
an HTTP layer only.
"""
import os
import tempfile
import time

import uvicorn
from fastapi import FastAPI, File, UploadFile
from faster_whisper import WhisperModel

MODEL = os.environ.get("STT_MODEL", "large-v3-turbo")
PORT = int(os.environ.get("STT_PORT", "8766"))
# Biases decoding toward the words this project actually uses.
HINT = "Furniture and room objects: sofa, armchair, coffee table, bookshelf, floor lamp, rug, desk, plant."

app = FastAPI()
_model = None


def model() -> WhisperModel:
    global _model
    if _model is None:
        try:
            _model = WhisperModel(MODEL, device="cuda", compute_type="float16")
        except Exception as exc:  # Blackwell/CTranslate2 mismatch etc.
            print(f"[stt] cuda float16 failed ({exc}); falling back to CPU int8")
            _model = WhisperModel(MODEL, device="cpu", compute_type="int8")
    return _model


@app.on_event("startup")
def warm() -> None:
    model()


@app.get("/health")
def health():
    return {"ready": _model is not None, "model": MODEL}


@app.post("/transcribe")
async def transcribe(file: UploadFile = File(...)):
    data = await file.read()
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        tmp.write(data)
        path = tmp.name
    try:
        start = time.time()
        segments, _ = model().transcribe(
            path,
            language="en",
            beam_size=1,
            vad_filter=True,                 # drops silence -> avoids Whisper's "Thank you." hallucinations
            condition_on_previous_text=False,
            initial_prompt=HINT,
        )
        text = " ".join(s.text.strip() for s in segments).strip()
        return {"text": text, "seconds": round(time.time() - start, 3)}
    finally:
        os.unlink(path)


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=PORT)
