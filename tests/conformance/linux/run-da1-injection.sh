#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
ARTIFACT_DIR="${1:-${ROOT_DIR}/conformance-artifacts/da1}"
mkdir -p "$ARTIFACT_DIR"

RUN_ROOT="$(mktemp -d)"
export TMPDIR="$RUN_ROOT"

export TERMINA_RAW_INPUT=1
unset TERMINA_KITTY_KEYBOARD || true
unset TMUX || true
unset TERM_PROGRAM || true

DEMO_DLL="$ROOT_DIR/demos/Termina.Demo.Conformance/bin/Release/net10.0/Termina.Demo.Conformance.dll"
DEMO_CMD=(dotnet "$DEMO_DLL")

if [[ ! -f "$DEMO_DLL" ]]; then
  echo "demo not built: $DEMO_DLL" >&2
  exit 1
fi

python3 "$ROOT_DIR/tests/conformance/linux/assert-da1-injection.py" \
  --demo "dotnet" \
  --demo-arg "$DEMO_DLL" \
  --events "$RUN_ROOT/termina-conformance/events.jsonl" \
  --artifacts "$ARTIFACT_DIR"

rm -rf "$RUN_ROOT"
