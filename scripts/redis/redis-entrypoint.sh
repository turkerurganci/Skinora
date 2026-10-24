#!/bin/sh
# =============================================================================
# Skinora — Redis entrypoint (T171b, DEFERRED_BACKLOG
# ThirdPartyCredentialsInPlainEnv, DEPLOY_RUNBOOK §L.9).
#
# The Redis password is the Docker secret redis_password, mounted at
# /run/secrets/redis_password. The official redis image has NO *_FILE support
# (its docker-entrypoint.sh only drops to the redis user and execs), and the
# old `redis-server --requirepass ${REDIS_PASSWORD}` put the password on the
# COMMAND LINE: `docker inspect` (Config.Cmd) and `ps` both showed it. This
# script reads the file and writes `requirepass` into a config file on a tmpfs
# (compose: tmpfs /run/skinora-redis — memory only, never the container layer
# or a volume), readable by the redis user alone, and starts redis-server
# with THAT file. The password reaches no argv and no environment.
#
# Fail-closed (the value is never printed):
#   CREDENTIAL_IN_ENV      REDIS_PASSWORD set as a variable
#   CREDENTIAL_MISSING     no file, or a blank one (Redis without a password
#                          would accept anyone on the network)
#   CREDENTIAL_UNREADABLE  the path exists but is not a readable file
#                          (Docker's directory for a missing source file)
#   CREDENTIAL_INVALID     a CR other than one trailing CRLF, or a character
#                          outside A-Z a-z 0-9 . _ ~ + = - — the
#                          password is also written into the backend's
#                          connection string (comma-separated) and the Steam
#                          service's redis:// URL; `openssl rand -hex 32` fits
#
#   redis-entrypoint.sh [redis-server options]   the container's start
#                                                (compose: --appendonly yes)
#   redis-entrypoint.sh --healthcheck            PING through redis-cli, the
#                                                password in REDISCLI_AUTH
#                                                (environment), never argv
#   redis-entrypoint.sh --check                  the file checks only (tests)
# =============================================================================
set -eu

FILE="${REDIS_PASSWORD_FILE:-/run/secrets/redis_password}"
CONF_DIR="${REDIS_AUTH_CONF_DIR:-/run/skinora-redis}"
ENTRYPOINT="${REDIS_DOCKER_ENTRYPOINT:-docker-entrypoint.sh}"
CLI="${REDIS_CLI:-redis-cli}"

fail() {
  printf '%s: %s\n' "$1" "$2" >&2
  exit 1
}

check_file() {
  if [ -n "${REDIS_PASSWORD:-}" ]; then
    fail CREDENTIAL_IN_ENV "REDIS_PASSWORD is set as an environment variable - the Redis password is read only from its Docker secret file ($FILE); remove the variable (DEPLOY_RUNBOOK L.9)"
  fi
  if [ -e "$FILE" ] && { [ ! -f "$FILE" ] || [ ! -r "$FILE" ]; }; then
    fail CREDENTIAL_UNREADABLE "$FILE exists but is not a readable file - create the redis_password secret file (secrets/README.md) and remove the directory Docker made in its place"
  fi
  [ -f "$FILE" ] || fail CREDENTIAL_MISSING "$FILE does not exist - create the redis_password secret file (secrets/README.md)"
  # Docker Desktop, missing source file, `compose run`: the mount point passes
  # -f yet every read fails with an I/O error (measured, T171b) — so read once.
  cat "$FILE" >/dev/null 2>&1 || fail CREDENTIAL_UNREADABLE "$FILE cannot be read - create the redis_password secret file (secrets/README.md) and remove what Docker made in its place"
}

# Prints the password (stdout, for the caller to capture) after the checks.
read_password() {
  check_file
  # CRs are counted on the raw bytes (some shells — MSYS — drop them from
  # $(...)). One CR is allowed: the one of a trailing CRLF (Git Bash's openssl
  # writes CRLF on Windows; the backend trims it too). Any other is refused.
  crs="$(tr -cd '\r' <"$FILE" | wc -c | tr -d ' ')"
  if [ "$crs" -gt 0 ]; then
    last="$(tail -c 2 "$FILE" | od -An -tx1 | tr -d ' \n')"
    case "$last" in
      *0d0a | *0d) [ "$crs" -eq 1 ] || fail CREDENTIAL_INVALID "$FILE has a CR inside the password - one line, LF or CRLF only" ;;
      *) fail CREDENTIAL_INVALID "$FILE has a CR inside the password - one line, LF or CRLF only" ;;
    esac
  fi
  # One line; without the CR, and $(...) drops the trailing LF.
  value="$(tr -d '\r' <"$FILE")"
  [ -n "$value" ] || fail CREDENTIAL_MISSING "$FILE is empty - Redis does not start without a password"
  case "$value" in
    *[!A-Za-z0-9._~+=-]*)
      fail CREDENTIAL_INVALID "$FILE may hold only A-Z a-z 0-9 and . _ ~ + = - on one line (openssl rand -hex 32; no CR, no blanks)" ;;
  esac
  printf '%s' "$value"
}

case "${1:-}" in
  --check)
    read_password >/dev/null
    echo "redis password file: ok"
    ;;
  --healthcheck)
    REDISCLI_AUTH="$(read_password)"
    export REDISCLI_AUTH
    reply="$("$CLI" --no-auth-warning ping 2>&1 || true)"
    [ "$reply" = "PONG" ] || { echo "redis healthcheck: no PONG" >&2; exit 1; }
    ;;
  *)
    password="$(read_password)"
    mkdir -p "$CONF_DIR"
    conf="$CONF_DIR/redis-auth.conf"
    # umask: the file is created 0600; the redis user (the image's entrypoint
    # drops to it) gets it through chown when we run as root.
    old_umask="$(umask)"
    umask 077
    printf 'requirepass "%s"\n' "$password" >"$conf"
    umask "$old_umask"
    unset password
    if [ "$(id -u)" = 0 ] && id redis >/dev/null 2>&1; then
      chown redis "$conf"
    fi
    echo "redis: password loaded from $FILE (value not shown)"
    exec "$ENTRYPOINT" redis-server "$conf" "$@"
    ;;
esac
