#!/usr/bin/env bash
# Starts the whole local environment: PostgreSQL + pgAdmin (Docker), the API and both portals.
# Usage: scripts/start.sh        Stop it again with scripts/stop.sh.
set -euo pipefail

# shellcheck source=lib.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
cd "$ROOT_DIR" || exit 1

REQUIRED_DOTNET_MAJOR=10
REQUIRED_NODE_MAJOR=22
POSTGRES_TIMEOUT_SECONDS=90
API_TIMEOUT_SECONDS=120
PORTAL_TIMEOUT_SECONDS=90

problems=()

check_prerequisites() {
  local version major

  # Docker
  if ! command -v docker >/dev/null 2>&1; then
    problems+=("Docker is not installed. Install Docker Desktop or Docker Engine: https://docs.docker.com/get-docker/")
  elif ! docker info >/dev/null 2>&1; then
    problems+=("Docker is installed but not running. Start Docker Desktop (or the Docker daemon) and try again.")
  elif ! docker compose version >/dev/null 2>&1; then
    problems+=("Docker Compose v2 is not available ('docker compose'). Update Docker or install the Compose plugin.")
  fi

  # .NET SDK
  if ! command -v dotnet >/dev/null 2>&1; then
    problems+=(".NET SDK $REQUIRED_DOTNET_MAJOR is not installed: https://dotnet.microsoft.com/download")
  else
    version=$(dotnet --version 2>/dev/null || echo "0")
    major=${version%%.*}
    if [ "${major:-0}" -lt "$REQUIRED_DOTNET_MAJOR" ] 2>/dev/null; then
      problems+=(".NET SDK $REQUIRED_DOTNET_MAJOR or newer is required (found $version): https://dotnet.microsoft.com/download")
    fi
  fi

  # Node.js
  if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
    problems+=("Node.js $REQUIRED_NODE_MAJOR LTS (with npm) is not installed: https://nodejs.org/")
  else
    version=$(node --version 2>/dev/null | sed 's/^v//')
    major=${version%%.*}
    if [ "${major:-0}" -lt "$REQUIRED_NODE_MAJOR" ] 2>/dev/null; then
      problems+=("Node.js $REQUIRED_NODE_MAJOR LTS or newer is required (found $version): https://nodejs.org/")
    fi
  fi

  if ! command -v curl >/dev/null 2>&1; then
    problems+=("curl is not installed (the script uses it to wait for the services).")
  fi

  # Configuration
  if [ ! -f "$ROOT_DIR/.env" ]; then
    problems+=("Missing .env file. Create it with: cp .env.example .env  (then replace the placeholder passwords).")
  else
    load_env "$ROOT_DIR/.env"
    local key
    for key in POSTGRES_USER POSTGRES_PASSWORD PGADMIN_DEFAULT_EMAIL PGADMIN_DEFAULT_PASSWORD ConnectionStrings__Default; do
      if [ -z "${!key:-}" ]; then
        problems+=("$key is not set in .env (see .env.example).")
      fi
    done
  fi

  # Portal dependencies
  local app
  for app in admin-portal author-portal; do
    if [ ! -d "$ROOT_DIR/frontend/$app/node_modules" ]; then
      problems+=("Dependencies of $app are not installed. Run: (cd frontend/$app && npm ci)")
    fi
  done

  # Ports used by the apps (skip the ones our own earlier run is still holding)
  local entry name port label
  for entry in "api:$API_PORT:API" "admin:$ADMIN_PORT:Admin Portal" "author:$AUTHOR_PORT:Author Portal"; do
    name=${entry%%:*}
    entry=${entry#*:}
    port=${entry%%:*}
    label=${entry#*:}
    if [ -z "$(running_pid "$name")" ] && port_in_use "$port"; then
      problems+=("Port $port ($label) is already in use. Stop the program using it, or run scripts/stop.sh if it is a previous run.")
    fi
  done
}

wait_for_postgres() {
  local container status waited=0
  container=$(docker compose ps -q postgres 2>/dev/null || true)
  while [ "$waited" -lt "$POSTGRES_TIMEOUT_SECONDS" ]; do
    status=$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container" 2>/dev/null || echo "unknown")
    if [ "$status" = "healthy" ]; then
      return 0
    fi
    sleep 2
    waited=$((waited + 2))
  done
  return 1
}

# Starts "$@" in $2 as its own process group (so stop.sh can stop the whole tree), logging to .run/$1.log.
start_background() {
  local name=$1 dir=$2
  shift 2
  : >"$(log_file "$name")"
  (
    set -m
    cd "$dir"
    nohup "$@" >>"$(log_file "$name")" 2>&1 &
    echo $! >"$(pid_file "$name")"
  )
}

# Waits until $2 returns HTTP $3 (default 200) while the process $1 stays alive.
wait_for_http() {
  local name=$1 url=$2 expected=${3:-200} timeout=$4 waited=0 pid status=000
  while [ "$waited" -lt "$timeout" ]; do
    status=$(http_status "$url")
    if [ "$status" = "$expected" ]; then
      return 0
    fi
    pid=$(running_pid "$name")
    if [ -z "$pid" ]; then
      warn "$name stopped unexpectedly. Last log lines ($(log_file "$name")):"
      tail -n 20 "$(log_file "$name")" >&2 || true
      return 2
    fi
    sleep 2
    waited=$((waited + 2))
  done
  LAST_STATUS=$status
  return 1
}

LAST_STATUS=000

# 1. Prerequisites ------------------------------------------------------------------------------------------
info "Checking prerequisites..."
check_prerequisites
if [ "${#problems[@]}" -gt 0 ]; then
  warn "Cannot start. Fix the following and run scripts/start.sh again:"
  for problem in "${problems[@]}"; do
    printf '   - %s\n' "$problem" >&2
  done
  exit 1
fi
ok "Docker, .NET SDK, Node.js, configuration, dependencies and ports are ready"

mkdir -p "$RUN_DIR"

# 2. PostgreSQL and pgAdmin ---------------------------------------------------------------------------------
info "Starting PostgreSQL and pgAdmin..."
if ! compose_output=$(docker compose up -d 2>&1); then
  warn "docker compose up failed:"
  printf '%s\n' "$compose_output" >&2
  warn "A port conflict on 5432 or 5050? Change POSTGRES_PORT / PGADMIN_PORT in .env."
  exit 1
fi
if ! wait_for_postgres; then
  warn "PostgreSQL did not become healthy within ${POSTGRES_TIMEOUT_SECONDS}s. Check: docker compose logs postgres"
  exit 1
fi
ok "PostgreSQL is healthy"

# 3. API (applies migrations at startup) ----------------------------------------------------------------------
info "Building the API..."
if ! build_output=$(dotnet build backend/src/ContentReview.Api -nologo -v q 2>&1); then
  printf '%s\n' "$build_output" >&2
  warn "The API did not build. Fix the errors above and run scripts/start.sh again."
  exit 1
fi
ok "API built"

if [ -n "$(running_pid api)" ]; then
  ok "API is already running"
else
  info "Starting the API (migrations are applied at startup)..."
  export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
  export Database__MigrateOnStartup="${Database__MigrateOnStartup:-true}"
  start_background api "$ROOT_DIR" dotnet run --project backend/src/ContentReview.Api --no-build
fi

if wait_for_http api "http://localhost:$API_PORT/health" 200 "$API_TIMEOUT_SECONDS"; then
  ok "API is healthy (http://localhost:$API_PORT/health)"
else
  rc=$?
  if [ "$rc" -eq 1 ]; then
    warn "The API did not report Healthy within ${API_TIMEOUT_SECONDS}s (last HTTP status: $LAST_STATUS)."
    warn "A 503 usually means it cannot reach the database: check ConnectionStrings__Default in .env."
    warn "Log: .run/api.log   Response: $(curl -s -m 3 "http://localhost:$API_PORT/health" || true)"
  fi
  exit 1
fi

# 4. Portals -------------------------------------------------------------------------------------------------
for entry in "admin:admin-portal:$ADMIN_PORT:Admin Portal" "author:author-portal:$AUTHOR_PORT:Author Portal"; do
  name=${entry%%:*}
  entry=${entry#*:}
  folder=${entry%%:*}
  entry=${entry#*:}
  port=${entry%%:*}
  label=${entry#*:}

  if [ -n "$(running_pid "$name")" ]; then
    ok "$label is already running"
  else
    info "Starting the $label..."
    start_background "$name" "$ROOT_DIR/frontend/$folder" npm run dev
  fi

  if wait_for_http "$name" "http://localhost:$port/" 200 "$PORTAL_TIMEOUT_SECONDS"; then
    ok "$label is up (http://localhost:$port)"
  else
    rc=$?
    if [ "$rc" -eq 1 ]; then
      warn "$label did not answer within ${PORTAL_TIMEOUT_SECONDS}s. Log: .run/$name.log"
    fi
    exit 1
  fi
done

# 5. pgAdmin (slow to start; not fatal) -------------------------------------------------------------------------
PGADMIN_URL="http://localhost:${PGADMIN_PORT:-5050}"
waited=0
until [ "$(http_status "$PGADMIN_URL/misc/ping")" = "200" ] || [ "$waited" -ge 60 ]; do
  sleep 2
  waited=$((waited + 2))
done
if [ "$(http_status "$PGADMIN_URL/misc/ping")" = "200" ]; then
  ok "pgAdmin is up"
else
  warn "pgAdmin is still starting; give it a little longer (docker compose logs pgadmin)."
fi

cat <<EOT

Everything is running:

  Admin Portal   http://localhost:$ADMIN_PORT
  Author Portal  http://localhost:$AUTHOR_PORT
  API            http://localhost:$API_PORT
  Health         http://localhost:$API_PORT/health
  Swagger UI     http://localhost:$API_PORT/swagger   (added by a later story)
  PostgreSQL     localhost:${POSTGRES_PORT:-5432}
  pgAdmin        $PGADMIN_URL

Logs: .run/*.log      Stop everything with: scripts/stop.sh
EOT
