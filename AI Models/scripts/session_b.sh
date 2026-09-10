#!/usr/bin/env bash
# source this before Hunyuan shape. Never source session_a in the same shell.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -f "$ROOT/.venv-b/bin/activate" ]]; then
  # shellcheck disable=SC1091
  source "$ROOT/.venv-b/bin/activate"
fi
export ROOT
export HF_HOME="$ROOT/hf_cache"
export HY3DGEN_MODELS="$ROOT/hf_cache/hy3dgen"
export PYTHONPATH="$ROOT/deps/Hunyuan3D-2${PYTHONPATH:+:$PYTHONPATH}"
export TORCH_CUDA_ARCH_LIST="${TORCH_CUDA_ARCH_LIST:-7.5}"
mkdir -p "$HF_HOME" "$HY3DGEN_MODELS"
cd "$ROOT"
tc="$(python3 -c "import torch; print(torch.version.cuda or '')" 2>/dev/null || true)"
if [[ -n "$tc" && -d "/usr/local/cuda-${tc}" ]]; then
  export CUDA_HOME="/usr/local/cuda-${tc}"
  export PATH="$CUDA_HOME/bin:$PATH"
  export LD_LIBRARY_PATH="$CUDA_HOME/lib64${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi
echo "B hunyuan $(command -v python3) CUDA_HOME=${CUDA_HOME:-unset}"
