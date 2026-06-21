# skinora-fake-sidecar

E2E-only test double for the **Steam** and **blockchain** sidecars (T107). It
implements the exact HTTP surface the backend calls outbound, and drives the
escrow happy path forward by emitting **HMAC-signed inbound webhooks** — so the
full `CREATED → COMPLETED` flow runs with **no real Steam account and no real
Tron node**.

> ⚠️ **Never deployed to production.** It exists solely for `docker-compose.e2e.yml`.

## Why a separate service

Real Steam OAuth and on-chain USDT finality cannot run unattended in CI. The
only clean, deterministic seam is the backend's **webhook ⇄ sidecar-client
boundary** (owner decision, T107 approach **B**). This service stands in for
both real sidecars at that boundary.

One process listens on **two ports** so the backend reaches each surface where
it expects it:

| Port | Surface | Backend config |
|------|---------|----------------|
| 5100 | Steam   | `SteamSidecar:BaseUrl` |
| 5200 | Blockchain | `BlockchainSidecar:BaseUrl` |

In `docker-compose.e2e.yml` the container is aliased to both
`skinora-steam-sidecar` and `skinora-blockchain-sidecar` hostnames.

## Implemented surface (backend → fake, `X-Internal-Key` when set)

**Steam (`:5100`)**
- `GET  /api/inventory/:steamId` — deterministic tradable inventory
- `DELETE /api/inventory/:steamId/cache`
- `GET  /api/trade-hold/:steamId` — no hold (MA verified)
- `POST /api/trade-offers/send` — returns `sent`, then self-emits
  `trade_offer.accepted` after `FAKE_TRADE_ACCEPT_DELAY_MS` (escrow leg ⇒
  `ITEM_ESCROWED`, delivery leg ⇒ `ITEM_DELIVERED`)

**Blockchain (`:5200`)**
- `POST /api/wallet/derive` — deterministic fake Tron deposit address
- `POST /api/wallet/balances`
- `POST /api/monitor/post-cancel-start` · `/post-cancel-stop`
- `POST /api/transfer/{payout,refund,sweep,cold-wallet}` — returns a fake `txHash`
- `GET  /api/transfer/status/:txHash` — immediate finality (`confirmations ≥ 20`,
  `SUCCESS`) so the seller payout confirms on the first poll ⇒ `COMPLETED`

**Health**: `GET /health` (both ports).

## E2E control surface (test → fake, no auth)

Simulates the on-chain buyer payment the real monitor would observe. Resolves
the transaction's deposit `PaymentAddress` from SQL Server so the inbound
webhook carries a valid `paymentAddressId` + the **exact** `ExpectedAmount`.

- `POST /__e2e/payment/pay`     `{ transactionId, amount? }` — detect **then**
  confirm (exact amount) ⇒ `PAYMENT_RECEIVED`. Happy-path entry point.
- `POST /__e2e/payment/detect`  `{ transactionId, amount? }` — DETECTED row only
- `POST /__e2e/payment/confirm` `{ transactionId }` — CONFIRMED + amount validation

## Webhook signing

HMAC-SHA256 over `timestamp + nonce + body` with the per-sidecar shared secret,
sent as `X-Signature` / `X-Timestamp` / `X-Nonce` — identical to the real
sidecars and verified by the backend's `WebhookSignatureMiddleware` (05 §3.4).
Steam webhooks use `STEAM_WEBHOOK_SECRET`, blockchain webhooks
`BLOCKCHAIN_WEBHOOK_SECRET`; they must match the backend's
`Webhook__SteamSharedSecret` / `Webhook__BlockchainSharedSecret`.

## Configuration

See [`src/config.ts`](src/config.ts). Key env vars: `STEAM_PORT` (5100),
`BLOCKCHAIN_PORT` (5200), `BACKEND_URL`, `STEAM_WEBHOOK_SECRET`,
`BLOCKCHAIN_WEBHOOK_SECRET`, `INTERNAL_KEY` (optional), `DB_*` (control-endpoint
lookups), `FAKE_BOT_STEAM_ID`, `FAKE_TRADE_ACCEPT_DELAY_MS`.

## Develop

```bash
npm install
npm run build      # tsc
npm run lint       # eslint
npm run format:check
npm test           # vitest (hmac signing + deterministic id generators)
```

## Scope

This package is **T107 PR-1** (infrastructure). The Playwright workspace, JWT
inject-login, SQL seed, and the happy-path spec land in PR-2 / PR-3; they drive
this service through `docker-compose.e2e.yml`.
