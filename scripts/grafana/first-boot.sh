#!/bin/sh
# T165 — Grafana entrypoint with a first-boot guard (DEPLOY_RUNBOOK §P.4).
#
# Why: on an EMPTY database Grafana 11.3 starts its Alertmanager with the
# built-in default configuration — one route to the receiver
# "grafana-default-email", address <example@email.com> — BEFORE the alerting
# provisioning (contact points, policies, rules) runs. Provisioning writes our
# routing to the database, but the running Alertmanager only reloads it on its
# next poll (unified_alerting.alertmanager_config_poll_interval, 60 s). The
# rules are already being evaluated in that window, so an alert that fires
# then is e-mailed — with its content — to example@email.com through the real
# SMTP relay (measured: T165 doğrulaması, bulgu 2; T165 düzeltme turu). The
# default configuration is not configurable in 11.3 (no ini key).
#
# What: when the data volume has not been provisioned yet (no marker file),
# Grafana is started once with rule execution OFF and bound to loopback, only
# until the log says "finished to provision alerting" — the routing is then in
# the database — and stopped. The real start that follows loads that routing
# into the Alertmanager at init, before the first rule is evaluated, so no
# alert can ever reach the default receiver. Later boots skip the guard: the
# database already holds our routing (a changed provisioning file is picked up
# within one poll, and until then the previous provisioned routing — ours —
# is used).
#
# Tested by first-boot.test.mjs (fake /run.sh) and measured live with an empty
# volume (T165_REPORT "Düzeltme turu").
set -eu

RUN="${GRAFANA_RUN:-/run.sh}"
DATA="${GF_PATHS_DATA:-/var/lib/grafana}"
TIMEOUT="${GRAFANA_FIRST_BOOT_TIMEOUT_SECONDS:-300}"
MARKER="$DATA/.skinora-alerting-provisioned"
LOG="${TMPDIR:-/tmp}/skinora-grafana-first-boot.log"

if [ ! -f "$MARKER" ]; then
  echo "skinora: first boot — provisioning alerting with rule execution off"
  : >"$LOG"
  GF_UNIFIED_ALERTING_EXECUTE_ALERTS=false GF_SERVER_HTTP_ADDR=127.0.0.1 \
    "$RUN" "$@" >>"$LOG" 2>&1 &
  pid=$!
  waited=0
  until grep -q 'msg="finished to provision alerting"' "$LOG"; do
    if ! kill -0 "$pid" 2>/dev/null; then
      cat "$LOG"
      echo "skinora: Grafana stopped before alerting was provisioned — not starting" >&2
      exit 1
    fi
    if [ "$waited" -ge "$TIMEOUT" ]; then
      kill -TERM "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
      cat "$LOG"
      echo "skinora: alerting not provisioned within ${TIMEOUT}s — not starting" >&2
      exit 1
    fi
    sleep 1
    waited=$((waited + 1))
  done
  kill -TERM "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  cat "$LOG"
  touch "$MARKER"
  echo "skinora: alerting provisioned — starting Grafana"
fi

exec "$RUN" "$@"
