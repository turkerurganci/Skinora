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
- `GET  /api/inventory/:steamId` — the steamId's **driven** inventory (T137).
  Answers `200` / `422 INVENTORY_PRIVATE` / `503 STEAM_UNAVAILABLE` with the
  08 §2.3 `visibility` in the body, exactly like the real sidecar. An **undriven**
  steamId reads as `PUBLIC` and **empty**.
- `DELETE /api/inventory/:steamId/cache` — `204` (the fake serves no cache)
- `GET  /api/trade-hold/:steamId` — MA-verified / no hold by default, drivable
  per steamId

> `POST /api/trade-offers/send` was **retired in T137**. The platform sends no
> trade offers (02 §2.1); the backend has neither a client that calls it nor a
> `/api/v1/webhooks/steam/trade-events` endpoint to receive its self-drive (both
> went with T117/T118). The seller→buyer trade is simulated by moving inventory
> instead — see `POST /__e2e/steam/trade` below.

**Blockchain (`:5200`)**
- `POST /api/wallet/derive` — deterministic fake Tron deposit address
- `POST /api/wallet/balances`
- `POST /api/monitor/post-cancel-start` · `/post-cancel-stop`
- `POST /api/transfer/{payout,refund,sweep,cold-wallet}` — returns a fake `txHash`
- `GET  /api/transfer/status/:txHash` — immediate finality (`confirmations ≥ 20`,
  `SUCCESS`) so the seller payout confirms on the first poll ⇒ `COMPLETED`

**Health**: `GET /health` (both ports).

## E2E control surface (test → fake, no auth)

Simulates what the platform cannot observe by itself.

### Payment (the on-chain transfers the real monitor would see)

Resolves the transaction's deposit `PaymentAddress` from SQL Server so the
inbound webhook carries a valid `paymentAddressId` + the **exact**
`ExpectedAmount`.

- `POST /__e2e/payment/pay`     `{ transactionId, amount? }` — detect **then**
  confirm (exact amount) ⇒ `PAYMENT_RECEIVED`. Happy-path entry point.
- `POST /__e2e/payment/detect`  `{ transactionId, amount? }` — DETECTED row only
- `POST /__e2e/payment/confirm` `{ transactionId }` — CONFIRMED + amount validation

### Steam inventory (T137 — the P2P trade the platform is not a party to)

P2P delivery is **inferred** from two inventory reads (02 §9.2): the asset left
the seller, and a copy of its class appeared at the buyer. A steamId-blind
inventory can express neither, so every steamId now has its own holdings.

- `POST /__e2e/steam/inventory` `{ steamId, items?, visibility? }` — seed one
  steamId. `items` **replaces** the whole inventory; each entry is a `catalog`
  template name (`AK47_REDLINE` / `AWP_ASIIMOV`), explicit fields, or a template
  with overrides (`{ catalog: 'AK47_REDLINE', assetId: '777' }` = a second copy
  of the same **class**, which is what a 06 §3.5 count delta is made of).
  `visibility` is `PUBLIC` / `PRIVATE` / `UNAVAILABLE`. Omit either to leave that
  half untouched.
- `GET  /__e2e/steam/inventory/:steamId` — read the **stored** state back
  (always `200`; it reports the store, it does not simulate a Steam read)
- `POST /__e2e/steam/trade` `{ fromSteamId, toSteamId, assetId }` — move one
  asset between inventories. The asset id **rotates** on arrival (06 §8.4) and is
  returned as `newAssetId`; class + instance are preserved. Call it in the other
  direction to simulate the seller pulling the trade back (T129 reversal).
- `POST /__e2e/steam/trade-hold` `{ steamId, active?, escrowEndDurationSeconds? }`
  — drive the 08 §2.2 MA probe. `active: false` ⇒ the accept endpoint answers
  `403 MOBILE_AUTHENTICATOR_REQUIRED` (T119a).
- `POST /__e2e/steam/reset` — drop every driven inventory + trade hold

Trade **lock / cooldown is deliberately not modelled**: no consumer reads lock
state (the delivery service documents why it must not), so simulating it would
only invite a test to assert on a field production ignores.

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
lookups), `FAKE_TRANSFER_CONFIRMATIONS`.

`FAKE_BOT_STEAM_ID` and `FAKE_TRADE_ACCEPT_DELAY_MS` were removed in T137 with
the custody trade surface — there is no bot identity and no self-accept delay
left anywhere in the e2e stack.

## Develop

```bash
npm install
npm run build      # tsc
npm run lint       # eslint
npm run format:check
npm test           # vitest (hmac signing, id generators, inventory store)
```

## Scope

This package is **T107 PR-1** (infrastructure). The Playwright workspace, JWT
inject-login, SQL seed, and the happy-path spec land in PR-2 / PR-3; they drive
this service through `docker-compose.e2e.yml`.
