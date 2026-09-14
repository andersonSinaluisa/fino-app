#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-${NEXO_PRODUCTION_URL:-}}"
if [[ -z "$BASE_URL" ]]; then
  echo "Usage: $0 https://api.fino.app" >&2
  echo "Or set NEXO_PRODUCTION_URL=https://api.fino.app" >&2
  exit 2
fi

BASE_URL="${BASE_URL%/}"

curl_json() {
  local path="$1"
  curl --fail --silent --show-error \
    --connect-timeout 5 \
    --max-time 15 \
    "$BASE_URL$path" >/dev/null
}

curl_json "/health/live"
curl_json "/health/ready"

echo "Production smoke OK: $BASE_URL"
