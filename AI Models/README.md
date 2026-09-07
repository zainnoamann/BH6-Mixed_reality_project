# CreativeTwin local AI pipelines

Two **separate** Python environments. Never install A and B into the same venv, and never load both models in one process.

| | Pipeline A | Pipeline B |
|---|---|---|
| Job | text → `image.png` | `image.png` → `shape.glb` |
| Model | FLUX.2-klein-4B GGUF Q8 | Hunyuan3D-2mini-Turbo (shape only) |
| Why | Fits a T4 if you encode, drop Qwen, then denoise | TRELLIS.2 hangs in `to_glb`. TripoSR finished but mesh quality was poor. Hunyuan shape on Colab T4 produced the chair we use in Unity. |
| Texture | | Hunyuan Paint turbo bake (`textured.glb`) on Colab T4, or **Unity URP Lit** + a tileable wood/fabric PNG to swap materials without a second bake. |

Copy **this `AI Models/` directory** onto Colab (`/content/...`) or SageMaker. A Mac cannot host these CUDA models.

## Pipeline B: the working Hunyuan run (do not undo)

This is the path that wrote a real `shape.glb` on a Colab T4.

- Model: `tencent/Hunyuan3D-2mini` / `hunyuan3d-dit-v2-mini-turbo`
- `use_safetensors=False`, `device="cuda"`
- **No** `enable_flashvdm()`, **no** `enable_model_cpu_offload()`
- `num_inference_steps=5`, `octree_resolution=256`, `num_chunks=20000`
- Patch `hy3dgen/shapegen/pipelines.py` so `torch.load` uses `map_location='cuda'` (stock code loads on CPU, fills 12 GB RAM, GPU stays at 0)
- `HF_HOME` on **local disk** (`hf_cache/`), not Google Drive, while the checkpoint loads
- Colab: **no venv** (`ensurepip` fails). Use the runtime `python3`
- Do not `pip install torch`

## Hunyuan Paint (bake `image.png` onto `shape.glb`)

This is the Colab T4 path that wrote `textured.glb`. There is no GGUF/Q8 Paint file. Do not load shape and paint in one process.

- Skip Delight (extra lighting UNet)
- Patch `torch.load` / safetensors onto **CUDA**. Stock Diffusers loads on CPU, fills 12.7 GB RAM, GPU stays at 0, runtime dies
- `trust_remote_code=True` for `hunyuanpaint/pipeline.py`
- After load, `.to("cuda")` on leftover modules (`unet`, `unet_ref`, `vae`). Otherwise bake crashes: mat1 on cuda, weights on cpu
- Texture 512. Default **30** denoise steps (the run that finished, ~20-40 min, progress bar was off in stock Hunyuan). `python pipelines/run_hunyuan_paint.py --steps 10` is faster
- `HF_HOME` on local disk (`/content/hf_cache`), not Drive, while loading
- No `enable_model_cpu_offload()`, no mmgp (not needed once CUDA load works)

```bash
python pipelines/run_hunyuan_paint.py
```

Watch Resources during load: RAM may spike, GPU should leave 0. Wait for `from_pretrained done`, then a denoise bar, then `done -> .../textured.glb`. Copy that file off the machine. Unity materials remain the path for wood vs fabric without rerunning Paint.

## One-time setup

Use **bash**, not `sh`.

```bash
cd /path/to/AI\ Models
bash scripts/setup_a.sh
bash scripts/setup_b.sh
```

Wait for `hunyuan ok`.

## Every session

**A, then stop:**

```bash
bash
cd /path/to/AI\ Models
source ./scripts/session_a.sh
python pipelines/run_t2i.py
```

Wait for `done -> .../image.png`. If the chair on white looks good, do not rerun A.

**B, new process:**

```bash
bash
cd /path/to/AI\ Models
source ./scripts/session_b.sh
python pipelines/run_hunyuan_shape.py
```

Watch GPU RAM leave 0 GB after `Loading model from ...ckpt`. Wait for `done -> .../shape.glb` and a non-zero size. Copy `shape.glb` off the machine (Drive or download) before the runtime dies.

On Colab you can persist weights by copying `hf_cache/` to Drive **after** the run. Next session copy Drive → local `hf_cache/`, then run B. Do not set `HF_HOME` to Drive during load.

## Unity

1. Install **glTFast** (`com.unity.cloud.gltfast`) from Package Manager.
2. Copy `shape.glb` (Unity materials) or `textured.glb` (FLUX bake) into `Assets/`.
3. Drag it into the scene. Press **F**. Scale if needed (`0.01` or `100`).
4. Create a **URP Lit** material. Base Map = a **tileable** wood/fabric PNG (not the FLUX photo). Metallic **0**. Metallic Map **empty**. Smoothness ~0.25.
5. Assign that material on the object's Mesh Renderer → Materials → Element 0.

If grain is missing, unwrap in Blender (Smart UV Project) and reimport.

## What we learned (do not undo)

- Match `CUDA_HOME` to `torch.version.cuda` (12.8 vs 13.0 vs 13.2).
- FLUX GGUF file name is `flux-2-klein-4b-Q8_0.gguf`. Load with `config=black-forest-labs/FLUX.2-klein-4B`, `subfolder=transformer`.
- Set `Flux2KleinPipeline._execution_device` on the **class**. Dummy text encoder after Qwen is dropped.
- Hunyuan must load the ckpt on **CUDA**. `scripts/patch_hunyuan_cuda.py` does that.
- TRELLIS.2 stays a 5090-class quality path. It hangs after texture sampling in remesh/`to_glb`.
- Hunyuan Paint on T4: CUDA `torch.load`, skip Delight, then move leftover modules onto GPU. CPU load of the Paint UNet kills Colab RAM.

## Layout

```
pipelines/run_t2i.py              Pipeline A
pipelines/run_hunyuan_shape.py    Pipeline B (working Hunyuan)
pipelines/run_hunyuan_paint.py    Hunyuan Paint turbo (CUDA load, skip Delight)
scripts/setup_a.sh
scripts/setup_b.sh                clone Hunyuan + CUDA patch
scripts/session_a.sh
scripts/session_b.sh
scripts/patch_hunyuan_cuda.py
scripts/common.sh
requirements-a.txt
requirements-b.txt
```
