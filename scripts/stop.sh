#!/usr/bin/env bash
# Stops what scripts/start.sh started: the API and both portals, then the Docker containers.
# The database data is kept (docker compose stop, not down). Safe to run more than once.
set -uo pipefail

# shellcheck source=lib.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
cd "$ROOT_DIR" || exit 1

stopped_any=0

for entry in "admin:Admin Portal" "author:Author Portal" "api:API"; do
  name=${entry%%:*}
  label=${entry#*:}
  if stop_component "$name"; then
    ok "Stopped the $label"
    stopped_any=1
  fi
done

if [ "$stopped_any" -eq 0 ]; then
  info "No API or portal processes were running."
fi

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  if docker compose stop >/dev/null 2>&1; then
    ok "Stopped PostgreSQL and pgAdmin (data is kept; 'docker compose down -v' deletes it)"
  else
    warn "Could not stop the containers. Check: docker compose ps"
  fi
else
  info "Docker is not running, so there are no containers to stop."
fi

# Verify that nothing is left listening on the app ports.
leftover=0
for entry in "$API_PORT:API" "$ADMIN_PORT:Admin Portal" "$AUTHOR_PORT:Author Portal"; do
  port=${entry%%:*}
  label=${entry#*:}
  if port_in_use "$port"; then
    warn "Port $port ($label) is still in use by a process that was not started by scripts/start.sh."
    leftover=1
  fi
done

if [ "$leftover" -eq 0 ]; then
  ok "Ports $API_PORT, $ADMIN_PORT and $AUTHOR_PORT are free"
  exit 0
fi
exit 1
