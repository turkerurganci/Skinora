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

# T171b (DEFERRED_BACKLOG ThirdPartyCredentialsInPlainEnv, ⚠ GÜVENLİK) — the
# admin password is the Docker secret grafana_admin_password, read by Grafana
# itself through GF_SECURITY_ADMIN_PASSWORD__FILE. Grafana does not stop when
# that file is missing (Docker's directory) or empty: it would come up with
# its built-in admin/admin on an empty data volume — and the admin password is
# only applied on that first start. So: no variable, a readable, non-empty
# file, or no start. The value is never printed.
#
# A CR is refused too (CREDENTIAL_INVALID): the image's /run.sh reads the file
# with $(< file), which drops trailing LFs but KEEPS a CR — measured on
# grafana/grafana:11.3.0: a CRLF file (what Git Bash's openssl writes on
# Windows) made "<password>\r" the admin password and the visible one was
# refused (401). The same holds for the SMTP password (below).
has_cr() { [ "$(tr -cd '\r' <"$1" | wc -c | tr -d ' ')" -gt 0 ]; }

if [ -n "${GF_SECURITY_ADMIN_PASSWORD:-}" ]; then
  echo "CREDENTIAL_IN_ENV: GF_SECURITY_ADMIN_PASSWORD is set as an environment variable - the Grafana admin password is read only from its Docker secret file (GF_SECURITY_ADMIN_PASSWORD__FILE, DEPLOY_RUNBOOK L.9)" >&2
  exit 1
fi
admin_password_file="${GF_SECURITY_ADMIN_PASSWORD__FILE:-}"
if [ -z "$admin_password_file" ]; then
  echo "CREDENTIAL_MISSING: GF_SECURITY_ADMIN_PASSWORD__FILE is not set - mount the grafana_admin_password secret (secrets/README.md)" >&2
  exit 1
fi
if [ -e "$admin_password_file" ] && { [ ! -f "$admin_password_file" ] || [ ! -r "$admin_password_file" ]; }; then
  echo "CREDENTIAL_UNREADABLE: $admin_password_file is not a readable file - create the grafana_admin_password secret file (secrets/README.md) and remove the directory Docker made in its place" >&2
  exit 1
fi
if [ -f "$admin_password_file" ] && ! cat "$admin_password_file" >/dev/null 2>&1; then
  # Docker Desktop, missing source file: the mount point can pass -f yet every
  # read fails with an I/O error (measured, T171b).
  echo "CREDENTIAL_UNREADABLE: $admin_password_file cannot be read - create the grafana_admin_password secret file (secrets/README.md) and remove what Docker made in its place" >&2
  exit 1
fi
if [ ! -f "$admin_password_file" ] || ! grep -q '[^[:space:]]' "$admin_password_file"; then
  echo "CREDENTIAL_MISSING: $admin_password_file is missing or empty - Grafana does not start without its admin password (an empty one would leave admin/admin)" >&2
  exit 1
fi
if has_cr "$admin_password_file"; then
  echo "CREDENTIAL_INVALID: $admin_password_file has a Windows line ending (CR) - Grafana would keep it as part of the password; rewrite it with LF only (secrets/README.md)" >&2
  exit 1
fi

# T171a (doğrulama S4-2, ⚠ GÜVENLİK — alarm yolu) — with SMTP on, the
# password file must be a readable file. Grafana's own <option>__FILE reading
# does not stop on a failed read: when the source secrets/…/
# grafana_smtp_password.txt is missing, Docker creates a DIRECTORY at the mount
# point and Grafana started with an empty SMTP password (measured), so every
# e-mail alert failed only at send time. Same fail-closed rule as every other
# shared secret (SHARED_SECRET_UNREADABLE, TELEGRAM_BOT_TOKEN_UNREADABLE): do
# not start. Anything but an explicit "off" value counts as on. An EMPTY file
# is still allowed (= e-mail not configured yet, DEPLOY_RUNBOOK §L.9 / §P).
# T171b — and a CR in it is refused (CREDENTIAL_INVALID), see above: Resend
# would reject "<key>\r" only at send time.
smtp_enabled=$(printf '%s' "${GF_SMTP_ENABLED:-false}" | tr 'A-Z' 'a-z')
case "$smtp_enabled" in
  ""|0|f|false|n|no|off) ;;
  *)
    smtp_password_file="${GF_SMTP_PASSWORD__FILE:-}"
    if [ -n "$smtp_password_file" ] && { [ ! -f "$smtp_password_file" ] || [ ! -r "$smtp_password_file" ]; }; then
      echo "GRAFANA_SMTP_PASSWORD_UNREADABLE: $smtp_password_file is not a readable file while SMTP is on - create the grafana_smtp_password secret file (secrets/README.md) and remove the directory Docker made in its place" >&2
      exit 1
    fi
    if [ -n "$smtp_password_file" ] && has_cr "$smtp_password_file"; then
      echo "CREDENTIAL_INVALID: $smtp_password_file has a Windows line ending (CR) - Grafana would send it as part of the SMTP password; rewrite it with LF only (secrets/README.md)" >&2
      exit 1
    fi
    ;;
esac

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
