#!/usr/bin/env bash
# Create .venv-a and install Pipeline A packages.
set -euo pipefail
# shellcheck disable=SC1091
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

PY="$(pick_python)"
echo "base python: $PY"
"$PY" -c "import torch; print('torch', torch.__version__, 'cuda', torch.version.cuda, torch.cuda.is_available())" || {
  echo "This python has no CUDA torch. On SageMaker use JupyterSystemEnv." >&2
  exit 1
}

"$PY" -m venv --system-site-packages "$ROOT/.venv-a"
# shellcheck disable=SC1091
source "$ROOT/.venv-a/bin/activate"
match_cuda_home python

python -m pip install -U pip wheel
python -m pip install -r "$ROOT/requirements-a.txt"

python - << 'PY'
import torch, diffusers, gguf, transformers
assert torch.cuda.is_available(), "venv-a lost CUDA"
print("A ok", torch.__version__, transformers.__version__, diffusers.__version__)
PY
echo "Pipeline A env: $ROOT/.venv-a"
echo "Next: source scripts/session_a.sh && python pipelines/run_t2i.py"
