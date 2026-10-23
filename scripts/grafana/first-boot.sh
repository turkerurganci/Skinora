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
# The guard reads ONE log line, so the guarded run pins its own logging
# (T165 kapanış, yeniden doğrulama Y1): level info, console output, text
# format, no logger filter muting provisioning.alerting — whatever GF_LOG_*
# the operator set (warn, file, json …), which still applies to the real start.
# Without the pin those settings left Grafana never starting (measured: warn →
# "not provisioned within …" loop). The line itself was measured on
# grafana/grafana:11.3.0; re-measure it before upgrading the image (the compose
# test pins the version for that reason). A Grafana that does not start is
# reported outside by the heartbeat (it checks Grafana's /api/health).
#
# Tested by first-boot.test.mjs (fake /run.sh) and measured live with an empty
# volume (T165_REPORT "Düzeltme turu" and "Kapanış").
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
    GF_LOG_LEVEL=info GF_LOG_MODE=console GF_LOG_CONSOLE_LEVEL=info \
    GF_LOG_CONSOLE_FORMAT=text GF_LOG_FILTERS=provisioning.alerting:info \
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
