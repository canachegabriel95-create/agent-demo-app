#!/usr/bin/env bash
# Provisions the initial administrator by running db/scripts/provision-admin.sql.
#
# Usage: scripts/provision-admin.sh [--email EMAIL] [--name NAME] [--connection CONNECTION]
#
#   ADMIN_EMAIL / ADMIN_NAME   used when the options are not given (also read from .env)
#   ADMIN_PASSWORD_HASH        optional; skips the password prompt when you already have a hash
#   --connection / PSQL_CONNECTION
#                              run the local psql client against this connection string or URI instead of
#                              'docker compose exec postgres psql' (e.g. an externally managed database)
#
# The password is typed into the masked prompt of 'hash-password'; it is never stored, logged or passed on a
# command line. Running the script again is safe: an existing user is never changed.
set -euo pipefail

# shellcheck source=lib.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
cd "$ROOT_DIR" || exit 1

SQL_FILE="db/scripts/provision-admin.sql"
TOOLS_PROJECT="backend/tools/ContentReview.Tools"

if [ -f .env ]; then
  load_env .env
fi

email=${ADMIN_EMAIL:-}
name=${ADMIN_NAME:-}
connection=${PSQL_CONNECTION:-}

while [ $# -gt 0 ]; do
  case "$1" in
    --email) email=${2:?--email needs a value}; shift 2 ;;
    --name) name=${2:?--name needs a value}; shift 2 ;;
    --connection) connection=${2:?--connection needs a value}; shift 2 ;;
    -h | --help) sed -n '2,13p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) warn "Unknown option: $1 (see --help)"; exit 2 ;;
  esac
done

if [ -z "$email" ] || [ -z "$name" ]; then
  warn "ADMIN_EMAIL and ADMIN_NAME are required: set them in .env (see .env.example) or pass --email and --name."
  exit 2
fi

hash=${ADMIN_PASSWORD_HASH:-}
if [ -z "$hash" ]; then
  command -v dotnet >/dev/null 2>&1 || { warn ".NET SDK 10 is required to generate the password hash."; exit 1; }
  info "Choose the administrator password (input is hidden)."
  # stdout carries only the hash; the prompts and errors of hash-password go to the terminal via stderr.
  hash=$(dotnet run --no-launch-profile --project "$TOOLS_PROJECT" -- hash-password | tail -n 1) \
    || { warn "Could not generate the password hash."; exit 1; }
fi

psql_args=(-X -q -v ON_ERROR_STOP=1 -v "admin_email=$email" -v "admin_name=$name" -v "admin_password_hash=$hash")

if [ -n "$connection" ]; then
  command -v psql >/dev/null 2>&1 || { warn "psql is not installed; omit --connection to use the Docker container."; exit 1; }
  psql "$connection" "${psql_args[@]}" -f "$SQL_FILE"
else
  docker info >/dev/null 2>&1 || { warn "Docker is not running. Start it (scripts/start.sh) or use --connection."; exit 1; }
  : "${POSTGRES_USER:=content_review}"
  : "${POSTGRES_DB:=content_review}"
  docker compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" "${psql_args[@]}" -f - <"$SQL_FILE"
fi

ok "Done. Sign in to the Admin Portal (http://localhost:5173) as $email."
