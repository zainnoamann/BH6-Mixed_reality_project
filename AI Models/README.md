# CreativeTwin local AI pipelines

Two **separate** Python environments. Never install A and B into the same venv, and never load both models in one process.

| | Pipeline A | Pipeline B |
|---|---|---|
| Job | text → `image.png` | `image.png` → `shape.glb` |
| Model | FLUX.2-klein-4B GGUF Q8 | Hunyuan3D-2mini-Turbo (shape only) |
| Why | Fits a T4 if you encode, drop Qwen, then denoise | TRELLIS.2 hangs in `to_glb`. TripoSR finished but mesh quality was poor. Hunyuan shape on Colab T4 produced the chair we use in Unity. |
| Texture | | **Unity URP Lit** + a tileable wood/fabric PNG. Hunyuan Paint does not fit Colab 12 GB RAM. |

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

Hunyuan Paint (bake the FLUX photo onto UVs) is not part of this demo. It OOM'd on free Colab. Wood/fabric is a Unity material.

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
2. Copy `shape.glb` into `Assets/`.
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
- Hunyuan Paint stays off the T4/Colab demo path.

## Layout

```
pipelines/run_t2i.py              Pipeline A
pipelines/run_hunyuan_shape.py    Pipeline B (working Hunyuan)
scripts/setup_a.sh
scripts/setup_b.sh                clone Hunyuan + CUDA patch
scripts/session_a.sh
scripts/session_b.sh
scripts/patch_hunyuan_cuda.py
scripts/common.sh
requirements-a.txt
requirements-b.txt
```
