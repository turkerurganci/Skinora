# skinora-fake-sidecar

E2E-only test double for the **Steam**, **TRON** and **Ethereum** sidecars (T107;
Ethereum since T155). It implements the exact HTTP surface the backend calls
outbound, and drives the escrow happy path forward by emitting **HMAC-signed
inbound webhooks** — so the full `CREATED → COMPLETED` flow runs with **no real
Steam account and no real TRON or Ethereum node**, on either payment network
(11 §F8 K1).

> ⚠️ **Never deployed to production.** It exists solely for `docker-compose.e2e.yml`.

## Why a separate service

Real Steam OAuth and on-chain USDT finality cannot run unattended in CI. The
only clean, deterministic seam is the backend's **webhook ⇄ sidecar-client
boundary** (owner decision, T107 approach **B**). This service stands in for
both real sidecars at that boundary.

One process listens on **three ports** so the backend reaches each surface where
it expects it:

| Port | Surface | Backend config |
|------|---------|----------------|
| 5100 | Steam   | `SteamSidecar:BaseUrl` |
| 5200 | TRON blockchain | `BlockchainSidecar:BaseUrl` |
| 5300 | Ethereum blockchain (T155) | `EthereumSidecar:BaseUrl` |

In `docker-compose.e2e.yml` the container is aliased to
`skinora-steam-sidecar`, `skinora-blockchain-sidecar` and
`skinora-ethereum-sidecar`.

The TRON and Ethereum surfaces serve the **same paths** (05 §3.3a — one
contract, one service per network), so each port has its own router bound to
one network profile ([`src/networks.ts`](src/networks.ts)): address and hash
formats, HD path, token contracts, webhook secret, and how a final transfer is
reported. A call that reaches the wrong network's surface is refused the way the
real service refuses it (below).

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

**Blockchain — TRON (`:5200`) and Ethereum (`:5300`), same paths**

| Endpoint | TRON | Ethereum |
|---|---|---|
| `POST /api/wallet/derive` | base58 `T…`, `m/44'/195'/0'/0/{i}` | EIP-55 `0x…`, `m/44'/60'/0'/0/{i}` |
| `POST /api/wallet/balances` | empty token map | empty token map |
| `POST /api/monitor/start` · `/stop` | registry, readable via `/__e2e/blockchain/monitors` | same |
| `POST /api/monitor/post-cancel-start` · `/post-cancel-stop` | acknowledged | acknowledged |
| `POST /api/transfer/{payout,refund,sweep,cold-wallet}` | 64-hex `txHash` | `0x` + 64-hex `txHash` |
| `GET /api/transfer/status/:txHash` | `confirmations: 25`, `SUCCESS` | `finalized: true`, `confirmations: null`, `realizedFeeWei` (K5, T154) |
| `POST /api/transfer/estimate-fee` | `feeUsdt` 2.00 + Energy breakdown | T154 shape: `feeWei` / `ethPriceUsdt` as strings, `feeUsdt` = their rounded-up product (0.05) |
| `POST /api/transfer/estimate-sweep-fee` | 8,441,349 SUN at 0.34 → the backend's 2.90 line (T149) | 501 `NOT_IMPLEMENTED` (no buyer-paid line on Ethereum) |

Immediate finality on both: the confirmation job settles a transfer on its
first poll, in that network's own terms ⇒ `COMPLETED`.

What each blockchain surface **refuses** (400), like the real services:

- a POST whose `network` names another network — or, on Ethereum, none
  (`NETWORK_MISMATCH`; TRON lets an absent field through — T148 iv / T150 iv);
- an address in another network's format (`INVALID_ADDRESS`) — a TRON refund
  address sent through the Ethereum service never confirms;
- `monitor/start` with another network's (or another symbol's) token contract
  (`UNEXPECTED_CONTRACT`);
- a `transfer/status` hash in another network's format (`INVALID_TX_HASH`).

Every call is journalled with its network and the status it was answered with
(`GET /__e2e/blockchain/calls`).

**Health**: `GET /health` on every port. The TRON and Ethereum surfaces name
their `network`; Ethereum also declares `acceptsPayments: true` (the T151 gate:
without it the backend keeps Ethereum out of `supportedNetworks`).

## E2E control surface (test → fake, no auth)

Simulates what the platform cannot observe by itself.

### Payment (the on-chain transfers the real monitor would see)

Resolves the transaction's deposit `PaymentAddress` from SQL Server so the
inbound webhook carries a valid `paymentAddressId` + the **exact**
`ExpectedAmount` — and, since T155, the deposit's **network**: the webhook is
the one that network's service would send (its hash format, payer wallet, token
contracts), signed with that network's secret. The same call drives a TRON and
an Ethereum transaction.

- `POST /__e2e/payment/pay`     `{ transactionId, amount? }` — detect **then**
  confirm (exact amount) ⇒ `PAYMENT_RECEIVED`. Happy-path entry point.
- `POST /__e2e/payment/detect`  `{ transactionId, amount? }` — DETECTED row only
- `POST /__e2e/payment/confirm` `{ transactionId }` — CONFIRMED + amount validation
- `POST /__e2e/payment/{wrong-token,spam-token,late-detected}` — the 03 §5.3 /
  §5.3a / §5.4 levers, on the deposit's network

Two negative levers on every payment endpoint send what the backend must refuse
(409 `WEBHOOK_NETWORK_MISMATCH`, T148): `claimNetwork` (the envelope's
`network`) and `signAs` (whose secret signs). A refused webhook answers 502 with
the backend's `backendStatus` + `backendBody`.

### Blockchain read-back + fee knobs (T155)

- `GET  /__e2e/blockchain/calls?network=&path=&sinceSeq=` — every backend →
  blockchain-service call, oldest first (`path` is a prefix)
- `GET  /__e2e/blockchain/monitors?network=` — deposits currently watched
- `POST /__e2e/blockchain/fees` `{ network, available?, tronFeeUsdt?,
  tronSweepCostSun?, tronTrxPriceUsdt?, ethFeeWei?, ethPriceUsdt? }` — drive one
  network's estimate answers; `available: false` ⇒ 502 and the backend takes its
  fallback. `GET /__e2e/blockchain/fees/:network` reads them back.
- `POST /__e2e/blockchain/reset` — journal, monitors and fee knobs to defaults

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
Only blockchain webhooks are signed: `BLOCKCHAIN_WEBHOOK_SECRET` must match the
backend's `Webhook__BlockchainSharedSecret` (TRON) and `ETHEREUM_WEBHOOK_SECRET`
its `Webhook__EthereumSharedSecret` (T155). The two must differ — the backend
refuses to start otherwise, and the secret that verifies is what tells it which
network is speaking (T148). There is no Steam counterpart —
T132 removed the inbound Steam webhook route together with
`Webhook__SteamSharedSecret`, and T133 removed the publishing half.

## Configuration

See [`src/config.ts`](src/config.ts). Key env vars: `STEAM_PORT` (5100),
`BLOCKCHAIN_PORT` (5200), `ETHEREUM_PORT` (5300), `BACKEND_URL`,
`BLOCKCHAIN_WEBHOOK_SECRET`, `ETHEREUM_WEBHOOK_SECRET`,
`ETHEREUM_USDT_CONTRACT` / `ETHEREUM_USDC_CONTRACT` (empty = mainnet, the
backend's default), `INTERNAL_KEY` (optional), `DB_*` (control-endpoint
lookups), `FAKE_TRANSFER_CONFIRMATIONS`.

`FAKE_BOT_STEAM_ID` and `FAKE_TRADE_ACCEPT_DELAY_MS` were removed in T137 with
the custody trade surface — there is no bot identity and no self-accept delay
left anywhere in the e2e stack. `STEAM_WEBHOOK_SECRET` went in T133 for the
same reason: nothing signs a Steam webhook and nothing verifies one.

## Develop

```bash
npm install
npm run build      # tsc
npm run lint       # eslint
npm run format:check
npm test           # vitest (hmac, ids + EIP-55, keccak, fee math, inventory
                   # store, both blockchain surfaces over HTTP, contract examples)
```

`src/contracts.test.ts` holds the fake against the real services' example
bodies — `sidecar-blockchain/contracts/` and `sidecar-ethereum/contracts/`, the
same files the backend's contract tests replay. An Ethereum block runs once its
directory exists (T151 adds `webhook/`, T154 `estimate-fee/` and
`transfer-status/`); a new webhook example without a builder fails the test.

## Scope

This package is **T107 PR-1** (infrastructure). The Playwright workspace, JWT
inject-login, SQL seed, and the happy-path spec land in PR-2 / PR-3; they drive
this service through `docker-compose.e2e.yml`.
