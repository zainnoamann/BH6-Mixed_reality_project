"""CreativeTwin AI server.

Exposes the two local pipelines to Unity over HTTP, mirroring the UI flow:

    POST /generate            {prompt}        -> job runs Pipeline A (text -> image)
    GET  /jobs/{id}                           -> status, stage, progress, urls
    POST /jobs/{id}/accept                    -> job runs Pipeline B (image -> shape.glb)
    POST /jobs/{id}/regenerate {prompt?}      -> new image for the same job
    DELETE /jobs/{id}                         -> cancel (kills the running subprocess)
    GET  /files/{id}/image.png | object.glb
    GET  /health                              -> gpu / models / venvs, so Unity can decide
                                                 between real generation and placeholder mode

Each pipeline runs as a SUBPROCESS in its own virtualenv. They must never share a process:
their dependency sets clash and both models do not fit VRAM together.

Pipeline B is Hunyuan3D-2mini-Turbo (shape). When the optional Hunyuan3D-Paint model is
installed, a third stage bakes the texture onto the mesh and the job returns textured.glb;
otherwise it returns the bare shape.glb and Unity applies a URP material instead. Paint
failing never fails the job - the shape is still delivered.

Runs inside .venv-server (fastapi + uvicorn only). Start with:  python start.py
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import threading
import time
import uuid
from pathlib import Path
from typing import Optional

from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse, JSONResponse
from pydantic import BaseModel

ROOT = Path(__file__).resolve().parents[1]
JOBS_DIR = ROOT / "jobs"
LOG_DIR = ROOT / "logs"
STATUS_FILE = ROOT / "status.json"

JOBS_DIR.mkdir(exist_ok=True)
LOG_DIR.mkdir(exist_ok=True)

app = FastAPI(title="CreativeTwin AI", version="0.1")


# --------------------------------------------------------------------------- environment


def venv_python(name: str) -> Path:
    base = ROOT / name
    return base / ("Scripts/python.exe" if os.name == "nt" else "bin/python")


def read_status() -> dict:
    if STATUS_FILE.exists():
        try:
            return json.loads(STATUS_FILE.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            pass
    return {}


def gpu_info() -> dict:
    """Ask nvidia-smi directly so the server does not need torch itself."""
    exe = shutil.which("nvidia-smi")
    if not exe:
        return {"available": False, "reason": "nvidia-smi not found"}
    try:
        out = subprocess.run(
            [exe, "--query-gpu=name,memory.total,driver_version", "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=10, check=True,
        ).stdout.strip().splitlines()
    except Exception as exc:  # driver present but not working
        return {"available": False, "reason": f"nvidia-smi failed: {exc}"}
    if not out:
        return {"available": False, "reason": "no GPU reported"}
    name, mem, driver = [p.strip() for p in out[0].split(",")]
    return {"available": True, "name": name, "vramMb": int(float(mem)), "driver": driver}


def health() -> dict:
    status = read_status()
    gpu = gpu_info()
    models = status.get("models", {})
    venvs = {
        "a": venv_python(".venv-a").exists(),
        "b": venv_python(".venv-b").exists(),
    }
    ready = (
        gpu["available"]
        and venvs["a"] and venvs["b"]
        and models.get("flux", {}).get("ready", False)
        and models.get("hunyuan", {}).get("ready", False)
    )
    missing = []
    if not gpu["available"]:
        missing.append("NVIDIA GPU: " + gpu.get("reason", "not detected"))
    if not venvs["a"]:
        missing.append("virtualenv .venv-a (run bootstrap.py)")
    if not venvs["b"]:
        missing.append("virtualenv .venv-b (run bootstrap.py)")
    if not models.get("flux", {}).get("ready"):
        missing.append("FLUX.2-klein-4B weights (run bootstrap.py)")
    if not models.get("hunyuan", {}).get("ready"):
        missing.append("Hunyuan3D-2mini weights/code (run bootstrap.py)")
    return {
        "ready": ready,
        "gpu": gpu,
        "venvs": venvs,
        "models": models,
        "missing": missing,
        "root": str(ROOT),
        "bootstrappedAt": status.get("bootstrappedAt"),
    }


# --------------------------------------------------------------------------- jobs


class Job:
    def __init__(self, prompt: str) -> None:
        self.id = uuid.uuid4().hex[:12]
        self.prompt = prompt
        self.status = "queued"          # queued | running | awaiting_review | done | failed | cancelled
        self.phase = "image"            # image | model
        self.stage = "Queued"
        self.progress = 0.0
        self.message = ""
        self.created = time.time()
        self.updated = time.time()
        self.dir = JOBS_DIR / self.id
        self.dir.mkdir(parents=True, exist_ok=True)
        self.image = self.dir / "image.png"
        self.model = self.dir / "shape.glb"
        self.textured = self.dir / "textured.glb"
        self.log = LOG_DIR / f"job-{self.id}.log"
        self.proc: Optional[subprocess.Popen] = None
        self.lock = threading.Lock()

    def to_dict(self) -> dict:
        return {
            "jobId": self.id,
            "prompt": self.prompt,
            "status": self.status,
            "phase": self.phase,
            "stage": self.stage,
            "progress": round(self.progress, 3),
            "message": self.message,
            "elapsedSeconds": round(time.time() - self.created, 1),
            "imageUrl": f"/files/{self.id}/image.png" if self.image.exists() else None,
            "modelUrl": (f"/files/{self.id}/textured.glb" if self.textured.exists()
                         else f"/files/{self.id}/shape.glb" if self.model.exists() else None),
            "textured": self.textured.exists(),
        }

    def set(self, **fields) -> None:
        with self.lock:
            for key, value in fields.items():
                setattr(self, key, value)
            self.updated = time.time()


JOBS: dict[str, Job] = {}
RUN_LOCK = threading.Lock()   # one GPU pipeline at a time


def paint_available() -> bool:
    return bool(read_status().get("models", {}).get("paint", {}).get("ready"))


def run_pipeline(job: Job, argv: list[str], phase: str, on_success: str,
                 scale: tuple = (0.0, 1.0), then=None) -> None:
    """Run one pipeline subprocess, streaming ##PROGRESS lines into the job."""
    python = venv_python(".venv-a" if phase == "image" else ".venv-b")

    if not python.exists():
        job.set(status="failed", message=f"{python} missing - run bootstrap.py")
        return

    env = dict(os.environ)
    env.setdefault("HF_HOME", str(ROOT / "hf_cache"))
    env.setdefault("PYTHONUNBUFFERED", "1")
    if phase == "model":
        # Hunyuan is used from a clone, and keeps its own model cache.
        env["PYTHONPATH"] = str(ROOT / "deps" / "Hunyuan3D-2") + os.pathsep + env.get("PYTHONPATH", "")
        env.setdefault("HY3DGEN_MODELS", str(ROOT / "hf_cache" / "hy3dgen"))
        env.setdefault("TORCH_CUDA_ARCH_LIST", read_status().get("archList", ""))

    with RUN_LOCK:
        if job.status == "cancelled":
            return
        job.set(status="running", phase=phase, stage="Starting", progress=0.0, message="")
        with open(job.log, "a", encoding="utf-8") as log:
            log.write(f"\n=== {phase} {time.strftime('%H:%M:%S')} :: {' '.join(argv)}\n")
            try:
                proc = subprocess.Popen(
                    [str(python), *argv],
                    cwd=str(ROOT), env=env,
                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                )
            except OSError as exc:
                job.set(status="failed", message=str(exc))
                return
            job.proc = proc
            error = ""
            for line in proc.stdout:
                log.write(line)
                log.flush()
                if line.startswith("##PROGRESS"):
                    parts = line.split(maxsplit=2)
                    try:
                        raw = float(parts[1])
                        lo, hi = scale
                        job.set(progress=lo + (hi - lo) * raw,
                                stage=parts[2].strip() if len(parts) > 2 else "")
                    except (IndexError, ValueError):
                        pass
                elif line.startswith("##ERROR"):
                    error = line[len("##ERROR"):].strip()
            code = proc.wait()
            job.proc = None

    if job.status == "cancelled":
        return
    if code != 0:
        job.set(status="failed", message=error or f"{phase} pipeline exited with code {code} (see {job.log.name})")
        return
    if then is not None:
        then(job)
        return

    job.set(status=on_success, progress=1.0, stage="Ready", message="")


def start_image(job: Job) -> None:
    if job.image.exists():
        job.image.unlink()
    argv = [str(ROOT / "pipelines" / "run_t2i.py"), "--prompt", job.prompt, "--out", str(job.image)]
    threading.Thread(target=run_pipeline, args=(job, argv, "image", "awaiting_review"), daemon=True).start()


def start_model(job: Job) -> None:
    recommended = read_status().get("recommended", {})
    argv = [
        str(ROOT / "pipelines" / "run_hunyuan_shape.py"),
        "--input", str(job.image),
        "--output", str(job.model),
        "--octree", str(recommended.get("octreeResolution", 256)),
        "--steps", str(recommended.get("steps", 5)),
    ]

    # With paint installed, shape is the first 60% of the model phase and texture the rest.
    if paint_available():
        threading.Thread(
            target=run_pipeline,
            args=(job, argv, "model", "done"),
            kwargs={"scale": (0.0, 0.6), "then": start_paint},
            daemon=True,
        ).start()
        return

    threading.Thread(target=run_pipeline, args=(job, argv, "model", "done"), daemon=True).start()


def start_paint(job: Job) -> None:
    """Bake the texture onto the mesh. A failure here leaves the untextured shape usable."""
    recommended = read_status().get("recommended", {})
    argv = [
        str(ROOT / "pipelines" / "run_hunyuan_paint.py"),
        "--image", str(job.image),
        "--shape", str(job.model),
        "--out", str(job.textured),
        "--steps", str(recommended.get("paintSteps", 30)),
    ]

    def finish() -> None:
        if job.status == "failed" and job.model.exists():
            job.set(status="done", progress=1.0, stage="Ready (untextured)",
                    message="Texture baking failed; returning the untextured mesh. "
                            f"See {job.log.name}.")

    threading.Thread(
        target=lambda: (run_pipeline(job, argv, "model", "done", scale=(0.6, 1.0)), finish()),
        daemon=True,
    ).start()


# --------------------------------------------------------------------------- routes


class GenerateRequest(BaseModel):
    prompt: str
    operation: str = "add"


class RegenerateRequest(BaseModel):
    prompt: Optional[str] = None


@app.get("/health")
def get_health() -> dict:
    return health()


@app.post("/generate", status_code=202)
def post_generate(body: GenerateRequest) -> dict:
    info = health()
    if not info["ready"]:
        raise HTTPException(status_code=503, detail={"message": "AI server not ready", "missing": info["missing"]})
    if not body.prompt.strip():
        raise HTTPException(status_code=400, detail="prompt is empty")
    job = Job(body.prompt.strip())
    JOBS[job.id] = job
    start_image(job)
    return job.to_dict()


@app.get("/jobs/{job_id}")
def get_job(job_id: str) -> dict:
    job = JOBS.get(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="unknown job")
    return job.to_dict()


@app.post("/jobs/{job_id}/accept", status_code=202)
def post_accept(job_id: str) -> dict:
    job = JOBS.get(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="unknown job")
    if job.status != "awaiting_review" or not job.image.exists():
        raise HTTPException(status_code=409, detail=f"job is {job.status}, not awaiting review")
    start_model(job)
    return job.to_dict()


@app.post("/jobs/{job_id}/regenerate", status_code=202)
def post_regenerate(job_id: str, body: RegenerateRequest) -> dict:
    job = JOBS.get(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="unknown job")
    if job.status == "running":
        raise HTTPException(status_code=409, detail="job is still running")
    if body.prompt and body.prompt.strip():
        job.prompt = body.prompt.strip()
    job.set(status="queued", phase="image")
    start_image(job)
    return job.to_dict()


@app.delete("/jobs/{job_id}")
def delete_job(job_id: str) -> dict:
    job = JOBS.get(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="unknown job")
    job.set(status="cancelled", stage="Cancelled")
    proc = job.proc
    if proc is not None and proc.poll() is None:
        proc.kill()
    return job.to_dict()


@app.get("/files/{job_id}/{name}")
def get_file(job_id: str, name: str):
    job = JOBS.get(job_id)
    if job is None:
        raise HTTPException(status_code=404, detail="unknown job")
    path = {
        "image.png": job.image,
        "shape.glb": job.model,
        "textured.glb": job.textured,
        "object.glb": job.textured if job.textured.exists() else job.model,
    }.get(name)
    if path is None or not path.exists():
        raise HTTPException(status_code=404, detail="file not ready")
    media = "image/png" if name.endswith(".png") else "model/gltf-binary"
    return FileResponse(str(path), media_type=media, filename=name)


@app.get("/")
def index() -> JSONResponse:
    return JSONResponse({"service": "CreativeTwin AI", "health": "/health", "docs": "/docs"})


if __name__ == "__main__":
    import uvicorn

    port = int(os.environ.get("CREATIVETWIN_PORT", "8765"))
    print(f"CreativeTwin AI server on http://127.0.0.1:{port}  (health: /health, docs: /docs)")
    uvicorn.run(app, host="0.0.0.0", port=port, log_level="info")
