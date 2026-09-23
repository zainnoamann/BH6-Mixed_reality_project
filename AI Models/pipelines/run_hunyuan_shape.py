"""image.png -> shape.glb with Hunyuan3D-2mini-Turbo.

This is the Colab T4 path that produced the chair GLB:
no FlashVDM, no CPU offload, octree 256, weights loaded on CUDA.
Do not load Pipeline A in this process. Texture is applied in Unity, not here.

Run it by hand exactly as before (defaults are unchanged):

    python pipelines/run_hunyuan_shape.py

or with explicit paths, which is how server/server.py drives it:

    python pipelines/run_hunyuan_shape.py --input <in.png> --output <out.glb>
                                          [--octree 256] [--steps 5] [--seed 12345]

Progress is printed as lines the server parses:  ##PROGRESS <0..1> <stage text>
"""
from __future__ import annotations

import argparse
import gc
import importlib.metadata
import os
import sys
from pathlib import Path


def _configure_cuda_allocator() -> None:
    """Set PYTORCH_CUDA_ALLOC_CONF before torch's first CUDA init.

    expandable_segments is the right fix for '23 GiB free, cannot allocate 12 MiB'
    on PyTorch 2.2+. Older builds reject that key and crash on get_device_name.
    """
    try:
        ver = importlib.metadata.version("torch").split("+", 1)[0]
        major, minor = (int(p) for p in ver.split(".")[:2])
    except Exception:
        major, minor = 0, 0
    parts = [p.strip() for p in os.environ.get("PYTORCH_CUDA_ALLOC_CONF", "").split(",") if p.strip()]
    supports_expandable = major > 2 or (major == 2 and minor >= 2)
    if not supports_expandable:
        parts = [p for p in parts if "expandable_segments" not in p]
        if not any(p.startswith("max_split_size_mb") for p in parts):
            parts.append("max_split_size_mb:128")
    elif not any("expandable_segments" in p for p in parts):
        parts.append("expandable_segments:True")
    if parts:
        os.environ["PYTORCH_CUDA_ALLOC_CONF"] = ",".join(parts)


_configure_cuda_allocator()

import torch
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
HY3D = ROOT / "deps" / "Hunyuan3D-2"
os.environ.setdefault("HF_HOME", str(ROOT / "hf_cache"))
os.environ.setdefault("HY3DGEN_MODELS", str(ROOT / "hf_cache" / "hy3dgen"))
sys.path.insert(0, str(HY3D))

# Defaults: the exact settings that worked on the T4.
IMAGE = ROOT / "image.png"
OUT = ROOT / "shape.glb"
STEPS = 5
OCTREE = 256
CHUNKS = 20000
SEED = 12345


def progress(value: float, stage: str) -> None:
    print(f"##PROGRESS {max(0.0, min(1.0, value)):.3f} {stage}", flush=True)


def _is_wsl() -> bool:
    if os.environ.get("WSL_DISTRO_NAME"):
        return True
    try:
        return "microsoft" in Path("/proc/version").read_text().lower()
    except Exception:
        return False


def _move_pipeline_to_cuda(pipeline):
    if hasattr(pipeline, "to"):
        pipeline.to("cuda")
        return pipeline
    for attr in ("model", "conditioner", "vae"):
        part = getattr(pipeline, attr, None)
        if part is not None and hasattr(part, "to"):
            setattr(pipeline, attr, part.to("cuda"))
    if hasattr(pipeline, "device"):
        pipeline.device = torch.device("cuda")
    return pipeline


def load_image(path: Path):
    """
    Hunyuan wants the subject separated from the background. FLUX gives us an opaque
    RGB image on white, so converting to RGBA alone leaves alpha fully opaque and the
    model has to guess what is subject and what is backdrop - which costs mesh quality.
    Use Hunyuan's own background remover when the clone provides it, and fall back to
    the plain convert so this never becomes a hard failure.
    """
    image = Image.open(path)

    if image.mode == "RGBA" and image.getextrema()[3][0] < 255:
        progress(0.30, "Using existing alpha")
        return image

    try:
        from hy3dgen.rembg import BackgroundRemover

        progress(0.30, "Removing background")
        return BackgroundRemover()(image.convert("RGB"))
    except Exception as exc:
        print(f"background removal unavailable ({type(exc).__name__}: {exc}); "
              f"using the image as-is", flush=True)
        return image.convert("RGBA")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", default=str(IMAGE))
    parser.add_argument("--output", default=str(OUT))
    parser.add_argument("--octree", type=int, default=OCTREE)
    parser.add_argument("--steps", type=int, default=STEPS)
    parser.add_argument("--chunks", type=int, default=CHUNKS)
    parser.add_argument("--seed", type=int, default=SEED)
    parser.add_argument(
        "--load-device",
        choices=("auto", "cuda", "cpu"),
        default="auto",
        help="Where convert() runs. auto = CPU on WSL (5090 CUDA convert OOMs "
             "with 23 GiB free), CUDA elsewhere. steps/octree/chunks are unused until after load.",
    )
    args = parser.parse_args()

    inp = Path(args.input)
    out = Path(args.output)

    if not (HY3D / "hy3dgen").is_dir():
        raise SystemExit(f"missing {HY3D}/hy3dgen; run python bootstrap.py")
    if not inp.exists():
        raise SystemExit(f"missing {inp}; run Pipeline A first")
    if not torch.cuda.is_available():
        raise SystemExit("CUDA required")

    from hy3dgen.shapegen import Hunyuan3DDiTFlowMatchingPipeline

    print("device", torch.cuda.get_device_name(0), flush=True)
    print("image", inp, inp.exists(), flush=True)

    progress(0.05, "Loading mini-turbo")
    print("loading mini-turbo (no FlashVDM, no CPU offload)", flush=True)
    print("torch", torch.__version__, "cuda", torch.version.cuda,
          "alloc", os.environ.get("PYTORCH_CUDA_ALLOC_CONF", ""), flush=True)

    def load_pipeline(device: str):
        return Hunyuan3DDiTFlowMatchingPipeline.from_pretrained(
            "tencent/Hunyuan3D-2mini",
            subfolder="hunyuan3d-dit-v2-mini-turbo",
            use_safetensors=False,
            device=device,
        )

    load_device = args.load_device
    if load_device == "auto":
        load_device = "cpu" if _is_wsl() else "cuda"
    print("load_device", load_device, "(octree/steps/chunks unused until after this)", flush=True)

    # CUDA convert() on WSL+5090 fails a 12 MiB alloc with ~23 GiB reported free.
    # Convert on CPU, then move the finished module. Inference still uses the GPU.
    if load_device == "cpu":
        pipeline = _move_pipeline_to_cuda(load_pipeline("cpu"))
    else:
        try:
            pipeline = load_pipeline("cuda")
        except torch.cuda.OutOfMemoryError as exc:
            print(f"cuda load OOM ({exc}); retrying CPU load then .to(cuda)", flush=True)
            gc.collect()
            torch.cuda.empty_cache()
            pipeline = _move_pipeline_to_cuda(load_pipeline("cpu"))
    print("from_pretrained done", flush=True)

    image = load_image(inp)

    progress(0.40, f"Generating shape ({args.steps} steps, octree {args.octree})")
    print("running", args.steps, "steps, octree", args.octree, flush=True)

    mesh = pipeline(
        image=image,
        num_inference_steps=args.steps,
        octree_resolution=args.octree,
        num_chunks=args.chunks,
        generator=torch.manual_seed(args.seed),
        output_type="trimesh",
    )[0]

    progress(0.90, "Writing GLB")
    out.parent.mkdir(parents=True, exist_ok=True)
    mesh.export(str(out))

    progress(1.0, "Model ready")
    print("done ->", out, "bytes", out.stat().st_size, flush=True)

    del pipeline, mesh
    gc.collect()
    torch.cuda.empty_cache()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:  # make failures visible to the server log
        print(f"##ERROR {type(exc).__name__}: {exc}", flush=True)
        raise
