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
import math
import re
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


# --------------------------------------------------------------------------- timing
#
# The pipelines only report a few milestones ("Loading model", "Baking texture" ...),
# and one milestone can take minutes, so the bar used to stand still and then jump.
# To show real progress the server remembers how long every stage took on the last
# successful run on THIS machine (logs/stage_times.json). During the next run the bar
# moves with the clock inside each stage, and the server can also say how many seconds
# are left. The very first run has no history, so it falls back to the milestones.

TIMES_FILE = LOG_DIR / "stage_times.json"
TIMES_LOCK = threading.Lock()


def _load_times() -> dict:
    try:
        return json.loads(TIMES_FILE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


STAGE_TIMES: dict = _load_times()   # {"run_t2i": [["Starting", 3.1], ["Loading text encoder", 8.0], ...]}


def stage_key(stage: str) -> str:
    """'Generating image (2/4)' and 'Generating image (3/4)' are the same stage."""
    return re.sub(r"\s*\(.*\)\s*$", "", stage or "").strip()


def expected_total(key: str) -> float:
    return sum(seconds for _, seconds in STAGE_TIMES.get(key, []))


def remember_times(key: str, marks: list, finished: float) -> None:
    """marks = [(stage name, start time)] of a run that succeeded."""
    measured = []
    for index, (name, started) in enumerate(marks):
        ended = marks[index + 1][1] if index + 1 < len(marks) else finished
        measured.append([name, round(max(0.05, ended - started), 2)])

    with TIMES_LOCK:
        old = STAGE_TIMES.get(key, [])
        if [n for n, _ in old] == [n for n, _ in measured]:
            # Same stages as before: average with the old numbers so one odd run
            # (a first run that had to load everything from disk) does not dominate.
            measured = [[n, round(0.5 * o + 0.5 * m, 2)] for (n, m), (_, o) in zip(measured, old)]
        STAGE_TIMES[key] = measured
        try:
            TIMES_FILE.write_text(json.dumps(STAGE_TIMES, indent=1), encoding="utf-8")
        except OSError:
            pass


def model_split() -> float:
    """Share of the model phase that is shape generation (the rest is texture baking)."""
    shape, paint = expected_total("run_hunyuan_shape"), expected_total("run_hunyuan_paint")
    if shape > 0 and paint > 0:
        return min(0.9, max(0.1, shape / (shape + paint)))
    return 0.6


# --------------------------------------------------------------------------- jobs


class Job:
    def __init__(self, prompt: str, operation: str = "add") -> None:
        self.id = uuid.uuid4().hex[:12]
        self.prompt = prompt
        self.operation = operation      # add | texture
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
        # Timing of the pipeline that is running now (see "timing" above).
        self.run_key = ""               # run_t2i | run_hunyuan_shape | run_hunyuan_paint
        self.run_scale = (0.0, 1.0)
        self.run_marks: list = []       # [(stage name, start time)]
        self.run_after = 0.0            # seconds expected for the pipeline that follows
        self.shown = 0.0                # the bar never moves backwards

    def live(self) -> tuple:
        """(progress 0..1, seconds left or -1 when unknown), using the clock."""
        if self.status != "running" or not self.run_marks:
            return self.progress, -1.0

        expected = STAGE_TIMES.get(self.run_key, [])
        names = [name for name, _ in expected]
        current, started = self.run_marks[-1]

        if current not in names:
            return max(self.progress, self.shown), -1.0     # no history yet: milestones

        index = names.index(current)
        total = sum(seconds for _, seconds in expected) or 1.0
        before = sum(seconds for _, seconds in expected[:index])
        length = expected[index][1]
        spent = time.time() - started

        # Inside the stage follow the clock. If it takes longer than last time, slow
        # down and creep towards the end of the stage instead of stopping dead.
        if spent <= 0.9 * length:
            part = spent
        else:
            over = spent - 0.9 * length
            part = length * (0.9 + 0.09 * (1.0 - math.exp(-over / max(length, 1.0))))

        fraction = (before + part) / total
        lo, hi = self.run_scale
        value = max(self.shown, min(0.99, lo + (hi - lo) * fraction))
        self.shown = value
        left = max(0.0, total - before - min(spent, length)) + self.run_after
        return value, left

    def to_dict(self) -> dict:
        return {
            "jobId": self.id,
            "prompt": self.prompt,
            "operation": self.operation,
            "status": self.status,
            "phase": self.phase,
            "stage": self.stage,
            "progress": round(self.live()[0], 3),
            "etaSeconds": round(self.live()[1], 1),
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
        torch_lib = Path(python).parent.parent / "Lib" / "site-packages" / "torch" / "lib"
        cuda_bin = Path(os.environ.get("CUDA_PATH", "C:/Program Files/NVIDIA GPU Computing Toolkit/CUDA/v12.8")) / "bin"
        env["PATH"] = os.pathsep.join((str(cuda_bin), str(torch_lib), env.get("PATH", "")))

    with RUN_LOCK:
        if job.status == "cancelled":
            return
        key = Path(argv[0]).stem
        lo, hi = scale
        job.set(status="running", phase=phase, stage="Starting", progress=lo, message="",
                run_key=key, run_scale=scale, run_marks=[("Starting", time.time())],
                run_after=expected_total("run_hunyuan_paint") if then is not None else 0.0,
                shown=lo)
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
                        stage = parts[2].strip() if len(parts) > 2 else ""
                        name = stage_key(stage)
                        if name and name != job.run_marks[-1][0]:
                            job.run_marks.append((name, time.time()))
                        job.set(progress=lo + (hi - lo) * raw, stage=stage)
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
    remember_times(key, job.run_marks, time.time())
    if then is not None:
        then(job)
        return

    job.set(status=on_success, progress=1.0, stage="Ready", message="")


def start_image(job: Job) -> None:
    if job.image.exists():
        job.image.unlink()
    argv = [str(ROOT / "pipelines" / "run_t2i.py"), "--prompt", job.prompt, "--out", str(job.image)]
    if getattr(job, "operation", "add") == "texture":
        argv += ["--mode", "texture"]   # flat material image, not an object on white
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

    # With paint installed, shape is the first part of the model phase and texture the rest.
    # The share comes from measured times (60% until both have been measured once).
    if paint_available():
        threading.Thread(
            target=run_pipeline,
            args=(job, argv, "model", "done"),
            kwargs={"scale": (0.0, model_split()), "then": start_paint},
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
        target=lambda: (run_pipeline(job, argv, "model", "done", scale=(model_split(), 1.0)), finish()),
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
    job = Job(body.prompt.strip(), operation=body.operation.strip() or "add")
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
    if getattr(job, "operation", "add") == "texture":
        raise HTTPException(status_code=409, detail="texture jobs keep the existing mesh; apply the image in Unity")
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
