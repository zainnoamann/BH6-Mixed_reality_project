# CreativeTwin: End-to-End Setup Guide

This guide sets up the whole system on a new machine:

1. The **AI server** (FLUX.2-klein, Hunyuan3D-2mini, Hunyuan3D-Paint, FastAPI on port 8765)
2. The **Unity project** (Unity 6000.3.20f1, URP, XR Interaction Toolkit, OpenXR)
3. The **Meta Quest** connected to the PC with Quest Link

The AI server needs an **NVIDIA GPU**. It runs on Linux, either inside **WSL (Windows)** or on **native Linux**. Unity runs on the Windows side (or any OS for desktop testing). A Mac can run Unity in offline test mode, but cannot run the AI models.

Tested on: RTX 5090 (32 GB), Windows 11 + WSL Ubuntu, PyTorch 2.7.1 + CUDA 12.8.

---

## Part 1. Before you start

| You need | Notes |
|---|---|
| NVIDIA GPU | 16 GB+ VRAM recommended. RTX 50-series (Blackwell) needs driver 570 or newer and CUDA 12.8. |
| Disk space | About 60 GB free for model weights and virtual environments. |
| System RAM | 16 GB or more. Hunyuan loads large checkpoints. |
| Python | 3.10 or newer inside Linux / WSL. |
| git | To clone the repo and the Hunyuan3D-2 code. |
| Internet | Hugging Face downloads of several GB on the first run. |

---

## Part 2A. AI server on Windows with WSL (the lab setup)

Use this on a Windows PC. Native Windows Python did not work for us; WSL Ubuntu does.

### Step 1. Install the NVIDIA driver on Windows (not in WSL)

1. Install the latest **Windows** NVIDIA Game Ready or Studio driver (570 or newer for RTX 50-series).
2. Do **not** install a Linux NVIDIA driver inside WSL. WSL uses the Windows driver automatically.

### Step 2. Install WSL and Ubuntu

In **PowerShell (as Administrator)**:

```powershell
wsl --install -d Ubuntu-24.04
wsl --update
```

Restart if asked, open **Ubuntu** from the Start menu, and create a Linux user name and password.

Check that the GPU is visible inside Ubuntu:

```bash
nvidia-smi
```

You should see your GPU and the driver version. If this fails, fix the Windows driver first.

### Step 3. Install system packages inside Ubuntu

```bash
sudo apt update
sudo apt install -y git python3 python3-venv python3-dev python3-pip build-essential ninja-build wget
```

### Step 4. Install the CUDA Toolkit 12.8 (WSL version)

The toolkit gives `nvcc`, which is needed to build Hunyuan3D-Paint's two GPU extensions (`custom_rasterizer` and `differentiable_renderer`). Without it, objects come out as a white mesh with no texture.

Use NVIDIA's **WSL-Ubuntu** package. It does not contain a driver, so it is safe in WSL.

```bash
wget https://developer.download.nvidia.com/compute/cuda/repos/wsl-ubuntu/x86_64/cuda-keyring_1.1-1_all.deb
sudo dpkg -i cuda-keyring_1.1-1_all.deb
sudo apt update
sudo apt install -y cuda-toolkit-12-8
```

Add it to your shell (once):

```bash
echo 'export CUDA_HOME=/usr/local/cuda-12.8' >> ~/.bashrc
echo 'export PATH=$CUDA_HOME/bin:$PATH' >> ~/.bashrc
echo 'export LD_LIBRARY_PATH=$CUDA_HOME/lib64:$LD_LIBRARY_PATH' >> ~/.bashrc
source ~/.bashrc
nvcc --version      # should say release 12.8
```

The CUDA version must match PyTorch's (`cu128`). A different toolkit version (12.4, 13.0) makes the extension build fail or crash at run time.

### Step 5. Get the project

Either clone inside Linux (faster disk):

```bash
cd ~
git clone https://github.com/zainnoamann/BH6-Mixed_reality_project.git
cd "BH6-Mixed_reality_project/AI Models"
```

or use the copy already on a Windows drive (this is what the lab PC does; it works but file access is slower):

```bash
cd "/mnt/d/g71_f1/BH6-Mixed_reality_project/AI Models"
```

### Step 6. First-time setup and start

```bash
unset PYTORCH_CUDA_ALLOC_CONF
python3 start.py
```

On the first run, `start.py` calls `bootstrap.py`, which:

1. Checks the GPU, driver, RAM and Python.
2. Creates three separate virtual environments: `.venv-server` (FastAPI), `.venv-a` (FLUX), `.venv-b` (Hunyuan). They must stay separate.
3. Installs the matching CUDA build of PyTorch (`cu128` for driver 570+).
4. Clones Hunyuan3D-2 into `deps/`, patches its loader, and builds the two Paint extensions with `nvcc`.
5. Downloads all model weights into `hf_cache/` (several GB, takes a while).
6. Writes `status.json` and starts the server on port **8765**.

This first run can take 30 to 60 minutes, mostly downloads. Later runs start in seconds.

You do **not** need to activate any virtual environment yourself. `start.py` picks the right one for each step.

### Step 7. Check it works

In a second Ubuntu terminal:

```bash
cd "<project>/AI Models"
python3 start.py --check
curl http://127.0.0.1:8765/health
```

All rows in the check table should say `OK`, including **Hunyuan3D-Paint: texture baking ready**.

Because WSL 2 forwards `localhost` to Windows, Unity on the same PC reaches the server at `http://127.0.0.1:8765` with no extra setup.

---

## Part 2B. AI server on native Linux (Ubuntu)

Same as WSL, with two differences: the driver is installed **inside** Linux, and the CUDA repository is the normal Ubuntu one.

### Step 1. NVIDIA driver

```bash
sudo ubuntu-drivers list            # see what is available
sudo apt install -y nvidia-driver-570   # or newer; RTX 50-series needs 570+
sudo reboot
nvidia-smi
```

### Step 2. System packages

```bash
sudo apt update
sudo apt install -y git python3 python3-venv python3-dev python3-pip build-essential ninja-build wget
```

### Step 3. CUDA Toolkit 12.8 (toolkit only, keep your driver)

Pick the line for your Ubuntu version (`ubuntu2204` or `ubuntu2404`):

```bash
wget https://developer.download.nvidia.com/compute/cuda/repos/ubuntu2404/x86_64/cuda-keyring_1.1-1_all.deb
sudo dpkg -i cuda-keyring_1.1-1_all.deb
sudo apt update
sudo apt install -y cuda-toolkit-12-8     # "cuda-toolkit", not "cuda", so the driver is not replaced
```

Then add the same three `export` lines as WSL Step 4 and check `nvcc --version`.

### Step 4. Project, setup and start

```bash
git clone https://github.com/zainnoamann/BH6-Mixed_reality_project.git
cd "BH6-Mixed_reality_project/AI Models"
unset PYTORCH_CUDA_ALLOC_CONF
python3 start.py
```

If Unity runs on a **different** computer, open port 8765 in the Linux firewall and set `AiClient.baseUrl` in Unity to `http://<linux-pc-ip>:8765`. The server already listens on all network interfaces.

---

## Part 3. Every day: start the server

```bash
cd "<project>/AI Models"
unset PYTORCH_CUDA_ALLOC_CONF
python3 start.py
```

Leave this terminal open while you use the app. Stop with `Ctrl + C`.

Useful commands:

| Command | What it does |
|---|---|
| `python3 start.py --check` | Status only, changes nothing |
| `python3 start.py --setup` | Re-run setup (safe: it skips what is already there) |
| `python3 bootstrap.py --skip-paint` | Set up without the large texture model (meshes stay untextured) |

Where things are:

| Path | Contents |
|---|---|
| `AI Models/jobs/<id>/` | `image.png`, `shape.glb`, `textured.glb` for each generation |
| `AI Models/logs/job-<id>.log` | Full log of each generation, the first place to look when something fails |
| `AI Models/logs/stage_times.json` | Measured time of each step; drives the loading bar's "time left" |
| `AI Models/status.json` | What the setup found (GPU, venvs, models) |

---

## Part 4. Unity project

1. Install **Unity Hub** and **Unity 6000.3.20f1** (6.3 LTS) with **Android Build Support** (for the Quest).
2. In Unity Hub, **Add project from disk** and choose the repo folder (the one with `Assets/`).
3. Wait for the first import (several minutes). Packages (XR Interaction Toolkit 3.3.2, OpenXR, glTFast) install automatically from `Packages/manifest.json`.
4. Open the right scene:
   - **QuestScene** for the headset
   - **SampleScene** for desktop (mouse and keyboard) testing
5. Check **Edit, Project Settings, XR Plug-in Management**:
   - **Windows / PC tab:** OpenXR enabled, and under OpenXR the **Oculus Touch Controller Profile** (or Meta Quest Touch Plus) is in the Interaction Profiles list. Play mode with Link uses the **PC** tab, not the Android tab.
   - **Android tab:** OpenXR enabled with the same controller profile (only needed for an APK).
6. The server address is in `Assets/Scripts/AiClient.cs` (`baseUrl`, default `http://127.0.0.1:8765`).

### Offline test mode (no AI server)

If the server is not running, Unity uses the placeholder files in `Assets/StreamingAssets/Placeholders/` (`chair.glb`, `vase.glb`, `lamp.glb` and three texture images). This lets you test menus, placement, move, rotate and resize on any computer, including a Mac.

---

## Part 5. Meta Quest with Quest Link

1. On Windows, install the **Meta Quest Link** app (formerly Oculus app) and sign in.
2. In the Link app, go to **Settings, General**:
   - turn on **Unknown Sources**
   - set **OpenXR Runtime** to **Meta Quest Link** (press "Set Meta Quest Link as active")
3. Connect the headset with a USB-C cable (or Air Link), put it on and accept **Quest Link**.
4. In the headset, set your **floor level** (Settings, Boundary / Physical space) so you stand at the right height.
5. In Unity, open **QuestScene** and press **Play**. The scene appears in the headset.

Check the OpenXR runtime from Windows (optional):

```powershell
reg query "HKLM\SOFTWARE\Khronos\OpenXR\1" /v ActiveRuntime
```

It should point to `...\oculus-runtime\oculus_openxr_64.json`.

### Controls in the headset

| Action | Control |
|---|---|
| Point and click | Trigger (either hand) |
| Move an object | Select it, then **Move**, or press **Grip** on it |
| Rotate / Resize | Select it, then **Rotate** or **Resize**; **Y** (left) for up, **X** (left) for down |
| Cancel | **B** (right) |
| Type a prompt | Point at the text field; an on-screen keyboard appears |

---

## Part 6. Troubleshooting

| Problem | Likely cause and fix |
|---|---|
| `nvidia-smi` fails in WSL | Windows NVIDIA driver missing or old. Update it on Windows, then `wsl --shutdown` and reopen Ubuntu. |
| `CUDA out of memory` | Another program is using the GPU, or two jobs ran at once. Close other GPU apps, restart the server. Make sure `PYTORCH_CUDA_ALLOC_CONF` is unset. |
| Object is **white** (no texture) | Paint extensions did not build. Check `nvcc --version` says 12.8, then run `python3 start.py --setup` and look for `custom_rasterizer` in the log. |
| `ImportError: libc10.so` when testing the rasteriser | Import torch first: `.venv-b/bin/python -c "import torch, custom_rasterizer; print('ok')"` |
| Generation is very slow (15 min or more) | Paint is working on a very dense mesh. The paint script reduces it to 40,000 faces (`--max-faces`); make sure you have the latest code. |
| Unity says server not reachable | Server not started, or wrong `baseUrl`. Open `http://127.0.0.1:8765/health` in a Windows browser. |
| Controllers do not show in the headset | Controller profile missing on the **PC** tab of OpenXR, or Meta Quest Link is not the active OpenXR runtime. |
| "Controllers point at me" / menu missing | You pressed Play in **SampleScene**. Open **QuestScene**. |
| Object appears as a brown box | The glTFast define is missing for that platform. `GLTFAST` must be in Scripting Define Symbols for both Standalone and Android. |
| Loading bar shows no "time left" | Normal for the first run of each step. It learns the times after one successful run. |
| `git` says `index.lock: File exists` | A git command was interrupted. Close other git tools, then delete `.git/index.lock`. |
