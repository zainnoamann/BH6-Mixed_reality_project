"""Make Hunyuan load its checkpoint on the device the caller asked for.

The stock loader always uses map_location='cpu'. On Colab (~12 GB RAM) that
fills system RAM, GPU stays at 0 GB, and the runtime dies.

The first version of this patch hard-coded map_location='cuda'. That fixed
Colab, but it also meant `--load-device cpu` (the WSL + 5090 path) still put
the whole fp32 checkpoint on the GPU first. That extra copy is the ~6.4 GiB
"allocated by PyTorch" seen in every WSL OOM, and it stays on the GPU while
the model is converted and moved.

This version uses the `device` argument that from_single_file already has:
  Colab:  device='cuda' -> checkpoint on GPU (same as before)
  WSL:    device='cpu'  -> checkpoint stays in RAM, then .to('cuda') once

Safe to run again: it upgrades both the stock file and the old 'cuda' patch.
"""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PIPE = ROOT / "deps" / "Hunyuan3D-2" / "hy3dgen" / "shapegen" / "pipelines.py"

NEW_LOAD = "torch.load(ckpt_path, map_location=device, weights_only=True)"
NEW_SF = "safetensors.torch.load_file(ckpt_path, device=str(device))"
OLD_LOADS = (
    "torch.load(ckpt_path, map_location='cpu', weights_only=True)",
    "torch.load(ckpt_path, map_location='cuda', weights_only=True)",
)
OLD_SFS = (
    "safetensors.torch.load_file(ckpt_path, device='cpu')",
    "safetensors.torch.load_file(ckpt_path, device='cuda')",
)


def main() -> None:
    if not PIPE.exists():
        raise SystemExit(f"missing {PIPE}; clone Hunyuan3D-2 first")
    text = PIPE.read_text()
    if NEW_LOAD in text and NEW_SF in text:
        print("already patched", PIPE)
        return
    if NEW_LOAD not in text and not any(old in text for old in OLD_LOADS):
        raise SystemExit(f"expected a torch.load(ckpt_path, ...) line in {PIPE}; Hunyuan source changed")
    for old in OLD_LOADS:
        text = text.replace(old, NEW_LOAD)
    for old in OLD_SFS:
        text = text.replace(old, NEW_SF)
    PIPE.write_text(text)
    print("patched (map_location follows device)", PIPE)


if __name__ == "__main__":
    main()
