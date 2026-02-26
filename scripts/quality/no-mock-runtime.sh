#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# Runtime paths only (exclude tests/docs/tools)
INCLUDE_PATHS=(
  "$ROOT_DIR/src/AgentFlow.Api"
  "$ROOT_DIR/src/AgentFlow.Application"
  "$ROOT_DIR/src/AgentFlow.Core.Engine"
  "$ROOT_DIR/src/AgentFlow.Infrastructure"
  "$ROOT_DIR/src/AgentFlow.Extensions"
  "$ROOT_DIR/src/AgentFlow.ModelRouting"
)

# Allowed exceptions list (one regex per line) to prevent false positives
ALLOWLIST_FILE="$ROOT_DIR/scripts/quality/no-mock-runtime.allowlist"
if [[ ! -f "$ALLOWLIST_FILE" ]]; then
  cat > "$ALLOWLIST_FILE" <<'EOF'
# Allowed mock/stub references in runtime (regex). Keep this list short.
EOF
fi

PATTERN='\b(mock|stub|simulat(e|ed|ion)|fake|dummy|placeholder)\b'

TMP_HITS="$(mktemp)"
trap 'rm -f "$TMP_HITS"' EXIT

for p in "${INCLUDE_PATHS[@]}"; do
  if [[ -d "$p" ]]; then
    rg -n --hidden --glob '!**/bin/**' --glob '!**/obj/**' -i "$PATTERN" "$p" >> "$TMP_HITS" || true
  fi
done

# Filter allowlisted matches
if [[ -s "$TMP_HITS" ]]; then
  grep -Ev '^[[:space:]]*#|^[[:space:]]*$' "$ALLOWLIST_FILE" > /tmp/no-mock-allowlist.$$ || true
  if [[ -s /tmp/no-mock-allowlist.$$ ]]; then
    # remove lines matching any allowlist regex
    cp "$TMP_HITS" "$TMP_HITS.filtered"
    while IFS= read -r rule; do
      [[ -z "$rule" ]] && continue
      grep -Ev "$rule" "$TMP_HITS.filtered" > "$TMP_HITS.next" || true
      mv "$TMP_HITS.next" "$TMP_HITS.filtered"
    done < /tmp/no-mock-allowlist.$$
    mv "$TMP_HITS.filtered" "$TMP_HITS"
  fi
  rm -f /tmp/no-mock-allowlist.$$
fi

if [[ -s "$TMP_HITS" ]]; then
  echo "[NO-MOCK-RUNTIME] FAILED: Found forbidden mock/stub terms in runtime code:"
  cat "$TMP_HITS"
  echo
  echo "If a match is intentional and safe, add a precise regex to:"
  echo "  scripts/quality/no-mock-runtime.allowlist"
  exit 1
fi

echo "[NO-MOCK-RUNTIME] OK: no forbidden mock/stub terms found in runtime paths."