"""Pipeline B: image.png -> object.glb (TripoSR).

Run only inside .venv-b after setup_b.sh. Exit Pipeline A first.
Does not import rembg, moderngl, or torchmcubes.
"""
import os
import sys
from pathlib import Path

import torch
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
TSR_DIR = ROOT / "deps" / "TripoSR"
INP = ROOT / "image.png"
OUT = ROOT / "object.glb"

os.environ.setdefault("HF_HOME", str(ROOT / "hf_cache"))
sys.path.insert(0, str(TSR_DIR))


def main() -> None:
    if not INP.exists():
        raise SystemExit(f"missing {INP}; run Pipeline A first")
    if not (TSR_DIR / "tsr").is_dir():
        raise SystemExit("missing deps/TripoSR; run scripts/setup_b.sh")
    if not torch.cuda.is_available():
        raise SystemExit("CUDA required for Pipeline B")

    from tsr.system import TSR

    device = "cuda"
    print("device", torch.cuda.get_device_name(0))

    model = TSR.from_pretrained(
        "stabilityai/TripoSR",
        config_name="config.yaml",
        weight_name="model.ckpt",
    )
    model.renderer.set_chunk_size(8192)
    model.to(device)

    image = Image.open(INP).convert("RGB")
    with torch.no_grad():
        codes = model([image], device=device)
    meshes = model.extract_mesh(codes, True, resolution=128)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    meshes[0].export(str(OUT))
    print("done ->", OUT, "bytes", OUT.stat().st_size)


if __name__ == "__main__":
    main()
