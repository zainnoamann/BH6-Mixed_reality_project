#!/usr/bin/env bash
# source this before Pipeline A. Never source session_b in the same shell.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ ! -f "$ROOT/.venv-a/bin/activate" ]]; then
  echo "Run scripts/setup_a.sh first" >&2
  return 1 2>/dev/null || exit 1
fi
# shellcheck disable=SC1091
source "$ROOT/.venv-a/bin/activate"
export ROOT HF_HOME="$ROOT/hf_cache"
mkdir -p "$HF_HOME"
cd "$ROOT"
echo "A env $(command -v python)"
