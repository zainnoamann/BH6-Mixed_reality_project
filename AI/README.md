# CreativeTwin local AI pipelines

Two **separate** Python environments. Never install A and B into the same venv, and never load both models in one process. Unity consumes `object.glb`.

| | Pipeline A | Pipeline B |
|---|---|---|
| Job | text → `image.png` | `image.png` → `object.glb` |
| Model | FLUX.2-klein-4B GGUF Q8 | TripoSR |
| Venv | `.venv-a` | `.venv-b` |
| Why this model | Fits a T4 if you encode, drop Qwen, then denoise | TRELLIS.2 on a T4 hangs in decode/remesh; TripoSR finishes and writes a GLB |

This folder lives at `AI/` in the CreativeTwin Unity repo. Copy **this `AI/` directory** onto the SageMaker volume and run there. A Mac cannot host these CUDA models.

## One-time setup (SageMaker Jupyter terminal)

Use **bash**, not `sh`. `source file.sh` in `sh` looks on PATH and fails even if `ls` shows the file. Use `source ./scripts/session_a.sh`.

```bash
cd /home/ec2-user/SageMaker/AI
bash scripts/setup_a.sh
bash scripts/setup_b.sh
```

Each setup creates a venv with `--system-site-packages` so it reuses the AMI CUDA torch. Extra packages live **in the venv on the notebook volume**, so they survive JupyterSystemEnv wipes better than installing into conda.

## Every session

**A, then stop:**

```bash
bash
cd /home/ec2-user/SageMaker/AI
source ./scripts/session_a.sh
python pipelines/run_t2i.py
```

Wait for `done -> .../image.png`. Open `image.png`. If the chair on white looks good, do not rerun A.

**B, new process:**

```bash
bash
cd /home/ec2-user/SageMaker/AI
source ./scripts/session_b.sh
python pipelines/run_img2glb.py
```

Wait for `done -> .../object.glb` and a non-zero file size. Download `object.glb`.

## Unity

1. Install **glTFast** (`com.unity.cloud.gltfast`) from Package Manager.
2. Copy `object.glb` into this Unity project's `Assets/` folder.
3. Drag it into the scene. Press **F** to frame. If it is tiny or huge, change Scale to `0.01` or `100`.

If Unity will not import GLB, open the file in Blender first (File → Import → glTF 2.0). If Blender shows a chair, the file is fine and Unity needs glTFast.

## What we learned (do not undo)

- Match `CUDA_HOME` to `torch.version.cuda` (12.8 vs 13.0 vs 13.2).
- FLUX GGUF file name is `flux-2-klein-4b-Q8_0.gguf`. Load with `config=black-forest-labs/FLUX.2-klein-4B`, `subfolder=transformer`.
- Set `Flux2KleinPipeline._execution_device` on the **class**. Dummy text encoder after Qwen is dropped.
- TripoSR is not `pip install git+...`; clone the repo and set `PYTHONPATH`.
- Do not compile `torchmcubes` on this AMI; `scripts/patch_triposr.py` uses scikit-image.
- TRELLIS.2 stays a 5090 quality path, not the T4 demo path.

## Layout

```
pipelines/run_t2i.py      Pipeline A
pipelines/run_img2glb.py  Pipeline B
scripts/setup_a.sh        create .venv-a
scripts/setup_b.sh        create .venv-b + clone/patch TripoSR
scripts/session_a.sh      activate A
scripts/session_b.sh      activate B
requirements-a.txt
requirements-b.txt
```
