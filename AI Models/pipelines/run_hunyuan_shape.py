"""image.png -> shape.glb with Hunyuan3D-2mini-Turbo.

This is the Colab T4 path that produced the chair GLB:
no FlashVDM, no CPU offload, octree 256, weights loaded on CUDA.
Do not load Pipeline A in this process. Texture is applied in Unity, not here.
"""
from __future__ import annotations

import gc
import os
import sys
from pathlib import Path

import torch
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
HY3D = ROOT / "deps" / "Hunyuan3D-2"
os.environ.setdefault("HF_HOME", str(ROOT / "hf_cache"))
os.environ.setdefault("HY3DGEN_MODELS", str(ROOT / "hf_cache" / "hy3dgen"))
sys.path.insert(0, str(HY3D))

IMAGE = ROOT / "image.png"
OUT = ROOT / "shape.glb"
STEPS = 5
OCTREE = 256
CHUNKS = 20000
SEED = 12345


def main() -> None:
    if not (HY3D / "hy3dgen").is_dir():
        raise SystemExit(f"missing {HY3D}/hy3dgen; run bash scripts/setup_b.sh")
    if not IMAGE.exists():
        raise SystemExit(f"missing {IMAGE}; run Pipeline A or copy image.png here")
    if not torch.cuda.is_available():
        raise SystemExit("CUDA required")

    from hy3dgen.shapegen import Hunyuan3DDiTFlowMatchingPipeline

    print("device", torch.cuda.get_device_name(0), flush=True)
    print("image", IMAGE, IMAGE.exists(), flush=True)
    print("loading mini-turbo (no FlashVDM, no CPU offload)", flush=True)
    pipeline = Hunyuan3DDiTFlowMatchingPipeline.from_pretrained(
        "tencent/Hunyuan3D-2mini",
        subfolder="hunyuan3d-dit-v2-mini-turbo",
        use_safetensors=False,
        device="cuda",
    )
    print("from_pretrained done", flush=True)
    print("running", STEPS, "steps, octree", OCTREE, flush=True)
    mesh = pipeline(
        image=Image.open(IMAGE).convert("RGBA"),
        num_inference_steps=STEPS,
        octree_resolution=OCTREE,
        num_chunks=CHUNKS,
        generator=torch.manual_seed(SEED),
        output_type="trimesh",
    )[0]
    mesh.export(str(OUT))
    print("done ->", OUT, "bytes", OUT.stat().st_size, flush=True)
    del pipeline, mesh
    gc.collect()
    torch.cuda.empty_cache()


if __name__ == "__main__":
    main()
