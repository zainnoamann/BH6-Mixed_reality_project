#!/usr/bin/env bash
# Shared paths for SageMaker (or a Linux GPU box).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export ROOT
export HF_HOME="${HF_HOME:-$ROOT/hf_cache}"
mkdir -p "$HF_HOME" "$ROOT/deps"

pick_python() {
  if [[ -x /home/ec2-user/anaconda3/envs/JupyterSystemEnv/bin/python ]]; then
    echo /home/ec2-user/anaconda3/envs/JupyterSystemEnv/bin/python
  else
    command -v python3
  fi
}

match_cuda_home() {
  local tc
  tc="$("$1" -c "import torch; print(torch.version.cuda or '')" 2>/dev/null || true)"
  if [[ -n "$tc" && -d "/usr/local/cuda-${tc}" ]]; then
    export CUDA_HOME="/usr/local/cuda-${tc}"
    export PATH="$CUDA_HOME/bin:$PATH"
    export LD_LIBRARY_PATH="$CUDA_HOME/lib64${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  fi
}
