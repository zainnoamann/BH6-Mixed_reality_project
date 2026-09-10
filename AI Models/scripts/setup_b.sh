#!/usr/bin/env bash
# Clone Hunyuan3D-2, install hy3dgen, apply the CUDA load patch.
# Colab: no venv (ensurepip is broken). SageMaker: venv if it works, else system python.
set -euo pipefail
# shellcheck disable=SC1091
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

PY="$(pick_python)"
echo "base python: $PY"
"$PY" -c "import torch; print('torch', torch.__version__, 'cuda', torch.version.cuda, torch.cuda.is_available())" || {
  echo "This python has no CUDA torch. Colab: Runtime > T4 GPU. SageMaker: JupyterSystemEnv." >&2
  exit 1
}

USE_VENV=1
if python3 -c "import google.colab" >/dev/null 2>&1 || [[ -d /content ]]; then
  echo "Colab: skip venv. Using $PY"
  USE_VENV=0
  rm -rf "$ROOT/.venv-b"
fi

if [[ "$USE_VENV" -eq 1 ]]; then
  if ! "$PY" -m venv --system-site-packages "$ROOT/.venv-b"; then
    echo "venv failed. Using $PY" >&2
    USE_VENV=0
    rm -rf "$ROOT/.venv-b"
  else
    # shellcheck disable=SC1091
    source "$ROOT/.venv-b/bin/activate"
    PY="$(command -v python)"
  fi
fi

match_cuda_home "$PY"
export TORCH_CUDA_ARCH_LIST="${TORCH_CUDA_ARCH_LIST:-7.5}"

"$PY" -m pip install -U pip wheel
"$PY" -m pip install -r "$ROOT/requirements-b.txt" || {
  echo "Retrying without pymeshlab" >&2
  grep -v '^pymeshlab' "$ROOT/requirements-b.txt" > /tmp/req-b.txt
  "$PY" -m pip install -r /tmp/req-b.txt
}

HY3D="$ROOT/deps/Hunyuan3D-2"
if [[ ! -d "$HY3D/.git" ]]; then
  git clone --depth 1 https://github.com/Tencent-Hunyuan/Hunyuan3D-2.git "$HY3D"
fi
"$PY" -m pip install -e "$HY3D" --no-deps
"$PY" "$ROOT/scripts/patch_hunyuan_cuda.py"

export PYTHONPATH="$HY3D${PYTHONPATH:+:$PYTHONPATH}"
"$PY" - << 'PY'
import torch
from hy3dgen.shapegen import Hunyuan3DDiTFlowMatchingPipeline
assert torch.cuda.is_available()
print("hunyuan ok", torch.__version__)
PY
echo "Next: source scripts/session_b.sh && python pipelines/run_hunyuan_shape.py"
echo "HF_HOME must stay on local disk ($ROOT/hf_cache), not Google Drive, while loading."
