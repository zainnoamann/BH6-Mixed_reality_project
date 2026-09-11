"""Bake image.png onto shape.glb with Hunyuan3D-Paint turbo.

Colab T4 path that wrote textured.glb:
- skip Delight
- torch.load / safetensors onto CUDA (CPU load fills 12.7 GB RAM and kills the runtime)
- trust_remote_code for hunyuanpaint/pipeline.py
- move leftover modules (unet, unet_ref, vae) onto CUDA
- texture 512, 30 denoise steps (pass --steps 10 to go faster)

Do not load the shape model in this process. Do not enable_model_cpu_offload.
HF_HOME must be local disk, not Google Drive, while weights load.
"""
from __future__ import annotations

import argparse
import gc
import os
import re
import sys
from pathlib import Path

import torch
import trimesh
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
HY3D = ROOT / "deps" / "Hunyuan3D-2"
CONTENT = Path("/content")
if (CONTENT / "hf_cache").is_dir():
    os.environ["HF_HOME"] = str(CONTENT / "hf_cache")
    os.environ["HY3DGEN_MODELS"] = str(CONTENT / "hf_cache" / "hy3dgen")
else:
    os.environ.setdefault("HF_HOME", str(ROOT / "hf_cache"))
    os.environ.setdefault("HY3DGEN_MODELS", str(ROOT / "hf_cache" / "hy3dgen"))
sys.path.insert(0, str(HY3D))

SIZE = 512


def progress(value: float, stage: str) -> None:
    """Lines the server parses into job stage + percent."""
    print(f"##PROGRESS {max(0.0, min(1.0, value)):.3f} {stage}", flush=True)


def _resolve(name: str) -> Path:
    for base in (ROOT, CONTENT):
        path = base / name
        if path.exists():
            return path
    return ROOT / name


def force_cuda_load() -> None:
    """Stock Diffusers pickle/safetensors onto CPU. That is what crashed Colab."""
    import safetensors.torch as st

    orig = torch.load

    def cuda_load(*args, **kwargs):
        kwargs["map_location"] = "cuda"
        src = args[0] if args else kwargs.get("f")
        print("torch.load -> cuda", src, flush=True)
        return orig(*args, **kwargs)

    torch.load = cuda_load

    orig_sf = st.load_file

    def cuda_sf(filename, device="cpu", **kwargs):
        print("safetensors -> cuda", filename, flush=True)
        return orig_sf(filename, device="cuda", **kwargs)

    st.load_file = cuda_sf


def patch_multiview(steps: int) -> None:
    path = HY3D / "hy3dgen" / "texgen" / "utils" / "multiview_utils.py"
    if not path.exists():
        raise SystemExit(f"missing {path}; clone Hunyuan3D-2 and compile paint kernels")
    text = path.read_text()
    old = "custom_pipeline=custom_pipeline_path, torch_dtype=torch.float16)"
    new = "custom_pipeline=custom_pipeline_path, torch_dtype=torch.float16, trust_remote_code=True)"
    if old in text:
        text = text.replace(old, new, 1)
        print("patched trust_remote_code", flush=True)
    text = text.replace(
        "pipeline.set_progress_bar_config(disable=True)",
        "pipeline.set_progress_bar_config(disable=False)",
    )
    text = re.sub(r"num_inference_steps=\d+", f"num_inference_steps={steps}", text)
    path.write_text(text)


def load_mesh(path: Path):
    loaded = trimesh.load(str(path), force="mesh", process=False)
    if isinstance(loaded, trimesh.Scene):
        geoms = list(loaded.geometry.values())
        if not geoms:
            raise SystemExit(f"{path} has no mesh")
        return trimesh.util.concatenate(geoms)
    return loaded


def skip_delight() -> None:
    from hy3dgen.texgen.pipelines import Hunyuan3DPaintPipeline
    from hy3dgen.texgen.utils.multiview_utils import Multiview_Diffusion_Net

    def load_models(self):
        torch.cuda.empty_cache()
        print("loading Paint only (no Delight)", flush=True)
        print("gpu allocated GB", torch.cuda.memory_allocated() / 1e9, flush=True)
        self.models["multiview_model"] = Multiview_Diffusion_Net(self.config)

        class _Identity:
            def __call__(self, im):
                return im.convert("RGB") if hasattr(im, "convert") else im

        self.models["delight_model"] = _Identity()
        print("gpu after load GB", torch.cuda.memory_allocated() / 1e9, flush=True)

    Hunyuan3DPaintPipeline.load_models = load_models


def to_cuda(pipe) -> None:
    pipe.to("cuda")
    for name in ("unet", "unet_ref", "vae", "text_encoder", "image_encoder"):
        mod = getattr(pipe, name, None)
        if mod is not None and hasattr(mod, "to"):
            mod.to("cuda")
            print("moved", name, "-> cuda", flush=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", type=Path, default=_resolve("image.png"))
    parser.add_argument("--shape", type=Path, default=_resolve("shape.glb"))
    parser.add_argument("--out", type=Path, default=_resolve("textured.glb"))
    parser.add_argument("--steps", type=int, default=30, help="30 is the run that finished; 10 is faster")
    args = parser.parse_args()

    if not args.image.exists():
        raise SystemExit(f"missing {args.image}")
    if not args.shape.exists():
        raise SystemExit(f"missing {args.shape}; run run_hunyuan_shape.py first")
    if not torch.cuda.is_available():
        raise SystemExit("CUDA required")

    progress(0.05, "Preparing paint")
    force_cuda_load()
    patch_multiview(args.steps)
    skip_delight()

    from hy3dgen.texgen import Hunyuan3DPaintPipeline
    from hy3dgen.texgen.differentiable_renderer.mesh_render import MeshRender

    print("device", torch.cuda.get_device_name(0), flush=True)
    progress(0.15, "Loading mesh")
    mesh = load_mesh(args.shape)
    image = Image.open(args.image).convert("RGBA")
    print("faces", int(mesh.faces.shape[0]), "image", image.size, "steps", args.steps, flush=True)

    progress(0.25, "Loading paint model")
    pipeline = Hunyuan3DPaintPipeline.from_pretrained(
        "tencent/Hunyuan3D-2",
        subfolder="hunyuan3d-paint-v2-0-turbo",
    )
    to_cuda(pipeline.models["multiview_model"].pipeline)
    pipeline.config.render_size = SIZE
    pipeline.config.texture_size = SIZE
    pipeline.render = MeshRender(default_resolution=SIZE, texture_size=SIZE)
    print("from_pretrained done", flush=True)

    progress(0.40, f"Baking texture ({args.steps} steps)")
    textured = pipeline(mesh, image=image)

    progress(0.92, "Writing textured GLB")
    args.out.parent.mkdir(parents=True, exist_ok=True)
    textured.export(str(args.out))
    progress(1.0, "Texture ready")
    print("done ->", args.out, "bytes", args.out.stat().st_size, flush=True)
    drive = Path("/content/drive/MyDrive/creativetwin")
    if drive.is_dir():
        dest = drive / "textured.glb"
        dest.write_bytes(args.out.read_bytes())
        print("copied Drive", dest, dest.stat().st_size, flush=True)
    del pipeline, mesh, textured
    gc.collect()
    torch.cuda.empty_cache()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:  # make failures visible to the server log
        print(f"##ERROR {type(exc).__name__}: {exc}", flush=True)
        raise
