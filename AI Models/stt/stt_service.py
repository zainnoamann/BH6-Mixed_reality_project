"""Speech-to-text service for CreativeTwin (Whisper large-v3-turbo via faster-whisper).

    .venv-stt/bin/python stt/stt_service.py        # serves http://127.0.0.1:8766

POST /transcribe   multipart form: file=<16 kHz wav>   ->  {"text": "...", "seconds": 0.31}
GET  /health

The model stays loaded between requests, so a short utterance is fast on a GPU.
Kept separate from server.py so the AI job server stays an HTTP layer only.
"""
import os
import re
import tempfile
import time

import numpy as np
import soundfile as sf
import uvicorn
from fastapi import FastAPI, File, UploadFile
from faster_whisper import WhisperModel

MODEL = os.environ.get("STT_MODEL", "large-v3-turbo")
PORT = int(os.environ.get("STT_PORT", "8766"))
# Biases decoding toward the words this project actually uses.
HINT = os.environ.get("STT_HINT", "")
# Whisper invents these when it is given silence or noise.
GHOSTS = {"thank you", "thanks", "thanks for watching", "thank you for watching", "you", "bye"}

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
        if os.environ.get("STT_DEBUG"):
            import shutil
            dbg = os.path.join(tempfile.gettempdir(), "stt_last.wav")
            shutil.copy(path, dbg)
            print("[stt] saved", dbg)
        # Decode the WAV ourselves: newer PyAV releases break faster-whisper's own decoder.
        audio, rate = sf.read(path, dtype="float32")
        if audio.ndim > 1:
            audio = audio.mean(axis=1)
        peak = float(abs(audio).max()) if len(audio) else 0.0
        if 0.001 < peak < 0.5:
            audio = audio * (0.7 / peak)
        if rate != 16000:
            w = max(1, int(round(rate / 16000)))
            if w > 1:
                audio = np.convolve(audio, np.ones(w, dtype="float32") / w, mode="same")
            n = int(len(audio) * 16000 / rate)
            audio = np.interp(np.linspace(0, len(audio) - 1, n), np.arange(len(audio)), audio).astype("float32")
        source = audio
        segments, _ = model().transcribe(
            source,
            language="en",
            beam_size=1,
            vad_filter=True,                 # drops silence -> avoids Whisper's "Thank you." hallucinations
            condition_on_previous_text=False,
            initial_prompt=HINT or None,
        )
        text = " ".join(
            s.text.strip() for s in segments
            if not (s.no_speech_prob > 0.6 and s.avg_logprob < -1.0)   # likely silence
        ).strip()
        if re.sub(r"[^a-z ]", "", text.lower()).strip() in GHOSTS:
            text = ""
        text = text.rstrip(". ")             # "Add a sofa." -> "Add a sofa"
        return {"text": text, "seconds": round(time.time() - start, 3)}
    finally:
        os.unlink(path)


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=PORT)
