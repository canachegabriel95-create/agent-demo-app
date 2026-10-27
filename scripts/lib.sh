#!/usr/bin/env bash
# Shared helpers for start.sh and stop.sh. Source this file; do not execute it.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_DIR="$ROOT_DIR/.run"

# These are used by the scripts that source this file.
# shellcheck disable=SC2034
API_PORT=5080
# shellcheck disable=SC2034
ADMIN_PORT=5173
# shellcheck disable=SC2034
AUTHOR_PORT=5174

info() { printf '%s\n' "$*"; }
ok() { printf '  [ok] %s\n' "$*"; }
warn() { printf '  [!!] %s\n' "$*" >&2; }

# True when something accepts connections on localhost:$1.
port_in_use() {
  (exec 3<>"/dev/tcp/localhost/$1") 2>/dev/null
}

# Prints the HTTP status of a URL, or 000 when nothing answers.
http_status() {
  curl -s -o /dev/null -m 3 -w '%{http_code}' "$1" 2>/dev/null || true
}

pid_file() { printf '%s/%s.pid' "$RUN_DIR" "$1"; }
log_file() { printf '%s/%s.log' "$RUN_DIR" "$1"; }

pid_alive() {
  [ -n "${1:-}" ] && kill -0 "$1" 2>/dev/null
}

# Guards against a recycled PID: only treat the process as ours if it is one of the tools we launch.
looks_like_ours() {
  local command
  command=$(ps -p "$1" -o args= 2>/dev/null || true)
  [[ $command == *dotnet* || $command == *npm* || $command == *node* || $command == *ContentReview* ]]
}

# Prints the PID recorded for a component if that process is still running, otherwise nothing.
running_pid() {
  local file pid
  file=$(pid_file "$1")
  [ -f "$file" ] || return 0
  pid=$(cat "$file" 2>/dev/null || true)
  if pid_alive "$pid" && looks_like_ours "$pid"; then
    printf '%s' "$pid"
  fi
}

# Loads KEY=VALUE lines from a .env file into the environment. Values already set in the environment win
# (same precedence as docker compose). The file is parsed, not sourced, because values such as the
# connection string contain ';'.
load_env() {
  local file=$1 line key value
  local line_re='^[[:space:]]*([A-Za-z_][A-Za-z0-9_]*)=(.*)$'
  local double_re='^"(.*)"$'
  local single_re="^'(.*)'\$"

  while IFS= read -r line || [ -n "$line" ]; do
    line=${line%$'\r'}
    if [[ $line =~ $line_re ]]; then
      key=${BASH_REMATCH[1]}
      value=${BASH_REMATCH[2]}
      if [[ $value =~ $double_re ]]; then
        value=${BASH_REMATCH[1]}
      elif [[ $value =~ $single_re ]]; then
        value=${BASH_REMATCH[1]}
      fi
      if [ -z "${!key+x}" ]; then
        export "$key=$value"
      fi
    fi
  done <"$file"
}

# Stops a component's whole process tree (it was started as its own process group).
stop_component() {
  local name=$1 pid
  pid=$(running_pid "$name")

  if [ -z "$pid" ]; then
    rm -f "$(pid_file "$name")"
    return 1
  fi

  kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true

  local waited=0
  while pid_alive "$pid" && [ "$waited" -lt 30 ]; do
    sleep 0.5
    waited=$((waited + 1))
  done

  if pid_alive "$pid"; then
    kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null || true
  fi

  rm -f "$(pid_file "$name")"
  return 0
}
