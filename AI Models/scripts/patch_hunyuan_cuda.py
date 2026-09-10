"""Force Hunyuan checkpoints onto the GPU.

The stock loader uses map_location='cpu'. On Colab (~12 GB RAM) that fills
system RAM, GPU stays at 0 GB, and the runtime dies. This is the patch that
unblocked shape.glb on the T4.
"""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PIPE = ROOT / "deps" / "Hunyuan3D-2" / "hy3dgen" / "shapegen" / "pipelines.py"

OLD_LOAD = "torch.load(ckpt_path, map_location='cpu', weights_only=True)"
NEW_LOAD = "torch.load(ckpt_path, map_location='cuda', weights_only=True)"
OLD_SF = "safetensors.torch.load_file(ckpt_path, device='cpu')"
NEW_SF = "safetensors.torch.load_file(ckpt_path, device='cuda')"


def main() -> None:
    if not PIPE.exists():
        raise SystemExit(f"missing {PIPE}; clone Hunyuan3D-2 first")
    text = PIPE.read_text()
    if NEW_LOAD in text and NEW_SF in text:
        print("already patched", PIPE)
        return
    if OLD_LOAD not in text:
        raise SystemExit(f"expected {OLD_LOAD!r} in {PIPE}; Hunyuan source changed")
    text = text.replace(OLD_LOAD, NEW_LOAD)
    text = text.replace(OLD_SF, NEW_SF)
    PIPE.write_text(text)
    print("patched", PIPE)


if __name__ == "__main__":
    main()
