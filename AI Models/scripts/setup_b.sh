#!/usr/bin/env bash
# Create .venv-b, clone TripoSR, patch marching cubes (no torchmcubes compile).
set -euo pipefail
# shellcheck disable=SC1091
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

PY="$(pick_python)"
echo "base python: $PY"
"$PY" -c "import torch; print('torch', torch.__version__, 'cuda', torch.version.cuda, torch.cuda.is_available())" || {
  echo "This python has no CUDA torch. On SageMaker use JupyterSystemEnv." >&2
  exit 1
}

"$PY" -m venv --system-site-packages "$ROOT/.venv-b"
# shellcheck disable=SC1091
source "$ROOT/.venv-b/bin/activate"
match_cuda_home python

python -m pip install -U pip wheel
python -m pip install -r "$ROOT/requirements-b.txt"

if [[ ! -d "$ROOT/deps/TripoSR/.git" ]]; then
  git clone --depth 1 https://github.com/VAST-AI-Research/TripoSR.git "$ROOT/deps/TripoSR"
fi

python "$ROOT/scripts/patch_triposr.py"

python - << 'PY'
import sys, os
sys.path.insert(0, os.path.join(os.environ["ROOT"], "deps/TripoSR"))
import torch
from tsr.system import TSR
assert torch.cuda.is_available(), "venv-b lost CUDA"
print("B ok", torch.__version__, "TSR imported")
PY
echo "Pipeline B env: $ROOT/.venv-b"
echo "Next: source scripts/session_b.sh && python pipelines/run_img2glb.py"
