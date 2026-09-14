#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$root/.env.production"
env_template="$root/.env.production.example"
compose_file="$root/docker-compose.production.yml"

auto_migrate=""
run_smoke=1
dry_run=0
api_url=""
api_port=""
db_host=""
db_port=""
db_name=""
db_user=""
db_password=""
db_ssl=""
inbound_domain=""
cors_origin=""

usage() {
  cat <<'EOF'
Usage:
  scripts/start-production.sh [options]

Options:
  --db-host HOST        PostgreSQL host on the server or managed provider
  --db-port PORT        PostgreSQL port. Default: value in env or 5432
  --db-name NAME        PostgreSQL database. Default: value in env or fino_app
  --db-user USER        PostgreSQL user. Default: value in env or fino_app
  --db-password PASS    PostgreSQL password. If omitted and missing, generated
  --db-ssl MODE         PostgreSQL SSL mode: require or disable. Default: require
  --api-url URL         Public API URL, e.g. https://api.fino.app
  --api-port PORT       Host port to publish the API. Default: 8080
  --inbound-domain D    Email forwarding inbound domain
  --cors-origin ORIGIN  First allowed CORS origin. Empty fails closed
  --auto-migrate        Start API with Nexo__Database__AutoMigrate=true
  --no-auto-migrate     Start API with Nexo__Database__AutoMigrate=false
  --no-smoke            Do not run health smoke after docker compose up
  --dry-run             Generate env and validate compose, but do not start
  -h, --help            Show this help

Examples:
  scripts/start-production.sh --db-host my-postgres.example.com --api-url https://api.fino.app
  scripts/start-production.sh --db-host host.docker.internal --db-ssl disable --api-url http://localhost:8080

Notes:
  - Existing non-placeholder secrets are preserved.
  - This deploys only the API container; PostgreSQL must already exist.
  - If PostgreSQL is installed on the same Docker host, use
    --db-host host.docker.internal. The production compose maps that name to the
    host gateway.
  - Gmail/Outlook OAuth credentials cannot be generated; configure them in the
    env file when those providers are activated.
EOF
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --db-host) db_host="$2"; shift 2 ;;
    --db-port) db_port="$2"; shift 2 ;;
    --db-name) db_name="$2"; shift 2 ;;
    --db-user) db_user="$2"; shift 2 ;;
    --db-password) db_password="$2"; shift 2 ;;
    --db-ssl) db_ssl="$2"; shift 2 ;;
    --api-url) api_url="$2"; shift 2 ;;
    --api-port) api_port="$2"; shift 2 ;;
    --inbound-domain) inbound_domain="$2"; shift 2 ;;
    --cors-origin) cors_origin="$2"; shift 2 ;;
    --auto-migrate) auto_migrate="true"; shift ;;
    --no-auto-migrate) auto_migrate="false"; shift ;;
    --no-smoke) run_smoke=0; shift ;;
    --dry-run) dry_run=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

log() {
  printf '[production] %s\n' "$*"
}

die() {
  printf '[production] ERROR: %s\n' "$*" >&2
  exit 1
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "Missing required command: $1"
}

random_b64() {
  local bytes="$1"

  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 "$bytes" | tr -d '\n'
    return
  fi

  if command -v base64 >/dev/null 2>&1; then
    dd if=/dev/urandom bs="$bytes" count=1 2>/dev/null | base64 | tr -d '\n'
    return
  fi

  die "openssl or base64 is required to generate secrets"
}

random_password() {
  random_b64 36
}

get_env_value() {
  local key="$1"
  if [ ! -f "$env_file" ]; then
    return 0
  fi

  grep -E "^${key}=" "$env_file" | tail -n 1 | sed "s/^${key}=//" || true
}

set_env_value() {
  local key="$1"
  local value="$2"
  local tmp

  tmp="$(mktemp)"
  awk -v key="$key" -v value="$value" '
    BEGIN { found = 0 }
    $0 ~ "^" key "=" {
      print key "=" value
      found = 1
      next
    }
    { print }
    END {
      if (found == 0) {
        print key "=" value
      }
    }
  ' "$env_file" > "$tmp"
  mv "$tmp" "$env_file"
}

is_missing_or_placeholder() {
  local value="${1:-}"
  [ -z "$value" ] && return 0
  case "$value" in
    CHANGE_ME*|*CHANGE_ME*) return 0 ;;
    *) return 1 ;;
  esac
}

ensure_generated_secret() {
  local key="$1"
  local bytes="$2"
  local value

  value="$(get_env_value "$key")"
  if is_missing_or_placeholder "$value"; then
    set_env_value "$key" "$(random_b64 "$bytes")"
    log "generated $key"
  else
    log "kept existing $key"
  fi
}

ensure_generated_password() {
  local key="$1"
  local value

  value="$(get_env_value "$key")"
  if is_missing_or_placeholder "$value"; then
    set_env_value "$key" "$(random_password)"
    log "generated $key"
  else
    log "kept existing $key"
  fi
}

ensure_env_file() {
  if [ ! -f "$env_file" ]; then
    [ -f "$env_template" ] || die "Template not found: $env_template"
    cp "$env_template" "$env_file"
    chmod 600 "$env_file" 2>/dev/null || true
    log "created $env_file from template"
  else
    log "using existing $env_file"
  fi
}

resolve_runtime_values() {
  local current

  [ -n "$db_name" ] || db_name="$(get_env_value POSTGRES_DB)"
  [ -n "$db_user" ] || db_user="$(get_env_value POSTGRES_USER)"
  [ -n "$db_port" ] || db_port="$(get_env_value POSTGRES_PORT)"
  [ -n "$db_password" ] || db_password="$(get_env_value POSTGRES_PASSWORD)"
  [ -n "$db_ssl" ] || db_ssl="${DB_SSL:-require}"
  [ -n "$api_port" ] || api_port="${API_PORT:-8080}"

  db_name="${db_name:-fino_app}"
  db_user="${db_user:-fino_app}"
  db_port="${db_port:-5432}"
  case "$db_ssl" in
    require|Require) db_ssl="Require" ;;
    disable|Disable) db_ssl="Disable" ;;
    *) die "--db-ssl must be 'require' or 'disable'" ;;
  esac

  set_env_value POSTGRES_DB "$db_name"
  set_env_value POSTGRES_USER "$db_user"
  set_env_value POSTGRES_PORT "$db_port"
  set_env_value POSTGRES_SSL_MODE "$db_ssl"
  if [ -n "$db_password" ]; then
    set_env_value POSTGRES_PASSWORD "$db_password"
  else
    ensure_generated_password POSTGRES_PASSWORD
    db_password="$(get_env_value POSTGRES_PASSWORD)"
  fi

  ensure_generated_secret Nexo__Jwt__SigningKey 48
  ensure_generated_secret Nexo__Secrets__EncryptionKey 32
  ensure_generated_secret Nexo__EmailIngestion__Forwarding__WebhookSecret 32

  set_env_value ASPNETCORE_ENVIRONMENT Production
  set_env_value ASPNETCORE_URLS "http://0.0.0.0:8080"
  set_env_value Nexo__Seed__Demo false
  set_env_value Nexo__Workers__Enabled true
  set_env_value Nexo__RateLimiting__Enabled true
  set_env_value Nexo__Push__Enabled true

  if [ -n "$api_url" ]; then
    api_url="${api_url%/}"
    set_env_value EXPO_PUBLIC_API_URL "$api_url"
    set_env_value Nexo__EmailIngestion__Gmail__RedirectUri "$api_url/oauth/gmail/callback"
    set_env_value Nexo__EmailIngestion__Outlook__RedirectUri "$api_url/oauth/outlook/callback"
  fi

  if [ -n "$inbound_domain" ]; then
    set_env_value Nexo__EmailIngestion__Forwarding__InboundDomain "$inbound_domain"
  fi

  if [ -n "$cors_origin" ]; then
    set_env_value Nexo__Cors__AllowedOrigins__0 "$cors_origin"
  fi

  current="$(get_env_value ConnectionStrings__Default)"
  if [ -n "$db_host" ]; then
    set_env_value POSTGRES_HOST "$db_host"
    set_env_value ConnectionStrings__Default "Host=$db_host;Port=$db_port;Database=$db_name;Username=$db_user;Password=$db_password;SSL Mode=$db_ssl;Trust Server Certificate=false"
  elif is_missing_or_placeholder "$current"; then
    die "ConnectionStrings__Default still has placeholders. Pass --db-host HOST or fill $env_file before deploying."
  fi

  if [ -n "$auto_migrate" ]; then
    set_env_value Nexo__Database__AutoMigrate "$auto_migrate"
  else
    current="$(get_env_value Nexo__Database__AutoMigrate)"
    if is_missing_or_placeholder "$current"; then
      set_env_value Nexo__Database__AutoMigrate false
    fi
  fi

  current="$(get_env_value API_PORT)"
  if [ -n "$api_port" ] || [ -z "$current" ]; then
    set_env_value API_PORT "$api_port"
  fi
}

compose() {
  docker compose --env-file "$env_file" -f "$compose_file" "$@"
}

wait_for_smoke() {
  local port
  port="$(get_env_value API_PORT)"
  port="${port:-8080}"

  if [ -n "$api_url" ]; then
    "$root/scripts/smoke-production.sh" "$api_url"
  else
    "$root/scripts/smoke-production.sh" "http://127.0.0.1:$port"
  fi
}

require_cmd docker
docker compose version >/dev/null 2>&1 || die "Docker Compose v2 is required (docker compose ...)"
[ -f "$compose_file" ] || die "Compose file not found: $compose_file"

ensure_env_file
resolve_runtime_values

log "validating docker compose configuration"
compose config >/dev/null

if [ "$dry_run" -eq 1 ]; then
  log "dry-run OK; env generated and compose configuration is valid"
  exit 0
fi

log "starting production stack"
compose up -d --build

if [ "$run_smoke" -eq 1 ]; then
  log "running production smoke"
  wait_for_smoke
fi

log "production stack is up"
compose ps
