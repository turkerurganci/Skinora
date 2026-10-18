#!/usr/bin/env bash
# Skinora — Ethereum ETH/USDT price source reachability probe (T158 (e)).
#
# The Ethereum service prices every capped send with ETH/USDT from two keyless
# sources (08 §3.7.7 md. 5, 11 §F8 T154 (i)/(ii)):
#   Binance   GET https://api.binance.com/api/v3/avgPrice?symbol=ETHUSDT
#   CoinGecko GET https://api.coingecko.com/api/v3/simple/price?ids=ethereum&vs_currencies=usd&include_last_updated_at=true
# A freshly started service needs BOTH to agree within 2 % before it accepts a
# price (yönetici kararı, T154 doğrulaması). Binance refuses some regions and
# cloud providers with HTTP 451 ("Unavailable For Legal Reasons"); on such a
# host the service never accepts a price: Ethereum refunds charge the static
# fallback fee and every capped send (sweep, payout, refund, cold transfer)
# waits with ETH_PRICE_UNAVAILABLE — permanently, it does not heal by itself.
#
# Run it FROM THE PRODUCTION HOST (or the exact egress the Ethereum container
# uses) before the Ethereum network is opened (DEPLOY_RUNBOOK §J). Read-only:
# two GET requests per source, no key, nothing signed or sent to a chain.
#
# Usage:  bash scripts/probe-eth-price-sources.sh [--json]
# Exit:   0 both sources usable · 1 at least one unusable (see the verdict)
#         2 curl missing
#
# The checks mirror what EthPriceService accepts: HTTP 200, a positive decimal
# price inside 1–1,000,000 USDT, the source's own timestamp at most 10 minutes
# old, and the two prices within 2 % of each other.

set -u

JSON=0
[ "${1:-}" = "--json" ] && JSON=1

if ! command -v curl >/dev/null 2>&1; then
  echo "curl is required" >&2
  exit 2
fi

# The two URLs the service calls (sidecar-ethereum/src/price/EthPriceService.ts).
# PROBE_BINANCE_URL / PROBE_COINGECKO_URL override them only to rehearse the
# failure verdicts (e.g. https://httpbin.org/status/451) — never in production.
BINANCE_URL="${PROBE_BINANCE_URL:-https://api.binance.com/api/v3/avgPrice?symbol=ETHUSDT}"
COINGECKO_URL="${PROBE_COINGECKO_URL:-https://api.coingecko.com/api/v3/simple/price?ids=ethereum&vs_currencies=usd&include_last_updated_at=true}"
TIMEOUT=10
MAX_AGE_SECONDS=600
NOW=$(date -u +%s)

# probe NAME URL → sets ${NAME}_CODE, ${NAME}_BODY, ${NAME}_TIME, ${NAME}_ERR
probe() {
  local name="$1" url="$2" out code time err
  out=$(mktemp)
  err=$(curl -sS -o "$out" -w '%{http_code} %{time_total}' --max-time "$TIMEOUT" \
    -H 'Accept: application/json' "$url" 2>&1)
  local rc=$?
  if [ $rc -ne 0 ]; then
    code="000"; time="-"
  else
    code=${err%% *}; time=${err##* }; err=""
  fi
  printf -v "${name}_CODE" '%s' "$code"
  printf -v "${name}_TIME" '%s' "$time"
  printf -v "${name}_ERR" '%s' "$err"
  printf -v "${name}_BODY" '%s' "$(head -c 400 "$out" | tr -d '\r\n')"
  rm -f "$out"
}

probe BINANCE "$BINANCE_URL"
probe COINGECKO "$COINGECKO_URL"

# --- parse (no jq dependency: the two answers are flat and small) ----------
BINANCE_PRICE=$(printf '%s' "$BINANCE_BODY" | sed -n 's/.*"price":"\([0-9.]*\)".*/\1/p')
BINANCE_CLOSE_MS=$(printf '%s' "$BINANCE_BODY" | sed -n 's/.*"closeTime":\([0-9]*\).*/\1/p')
COINGECKO_PRICE=$(printf '%s' "$COINGECKO_BODY" | sed -n 's/.*"usd":\([0-9.]*\).*/\1/p')
COINGECKO_UPDATED=$(printf '%s' "$COINGECKO_BODY" | sed -n 's/.*"last_updated_at":\([0-9]*\).*/\1/p')

# verdict NAME CODE PRICE AGE_SECONDS → prints the reason, returns 0 when usable
verdict() {
  local code="$2" price="$3" age="$4"
  case "$code" in
    200) ;;
    451) echo "HTTP 451 — the source refuses this host's region (Binance geo-block). Permanent: decide a different egress or source before opening Ethereum."; return 1 ;;
    403) echo "HTTP 403 — refused (WAF / cloud-provider block). Permanent until the egress changes."; return 1 ;;
    429) echo "HTTP 429 — rate limited. Retry later; a steady 429 from this host is a capacity problem."; return 1 ;;
    000) echo "no HTTP answer (DNS, TLS, firewall or timeout ${TIMEOUT}s)."; return 1 ;;
    *) echo "HTTP $code — not a usable answer."; return 1 ;;
  esac
  if [ -z "$price" ]; then echo "HTTP 200 but no price in the body (the service would refuse it)."; return 1; fi
  if ! awk -v p="$price" 'BEGIN { exit !(p >= 1 && p <= 1000000) }'; then
    echo "price $price outside the 1–1,000,000 USDT band."; return 1
  fi
  if [ -z "$age" ] || [ "$age" -gt "$MAX_AGE_SECONDS" ] || [ "$age" -lt -60 ]; then
    echo "price $price but its timestamp is ${age:-missing}s old (limit ${MAX_AGE_SECONDS}s)."; return 1
  fi
  echo "usable — $price USDT, ${age}s old."
  return 0
}

BINANCE_AGE=""
[ -n "$BINANCE_CLOSE_MS" ] && BINANCE_AGE=$(( NOW - BINANCE_CLOSE_MS / 1000 ))
COINGECKO_AGE=""
[ -n "$COINGECKO_UPDATED" ] && COINGECKO_AGE=$(( NOW - COINGECKO_UPDATED ))

BINANCE_VERDICT=$(verdict BINANCE "$BINANCE_CODE" "$BINANCE_PRICE" "$BINANCE_AGE"); BINANCE_OK=$?
COINGECKO_VERDICT=$(verdict COINGECKO "$COINGECKO_CODE" "$COINGECKO_PRICE" "$COINGECKO_AGE"); COINGECKO_OK=$?

AGREE="n/a"
if [ $BINANCE_OK -eq 0 ] && [ $COINGECKO_OK -eq 0 ]; then
  if awk -v a="$BINANCE_PRICE" -v b="$COINGECKO_PRICE" 'BEGIN { d = (a > b ? a - b : b - a) / a; exit !(d <= 0.02) }'; then
    AGREE="yes"
  else
    AGREE="no"
  fi
fi

if [ $BINANCE_OK -eq 0 ] && [ $COINGECKO_OK -eq 0 ] && [ "$AGREE" = "yes" ]; then
  OVERALL="OK — both sources usable and within 2 %: a fresh Ethereum service can accept a price."
  RC=0
elif [ $BINANCE_OK -ne 0 ] && [ $COINGECKO_OK -ne 0 ]; then
  OVERALL="FAIL — neither source is usable from this host: the Ethereum service will never price a send (ETH_PRICE_UNAVAILABLE)."
  RC=1
elif [ "$AGREE" = "no" ]; then
  OVERALL="FAIL — both answer but disagree by more than 2 %: the service trusts neither right now (re-run; a lasting gap is a source problem)."
  RC=1
else
  OVERALL="FAIL — only one source is usable: a FRESHLY STARTED service cannot accept a price (two sources required at start, T154 doğrulaması). Do not open Ethereum from this host until both answer."
  RC=1
fi

if [ $JSON -eq 1 ]; then
  printf '{"probedAtUtc":"%s","binance":{"http":"%s","seconds":"%s","price":"%s","ageSeconds":"%s","usable":%s},"coingecko":{"http":"%s","seconds":"%s","price":"%s","ageSeconds":"%s","usable":%s},"agreeWithin2pct":"%s","ok":%s}\n' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    "$BINANCE_CODE" "$BINANCE_TIME" "$BINANCE_PRICE" "$BINANCE_AGE" "$([ $BINANCE_OK -eq 0 ] && echo true || echo false)" \
    "$COINGECKO_CODE" "$COINGECKO_TIME" "$COINGECKO_PRICE" "$COINGECKO_AGE" "$([ $COINGECKO_OK -eq 0 ] && echo true || echo false)" \
    "$AGREE" "$([ $RC -eq 0 ] && echo true || echo false)"
else
  echo "Ethereum price source probe — $(date -u +%Y-%m-%dT%H:%M:%SZ) — host $(hostname 2>/dev/null || echo '?')"
  echo "  Binance   HTTP $BINANCE_CODE (${BINANCE_TIME}s): $BINANCE_VERDICT"
  [ -n "$BINANCE_ERR" ] && echo "            curl: $BINANCE_ERR"
  echo "  CoinGecko HTTP $COINGECKO_CODE (${COINGECKO_TIME}s): $COINGECKO_VERDICT"
  [ -n "$COINGECKO_ERR" ] && echo "            curl: $COINGECKO_ERR"
  echo "  Agree within 2 %: $AGREE"
  echo "  => $OVERALL"
fi
exit $RC
