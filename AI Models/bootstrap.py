"""One-time setup for the CreativeTwin AI pipelines, safe to re-run.

    python bootstrap.py            set up whatever is missing, then print a status table
    python bootstrap.py --check    only report: what is present, where, what is missing
    python bootstrap.py --serve    set up if needed, then start the server

What it does, idempotently, with a log in logs/bootstrap-*.log:
  1. detect OS, Python, git, NVIDIA driver / GPU / VRAM
  2. create .venv-a (FLUX), .venv-b (Hunyuan3D), .venv-server (FastAPI) if missing
  3. install the CUDA build of torch that matches the driver, plus each requirements file
  4. clone Hunyuan3D-2 into deps/, install hy3dgen, patch its loader onto the GPU
  5. download every model weight up front, so the first Generate is not a surprise
  6. write status.json - the server's /health reads it, Unity reads /health

With no NVIDIA GPU it still installs the server (so /health can say so), skips the
multi-gigabyte downloads unless --force, and tells you exactly what would be needed.

Standard library only, so it runs before anything is installed.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import platform
import shutil
import subprocess
import sys
import textwrap
from pathlib import Path

ROOT = Path(__file__).resolve().parent
LOG_DIR = ROOT / "logs"
STATUS_FILE = ROOT / "status.json"
DEPS_DIR = ROOT / "deps"
HUNYUAN_DIR = DEPS_DIR / "Hunyuan3D-2"
HF_HOME = ROOT / "hf_cache"

FLUX_REPO = "black-forest-labs/FLUX.2-klein-4B"
GGUF_REPO = "unsloth/FLUX.2-klein-4B-GGUF"
GGUF_FILE = "flux-2-klein-4b-Q8_0.gguf"
HUNYUAN_REPO = "tencent/Hunyuan3D-2mini"
HUNYUAN_SUBFOLDER = "hunyuan3d-dit-v2-mini-turbo"
HUNYUAN_GIT = "https://github.com/Tencent-Hunyuan/Hunyuan3D-2.git"
HUNYUAN_PAINT_REPO = "tencent/Hunyuan3D-2"
HUNYUAN_PAINT_SUBFOLDER = "hunyuan3d-paint-v2-0-turbo"

LOG_DIR.mkdir(exist_ok=True)
LOG_FILE = LOG_DIR / f"bootstrap-{dt.datetime.now():%Y%m%d-%H%M%S}.log"


# --------------------------------------------------------------------------- logging


def log(message: str = "", *, level: str = "INFO") -> None:
    line = f"[{dt.datetime.now():%H:%M:%S}] {level:<5} {message}" if message else ""
    print(line, flush=True)
    with open(LOG_FILE, "a", encoding="utf-8") as handle:
        handle.write(line + "\n")


def run(cmd: list[str], *, cwd: Path | None = None, env: dict | None = None, check: bool = True) -> int:
    log("$ " + " ".join(str(c) for c in cmd))
    proc = subprocess.Popen(
        [str(c) for c in cmd], cwd=str(cwd or ROOT), env=env,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace",
    )
    with open(LOG_FILE, "a", encoding="utf-8") as handle:
        for line in proc.stdout:
            handle.write(line)
            # keep the console readable: pip prints a lot
            if any(k in line for k in ("Successfully", "error", "Error", "ERROR", "Cloning", "done", "##")):
                print("   " + line.rstrip(), flush=True)
    code = proc.wait()
    if check and code != 0:
        raise RuntimeError(f"command failed ({code}): {' '.join(str(c) for c in cmd)} - see {LOG_FILE.name}")
    return code


# --------------------------------------------------------------------------- detection


def venv_python(name: str) -> Path:
    return ROOT / name / ("Scripts/python.exe" if os.name == "nt" else "bin/python")


def detect_gpu() -> dict:
    exe = shutil.which("nvidia-smi")
    if not exe:
        return {"available": False, "reason": "nvidia-smi not found (no NVIDIA driver?)"}
    try:
        out = subprocess.run(
            [exe, "--query-gpu=name,memory.total,driver_version,compute_cap",
             "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=15, check=True,
        ).stdout.strip().splitlines()
    except Exception as exc:
        return {"available": False, "reason": f"nvidia-smi failed: {exc}"}
    if not out:
        return {"available": False, "reason": "driver present, no GPU reported"}
    parts = [p.strip() for p in out[0].split(",")]
    name, mem, driver = parts[0], parts[1], parts[2]
    arch = parts[3] if len(parts) > 3 and parts[3] not in ("", "[N/A]") else ""
    return {"available": True, "name": name, "vramMb": int(float(mem)),
            "driver": driver, "computeCap": arch}


def system_ram_mb() -> int:
    """Hunyuan's stock loader maps checkpoints to CPU; low system RAM is what killed
    the team's Colab runs, so it is worth reporting alongside VRAM."""
    try:
        if os.name == "nt":
            import ctypes

            class Status(ctypes.Structure):
                _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong),
                            ("ullTotalPhys", ctypes.c_ulonglong), ("ullAvailPhys", ctypes.c_ulonglong),
                            ("ullTotalPageFile", ctypes.c_ulonglong), ("ullAvailPageFile", ctypes.c_ulonglong),
                            ("ullTotalVirtual", ctypes.c_ulonglong), ("ullAvailVirtual", ctypes.c_ulonglong),
                            ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]

            status = Status()
            status.dwLength = ctypes.sizeof(Status)
            ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status))
            return int(status.ullTotalPhys / (1024 * 1024))
        return int(os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES") / (1024 * 1024))
    except Exception:
        return 0


def choose_cuda_tag(gpu: dict, override: str | None) -> str:
    """Pick the torch wheel index that matches the installed driver."""
    if override:
        return override
    try:
        major = int(gpu.get("driver", "0").split(".")[0])
    except ValueError:
        major = 0
    if major >= 570:
        return "cu128"
    if major >= 550:
        return "cu126"
    return "cu121"


def recommended_settings(gpu: dict) -> dict:
    vram = gpu.get("vramMb", 0)
    return {
        # 256 is the octree the team verified on a T4; more VRAM buys a finer grid.
        "octreeResolution": 384 if vram >= 20000 else 256,
        "steps": 5,
        "imageSize": 1024,
        "note": "Hunyuan3D-2mini-Turbo, shape only. Texture is applied in Unity.",
    }


# --------------------------------------------------------------------------- setup steps


def ensure_venv(name: str) -> Path:
    python = venv_python(name)
    if python.exists():
        log(f"{name}: present at {python}")
        return python
    log(f"{name}: creating")
    run([sys.executable, "-m", "venv", str(ROOT / name)])
    run([python, "-m", "pip", "install", "--upgrade", "pip", "wheel"])
    return python


def install_torch(python: Path, cuda_tag: str) -> None:
    code = subprocess.run([str(python), "-c", "import torch; print(torch.__version__)"], capture_output=True).returncode
    if code == 0:
        log(f"torch already installed in {python.parent.parent.name}")
        return
    run([python, "-m", "pip", "install", "torch", "torchvision",
         "--index-url", f"https://download.pytorch.org/whl/{cuda_tag}"])


def install_requirements(python: Path, requirements: Path) -> None:
    run([python, "-m", "pip", "install", "-r", str(requirements)])


def ensure_hunyuan(python_b: Path) -> bool:
    """Clone Hunyuan3D-2, install hy3dgen in place, then patch its loader onto the GPU.

    Mirrors scripts/setup_b.sh, which is bash-only; this path also works on Windows.
    """
    if (HUNYUAN_DIR / "hy3dgen").is_dir():
        log(f"Hunyuan3D-2 code: present at {HUNYUAN_DIR}")
    else:
        if not shutil.which("git"):
            log("git not found - cannot clone Hunyuan3D-2", level="ERROR")
            return False
        DEPS_DIR.mkdir(exist_ok=True)
        run(["git", "clone", "--depth", "1", HUNYUAN_GIT, str(HUNYUAN_DIR)])

    # --no-deps: requirements-b.txt owns the dependency set, and the repo's own list
    # pulls extras (torch included) that would fight the CUDA build we installed.
    run([python_b, "-m", "pip", "install", "-e", str(HUNYUAN_DIR), "--no-deps"], check=False)

    patch = ROOT / "scripts" / "patch_hunyuan_cuda.py"
    if patch.exists():
        run([python_b, str(patch)], check=False)

    build_texgen_kernels(python_b)

    return True


def build_texgen_kernels(python_b: Path) -> None:
    """Compile the two C++/CUDA extensions the paint stage needs.

    Non-fatal: shape generation works without them, and paint is optional. A build
    needs a compiler toolchain, so it is expected to fail on some machines.
    """
    for rel in (
        Path("hy3dgen") / "texgen" / "custom_rasterizer",
        Path("hy3dgen") / "texgen" / "differentiable_renderer",
    ):
        target = HUNYUAN_DIR / rel
        if not target.is_dir():
            log(f"paint kernels: {rel} not in the clone; skipping", level="WARN")
            continue
        log(f"paint kernels: building {rel.name}")
        code = run([python_b, "-m", "pip", "install", "-e", str(target), "--no-deps"],
                   check=False)
        if code != 0:
            log(f"paint kernels: {rel.name} failed to build - texture baking will be unavailable, shape still works", level="WARN")


def download_models(python_a: Path, python_b: Path, skip_paint: bool = False) -> dict:
    env = dict(os.environ, HF_HOME=str(HF_HOME), HF_HUB_DISABLE_PROGRESS_BARS="1")
    HF_HOME.mkdir(exist_ok=True)

    flux_script = textwrap.dedent(f"""
        from huggingface_hub import snapshot_download, hf_hub_download
        p = snapshot_download("{FLUX_REPO}", allow_patterns=[
            "*.json", "tokenizer/*", "text_encoder/*", "vae/*", "scheduler/*", "transformer/config.json"])
        g = hf_hub_download("{GGUF_REPO}", "{GGUF_FILE}")
        print("##FLUX", p); print("##GGUF", g)
    """)
    log("FLUX.2-klein-4B: downloading config, tokenizer, text encoder, VAE and Q8 GGUF (several GB)")
    run([python_a, "-c", flux_script], env=env)

    hunyuan_script = textwrap.dedent(f"""
        from huggingface_hub import snapshot_download
        p = snapshot_download("{HUNYUAN_REPO}", allow_patterns=["{HUNYUAN_SUBFOLDER}/*"])
        print("##HY3D", p)
    """)
    log(f"Hunyuan3D-2mini: downloading {HUNYUAN_SUBFOLDER} (several GB)")
    run([python_b, "-c", hunyuan_script], env=env)

    paint_ready = False
    if not skip_paint:
        paint_script = textwrap.dedent(f"""
            from huggingface_hub import snapshot_download
            p = snapshot_download("{HUNYUAN_PAINT_REPO}", allow_patterns=["{HUNYUAN_PAINT_SUBFOLDER}/*"])
            print("##PAINT", p)
        """)
        log(f"Hunyuan3D-Paint: downloading {HUNYUAN_PAINT_SUBFOLDER} (large; --skip-paint to omit)")
        paint_ready = run([python_b, "-c", paint_script], env=env, check=False) == 0
        if not paint_ready:
            log("paint weights failed to download - texture baking unavailable", level="WARN")

    return {
        "flux": {"ready": True, "repo": FLUX_REPO, "gguf": GGUF_FILE, "cache": str(HF_HOME)},
        "hunyuan": {"ready": True, "repo": HUNYUAN_REPO, "subfolder": HUNYUAN_SUBFOLDER,
                    "code": str(HUNYUAN_DIR), "cache": str(HF_HOME)},
        "paint": {"ready": paint_ready, "repo": HUNYUAN_PAINT_REPO,
                  "subfolder": HUNYUAN_PAINT_SUBFOLDER, "optional": True},
    }


def models_present() -> dict:
    """Cheap check used by --check: look for the cached repos on disk."""
    hub = HF_HOME / "hub"

    def cached(repo: str) -> bool:
        return (hub / ("models--" + repo.replace("/", "--"))).is_dir()

    return {
        "flux": {"ready": cached(FLUX_REPO) and cached(GGUF_REPO), "cache": str(HF_HOME)},
        "hunyuan": {"ready": cached(HUNYUAN_REPO) and (HUNYUAN_DIR / "hy3dgen").is_dir(),
                    "code": str(HUNYUAN_DIR)},
        "paint": {"ready": cached(HUNYUAN_PAINT_REPO), "optional": True},
    }


# --------------------------------------------------------------------------- status


def write_status(gpu: dict, cuda_tag: str, models: dict) -> dict:
    status = {
        "bootstrappedAt": dt.datetime.now().isoformat(timespec="seconds"),
        "platform": f"{platform.system()} {platform.release()} ({platform.machine()})",
        "python": sys.version.split()[0],
        "root": str(ROOT),
        "gpu": gpu,
        "systemRamMb": system_ram_mb(),
        "cudaTag": cuda_tag,
        "archList": gpu.get("computeCap", ""),
        "venvs": {n: str(venv_python(n)) for n in (".venv-a", ".venv-b", ".venv-server")},
        "models": models,
        "recommended": recommended_settings(gpu),
        "log": str(LOG_FILE),
    }
    STATUS_FILE.write_text(json.dumps(status, indent=2), encoding="utf-8")
    return status


def print_table(gpu: dict, models: dict, cuda_tag: str) -> list[str]:
    missing: list[str] = []

    def row(label: str, ok: bool, detail: str) -> None:
        mark = "OK " if ok else "-- "
        log(f"  {mark} {label:<22} {detail}")

    log()
    log("CreativeTwin AI - status")
    row("platform", True, f"{platform.system()} {platform.release()}  python {sys.version.split()[0]}")
    row("git", bool(shutil.which("git")), shutil.which("git") or "not found")
    if gpu["available"]:
        arch = f"  arch {gpu['computeCap']}" if gpu.get('computeCap') else ''
        row("gpu", True, f"{gpu['name']}  {gpu['vramMb'] / 1024:.0f} GB  driver {gpu['driver']}{arch}  -> torch {cuda_tag}")
    else:
        row("gpu", False, gpu.get("reason", "none"))
        missing.append("NVIDIA GPU with a working driver")
    for name in (".venv-a", ".venv-b", ".venv-server"):
        present = venv_python(name).exists()
        row(name, present, str(venv_python(name)) if present else "missing")
        if not present:
            missing.append(f"{name} (run bootstrap.py)")
    ram = system_ram_mb()
    row("system RAM", ram == 0 or ram >= 16000,
        (f"{ram / 1024:.0f} GB" + ("" if ram >= 16000 else "   (16 GB+ recommended: Hunyuan loads large checkpoints)"))
        if ram else "unknown")
    paint = models.get("paint", {})
    row("Hunyuan3D-Paint", paint.get("ready", False),
        "texture baking ready" if paint.get("ready")
        else "not installed - meshes come out untextured (optional)")
    for key, label in (("flux", "FLUX.2-klein-4B"), ("hunyuan", "Hunyuan3D-2mini-Turbo")):
        info = models.get(key, {})
        row(label, info.get("ready", False), info.get("cache", info.get("code", "not downloaded")))
        if not info.get("ready"):
            missing.append(f"{label} weights (run bootstrap.py)")
    log()
    if missing:
        log("Not ready. Missing:")
        for item in missing:
            log(f"   - {item}")
        log("Unity will run in placeholder mode until these are present.")
    else:
        log("Ready. Start the server with:  python start.py")
    log(f"Full log: {LOG_FILE}")
    return missing


# --------------------------------------------------------------------------- main


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="report only; change nothing")
    parser.add_argument("--serve", action="store_true", help="start the server after setup")
    parser.add_argument("--cuda", help="override the torch CUDA tag, e.g. cu126")
    parser.add_argument("--skip-models", action="store_true", help="do not download weights")
    parser.add_argument("--skip-paint", action="store_true",
                        help="skip the optional texture-baking model (large)")
    parser.add_argument("--force", action="store_true", help="set up pipelines even with no GPU")
    args = parser.parse_args(argv)

    if sys.version_info < (3, 10):
        log(f"Python 3.10+ required, found {sys.version.split()[0]}", level="ERROR")
        return 2

    gpu = detect_gpu()
    cuda_tag = choose_cuda_tag(gpu, args.cuda)

    if args.check:
        print_table(gpu, models_present(), cuda_tag)
        return 0

    log(f"bootstrap starting in {ROOT}")

    # The server is cheap and useful even without a GPU: Unity asks it what is missing.
    server_python = ensure_venv(".venv-server")
    install_requirements(server_python, ROOT / "server" / "requirements-server.txt")

    models = models_present()

    if not gpu["available"] and not args.force:
        log("No NVIDIA GPU detected - skipping pipeline virtualenvs and model downloads "
            "(use --force to set them up anyway).", level="WARN")
    else:
        python_a = ensure_venv(".venv-a")
        install_torch(python_a, cuda_tag)
        install_requirements(python_a, ROOT / "requirements-a.txt")

        python_b = ensure_venv(".venv-b")
        install_torch(python_b, cuda_tag)
        install_requirements(python_b, ROOT / "requirements-b.txt")

        ensure_hunyuan(python_b)

        if args.skip_models:
            log("--skip-models: weights not downloaded")
        else:
            models = download_models(python_a, python_b, skip_paint=args.skip_paint)

    write_status(gpu, cuda_tag, models)
    missing = print_table(gpu, models, cuda_tag)

    if args.serve:
        server = ROOT / "server" / "server.py"
        log("starting server")
        os.execv(str(server_python), [str(server_python), str(server)])

    return 0 if not missing else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except RuntimeError as exc:
        log(str(exc), level="ERROR")
        sys.exit(1)
