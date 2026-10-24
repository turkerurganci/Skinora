#!/bin/bash
# =============================================================================
# Skinora — SQL Server entrypoint guard (T171b, DEFERRED_BACKLOG
# ThirdPartyCredentialsInPlainEnv, DEPLOY_RUNBOOK §L.9).
#
# The sa password is the Docker secret mssql_sa_password, mounted at
# /run/secrets/mssql_sa_password. SQL Server reads it ITSELF through
# MSSQL_SA_PASSWORD_FILE (measured on mcr.microsoft.com/mssql/server:2022-latest,
# digest sha256:2dca9ee5…, 2026-10-24: the file's password logs in, a trailing
# newline is dropped). The password therefore never sits in the container's
# environment — `docker inspect` shows only the path.
#
# Why a guard in front of it: SQL Server does NOT stop when that file is
# missing or unreadable — measured: a directory at the path (what Docker
# creates for a missing source file) and no password at all both start a
# server. On an EMPTY data volume that is worse than a crash: the server
# initialises master with a password nobody knows, and a fixed file later
# changes nothing (the sa password is only applied on first initialisation)
# — the volume has to be wiped. So the file is checked first, fail-closed:
#
#   CREDENTIAL_IN_ENV      MSSQL_SA_PASSWORD / SA_PASSWORD set as a variable
#   CREDENTIAL_MISSING     no MSSQL_SA_PASSWORD_FILE, no file, or a blank one
#   CREDENTIAL_UNREADABLE  the path exists but is not a readable file
#   CREDENTIAL_INVALID     a CR (Windows line ending) or surrounding blanks —
#                          the backend trims them, SQL Server may not; save
#                          the file with `printf '%s\n' … >` (secrets/README.md)
#
# The value is never printed. Then the image's own entrypoint runs unchanged
# (launch_sqlservr.sh "$@", compose passes /opt/mssql/bin/sqlservr).
#
#   mssql-entrypoint.sh /opt/mssql/bin/sqlservr   the container's start
#   mssql-entrypoint.sh --healthcheck             compose healthcheck: SELECT 1
#                                                 as sa, the password handed to
#                                                 sqlcmd through SQLCMDPASSWORD
#                                                 (environment), never argv
#   mssql-entrypoint.sh --check                   the file checks only (tests)
# =============================================================================
set -euo pipefail

LAUNCH="${MSSQL_LAUNCH:-/opt/mssql/bin/launch_sqlservr.sh}"
SQLCMD="${SQLCMD:-/opt/mssql-tools18/bin/sqlcmd}"

fail() {
  printf '%s: %s\n' "$1" "$2" >&2
  exit 1
}

# Prints the password (stdout, for the caller to capture) after the checks.
read_password() {
  if [ -n "${MSSQL_SA_PASSWORD:-}" ] || [ -n "${SA_PASSWORD:-}" ]; then
    fail CREDENTIAL_IN_ENV "MSSQL_SA_PASSWORD / SA_PASSWORD is set as an environment variable - the sa password is read only from its Docker secret file (MSSQL_SA_PASSWORD_FILE); remove the variable (DEPLOY_RUNBOOK L.9)"
  fi
  local file="${MSSQL_SA_PASSWORD_FILE:-}"
  [ -n "$file" ] || fail CREDENTIAL_MISSING "MSSQL_SA_PASSWORD_FILE is not set - mount the mssql_sa_password secret (secrets/README.md)"
  if [ -e "$file" ] && { [ ! -f "$file" ] || [ ! -r "$file" ]; }; then
    fail CREDENTIAL_UNREADABLE "$file exists but is not a readable file - create the mssql_sa_password secret file (secrets/README.md) and remove the directory Docker made in its place"
  fi
  [ -f "$file" ] || fail CREDENTIAL_MISSING "$file does not exist - create the mssql_sa_password secret file (secrets/README.md)"
  local value
  # A CR is counted on the raw bytes: some shells (MSYS) drop it from $(...).
  if [ "$(tr -cd '\r' <"$file" | wc -c | tr -d ' ')" -gt 0 ]; then
    fail CREDENTIAL_INVALID "$file has a Windows line ending (CR) - save it with LF only (secrets/README.md)"
  fi
  # $(...) drops trailing newlines; anything else is kept and judged below.
  value="$(cat "$file")"
  [ -n "${value//[[:space:]]/}" ] || fail CREDENTIAL_MISSING "$file is empty - the database does not start without its sa password"
  if [ "$value" != "${value#[[:space:]]}" ] || [ "$value" != "${value%[[:space:]]}" ] || [[ "$value" == *$'\n'* ]]; then
    fail CREDENTIAL_INVALID "$file must hold the password alone on one line, without surrounding blanks"
  fi
  printf '%s' "$value"
}

case "${1:-}" in
  --check)
    read_password >/dev/null
    echo "mssql sa password file: ok"
    ;;
  --healthcheck)
    SQLCMDPASSWORD="$(read_password)"
    export SQLCMDPASSWORD
    exec "$SQLCMD" -S localhost -U sa -C -Q 'SELECT 1' -b -h -1 >/dev/null
    ;;
  *)
    read_password >/dev/null
    exec "$LAUNCH" "$@"
    ;;
esac
