#!/usr/bin/env bash
# source this before Pipeline B. Never source session_a in the same shell.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ ! -f "$ROOT/.venv-b/bin/activate" ]]; then
  echo "Run scripts/setup_b.sh first" >&2
  return 1 2>/dev/null || exit 1
fi
# shellcheck disable=SC1091
source "$ROOT/.venv-b/bin/activate"
export ROOT HF_HOME="$ROOT/hf_cache"
export PYTHONPATH="$ROOT/deps/TripoSR${PYTHONPATH:+:$PYTHONPATH}"
mkdir -p "$HF_HOME"
cd "$ROOT"
echo "B env $(command -v python)"
