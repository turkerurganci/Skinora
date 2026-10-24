#!/bin/sh
# =============================================================================
# Skinora — heartbeat to the outside monitor (T165, DEPLOY_RUNBOOK §P).
#
# A "dead man's switch": the outside service (Healthchecks.io) alerts when the
# pings STOP. That is the only way to hear about this PC, Docker Desktop or
# the whole stack being down — the in-house Grafana goes down with them.
#
# Every round (HEARTBEAT_INTERVAL_SECONDS, default 60, at least 15 — the
# service rate-limits at 5 pings a minute):
#   1. every URL in HEARTBEAT_CHECK_URLS must answer 2xx (default: the edge's
#      /api/health — nginx -> backend -> database + Redis — and the front page);
#   2. all good  -> GET HEARTBEAT_URL           (the "I am alive" ping);
#      one fails -> no ping; after HEARTBEAT_FAILURES_BEFORE_FAIL failed rounds
#                   in a row (default 3) ONE GET HEARTBEAT_URL/fail, so the
#                   owner hears about a broken site at once instead of after
#                   the grace time. Nothing more until the site recovers.
#
# Without HEARTBEAT_URL the script idles and says so once (development, or a
# trial before the owner has created the check). The URL is a secret of sorts
# (anyone holding it can send pings): it is never printed.
#
# Test hook: HEARTBEAT_ROUNDS=N runs N rounds and exits (no interval floor).
# =============================================================================
set -u

URL="${HEARTBEAT_URL:-}"
# T171a — in the trial the ping URL is a Docker secret (HEARTBEAT_URL_FILE,
# /run/secrets/heartbeat_url), not an environment value. A path that exists but
# is not a readable file (Docker's directory for a missing source) stops the
# container; a missing or empty file idles like an empty HEARTBEAT_URL. Both at
# once is a configuration error. The URL is never printed.
URL_FILE="${HEARTBEAT_URL_FILE:-}"
INTERVAL="${HEARTBEAT_INTERVAL_SECONDS:-60}"
FAILS_BEFORE_FAIL="${HEARTBEAT_FAILURES_BEFORE_FAIL:-3}"
CHECKS="${HEARTBEAT_CHECK_URLS:-http://skinora-reverse-proxy/api/health}"
TIMEOUT="${HEARTBEAT_TIMEOUT_SECONDS:-10}"
ROUNDS="${HEARTBEAT_ROUNDS:-}"

log() {
  echo "$(date -u '+%Y-%m-%dT%H:%M:%SZ') heartbeat: $*"
}

is_uint() {
  case "$1" in
    ''|*[!0-9]*) return 1 ;;
    *) return 0 ;;
  esac
}

# 0 when the URL answers 2xx (redirects followed), non-zero otherwise.
fetch() {
  curl -fsS -L -m "$TIMEOUT" -o /dev/null "$1" 2>/dev/null
}

if [ -n "$URL_FILE" ] && [ -e "$URL_FILE" ]; then
  if [ ! -f "$URL_FILE" ] || [ ! -r "$URL_FILE" ]; then
    log "HEARTBEAT_URL_UNREADABLE - $URL_FILE is not a readable file (create secrets/trial/heartbeat_url.txt and remove the directory Docker made in its place)"
    exit 2
  fi
  FROM_FILE="$(tr -d ' \t\r\n' < "$URL_FILE")"
  if [ -n "$FROM_FILE" ]; then
    if [ -n "$URL" ]; then
      log "HEARTBEAT_URL_AMBIGUOUS - the ping URL is set both as HEARTBEAT_URL and in $URL_FILE; keep one"
      exit 2
    fi
    URL="$FROM_FILE"
  fi
fi

if [ -z "$URL" ]; then
  log "HEARTBEAT_DISABLED - HEARTBEAT_URL is empty, no outside monitor is pinged (DEPLOY_RUNBOOK §P)"
  if [ -n "$ROUNDS" ]; then exit 0; fi
  while :; do sleep 3600; done
fi

if ! is_uint "$INTERVAL" || ! is_uint "$FAILS_BEFORE_FAIL" || [ "$FAILS_BEFORE_FAIL" -lt 1 ]; then
  log "HEARTBEAT_CONFIG_INVALID - HEARTBEAT_INTERVAL_SECONDS and HEARTBEAT_FAILURES_BEFORE_FAIL must be whole numbers (failures >= 1)"
  exit 2
fi
if [ -z "$ROUNDS" ] && [ "$INTERVAL" -lt 15 ]; then
  log "HEARTBEAT_INTERVAL_SECONDS=$INTERVAL is below 15 - using 15 (the service accepts at most 5 pings a minute)"
  INTERVAL=15
fi

log "started - every ${INTERVAL}s, /fail after ${FAILS_BEFORE_FAIL} failed round(s), checks: $CHECKS"

fails=0
round=0
while :; do
  failed=""
  for check in $CHECKS; do
    if ! fetch "$check"; then
      failed="$check"
      break
    fi
  done

  if [ -z "$failed" ]; then
    if [ "$fails" -gt 0 ]; then log "site answers again after $fails failed round(s)"; fi
    fails=0
    if fetch "$URL"; then :; else log "PING_FAILED - the outside monitor did not accept the ping (network or service down)"; fi
  else
    fails=$((fails + 1))
    log "CHECK_FAILED - $failed did not answer 2xx ($fails in a row)"
    if [ "$fails" -eq "$FAILS_BEFORE_FAIL" ]; then
      if fetch "$URL/fail"; then
        log "fail signal sent"
      else
        log "PING_FAILED - the fail signal was not accepted"
      fi
    fi
  fi

  round=$((round + 1))
  if [ -n "$ROUNDS" ] && [ "$round" -ge "$ROUNDS" ]; then exit 0; fi
  sleep "$INTERVAL"
done
