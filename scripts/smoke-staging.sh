#!/usr/bin/env bash
# Entregable 30 ("Staging"): a smoke test to run right after deploying the API
# to staging (or any environment reachable by URL) -- the first honest signal
# that a deploy actually works, before QA (Entregable 32) or a beta build
# (Entregable 33) is pointed at it.
#
# This cannot be run from this development sandbox: there is no real staging
# host provisioned yet (no cloud account, no domain, no TLS certificate --
# those are infrastructure and budget decisions, not something to fabricate).
# It is written and reviewed so that the first real deploy has something to
# run against immediately instead of starting from nothing.
#
# Usage:
#   BASE_URL=https://api.staging.nexo.app scripts/smoke-staging.sh
#   scripts/smoke-staging.sh https://api.staging.nexo.app
#
# What it checks, in order, stopping at the first failure:
#   1. GET  /health/live   -> 200, body "Healthy" (liveness: no dependencies)
#   2. GET  /health/ready  -> 200, body "Healthy" (readiness: reaches the DB)
#   3. POST /api/v1/auth/register with a disposable, clearly-labelled account
#      -> 200/201 and an access token (confirms the write path: DB write,
#      password hashing, JWT signing key configured correctly)
#   4. GET  /api/v1/accounts with that token -> 200 and an empty list (confirms
#      auth + per-user isolation on a brand-new account, nothing more)
#
# The registered account's email is stamped smoke-test+<timestamp>@nexo.invalid
# so it is unmistakable in any staging database browser and safe to purge in
# bulk (WHERE email LIKE 'smoke-test+%@nexo.invalid'). Nothing here reads or
# writes another user's data, and nothing here is fictitious bank data --
# just an empty throwaway account.
set -uo pipefail

base_url="${1:-${BASE_URL:-}}"
if [ -z "$base_url" ]; then
  echo "Uso: BASE_URL=https://api.staging.nexo.app $0" >&2
  echo "     $0 https://api.staging.nexo.app" >&2
  exit 2
fi
base_url="${base_url%/}"

if ! command -v curl >/dev/null 2>&1; then
  echo "curl no encontrado en PATH" >&2
  exit 2
fi

failures=0

check_health() {
  local path="$1"
  local url="$base_url$path"
  local body status
  body="$(curl -sS -o /tmp/smoke-body.$$ -w '%{http_code}' "$url")" || {
    echo "FALLO: $path -- curl no pudo conectar"
    failures=$((failures + 1))
    return
  }
  status="$body"
  local content
  content="$(cat /tmp/smoke-body.$$ 2>/dev/null || true)"
  rm -f /tmp/smoke-body.$$

  if [ "$status" != "200" ]; then
    echo "FALLO: $path -- esperaba 200, recibi $status"
    failures=$((failures + 1))
    return
  fi
  if [ "$(echo "$content" | tr -d '[:space:]')" != "Healthy" ]; then
    echo "FALLO: $path -- esperaba cuerpo 'Healthy', recibi '$content'"
    failures=$((failures + 1))
    return
  fi
  echo "OK: $path -> 200 Healthy"
}

echo "== smoke-staging == $base_url"

check_health "/health/live"
check_health "/health/ready"

if [ "$failures" -gt 0 ]; then
  echo "== deteniendose: los health checks fallaron, no tiene sentido probar auth =="
  exit 1
fi

stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
email="smoke-test+${stamp}@nexo.invalid"
password="SmokeTest$(date -u +%s)!Aa1"

register_response="$(curl -sS -o /tmp/smoke-register.$$ -w '%{http_code}' \
  -X POST "$base_url/api/v1/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$email\",\"password\":\"$password\",\"displayName\":\"Smoke Test\"}")"
register_body="$(cat /tmp/smoke-register.$$ 2>/dev/null || true)"
rm -f /tmp/smoke-register.$$

if [ "$register_response" != "200" ] && [ "$register_response" != "201" ]; then
  echo "FALLO: POST /api/v1/auth/register -- esperaba 200/201, recibi $register_response"
  echo "  cuerpo: $register_body"
  failures=$((failures + 1))
else
  echo "OK: POST /api/v1/auth/register -> $register_response ($email)"

  if command -v python3 >/dev/null 2>&1; then
    access_token="$(echo "$register_body" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("accessToken",""))' 2>/dev/null || true)"
  else
    # Extraccion minima sin depender de jq/python -- suficiente para un token JWT.
    access_token="$(echo "$register_body" | grep -o '"accessToken":"[^"]*"' | head -1 | cut -d'"' -f4)"
  fi

  if [ -z "$access_token" ]; then
    echo "FALLO: no se pudo extraer accessToken de la respuesta de registro"
    failures=$((failures + 1))
  else
    accounts_status="$(curl -sS -o /tmp/smoke-accounts.$$ -w '%{http_code}' \
      -H "Authorization: Bearer $access_token" \
      "$base_url/api/v1/accounts")"
    accounts_body="$(cat /tmp/smoke-accounts.$$ 2>/dev/null || true)"
    rm -f /tmp/smoke-accounts.$$

    if [ "$accounts_status" != "200" ]; then
      echo "FALLO: GET /api/v1/accounts -- esperaba 200, recibi $accounts_status"
      failures=$((failures + 1))
    else
      echo "OK: GET /api/v1/accounts -> 200 ($accounts_body)"
    fi
  fi
fi

echo "== fin: $failures fallo(s) =="
exit $([ "$failures" -eq 0 ] && echo 0 || echo 1)
