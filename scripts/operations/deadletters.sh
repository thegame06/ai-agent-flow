#!/usr/bin/env bash
set -euo pipefail

ACTION="${1:-}"
API_BASE="${API_BASE:-http://localhost:5000}"
TENANT_ID="${TENANT_ID:-tenant-1}"

if [[ -z "$ACTION" ]]; then
  echo "Usage:"
  echo "  API_BASE=http://localhost:5000 TENANT_ID=tenant-1 $0 list"
  echo "  API_BASE=http://localhost:5000 TENANT_ID=tenant-1 $0 replay <deadletter-id>"
  exit 1
fi

case "$ACTION" in
  list)
    curl -sS "${API_BASE}/api/v1/tenants/${TENANT_ID}/audit/operations/deadletters"
    ;;
  replay)
    DEADLETTER_ID="${2:-}"
    if [[ -z "$DEADLETTER_ID" ]]; then
      echo "Missing deadletter id. Usage: $0 replay <deadletter-id>"
      exit 1
    fi
    curl -sS -X POST "${API_BASE}/api/v1/tenants/${TENANT_ID}/audit/operations/deadletters/${DEADLETTER_ID}/replay"
    ;;
  *)
    echo "Unknown action: $ACTION"
    exit 1
    ;;
esac

