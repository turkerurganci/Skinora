import { test, expect } from '@playwright/test';
import { seedHappyPath, insertBuyer, closePool, seed } from '../src/db';
import { mintAccessToken } from '../src/jwt';
import { injectLogin, waitForUiStatus } from '../src/browser';
import * as api from '../src/api';

/**
 * T107 happy-path UI smoke (AC3) — drives the escrow flow in a real browser and
 * asserts every state transition is reflected on the transaction detail page's
 * status badge. The transaction is created via API (seller) and accepted via
 * the actual UI form (buyer); the fake sidecar drives the Steam/payment legs at
 * the backend webhook/client seam. Targets the nginx origin (relative
 * /api/v1 + /hubs proxied to the backend) — same as production.
 */
test.afterAll(async () => {
  await closePool();
});

test('UI happy path: badge tracks CREATED → COMPLETED', async ({ page, context }) => {
  // Defer the buyer so the transaction is created with BuyerId=null — that's the
  // prospective-buyer shape the detail service's canAccept gate requires to
  // enable the UI accept form.
  await seedHappyPath({ includeBuyer: false });
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });
  const buyerToken = mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId });

  // Seller creates the transaction (API) — the create wizard is out of scope;
  // AC3 is about the UI reflecting state, exercised from the buyer's detail view.
  const create = await api.createTransaction(sellerToken, {
    itemAssetId: seed.itemAssetId,
    stablecoin: 'USDT',
    price: seed.price,
    paymentTimeoutHours: 1,
    buyerIdentificationMethod: 'STEAM_ID',
    buyerSteamId: seed.buyerSteamId,
    sellerWalletAddress: seed.sellerPayoutAddress,
  });
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const txId = String(api.unwrap(create.body).id);

  // Now seed the buyer User (login + accept need it); the transaction's BuyerId
  // stays null, so the buyer is a prospective buyer with canAccept=true.
  await insertBuyer();

  // Buyer logs in (JWT-inject) and opens the detail page.
  await injectLogin(context, buyerToken);
  await page.goto(`/en/transactions/${txId}`, { waitUntil: 'domcontentloaded' });

  // CREATED — badge + accept form visible.
  await expect(page.getByTestId('tx-status-badge')).toHaveAttribute('data-status', 'CREATED', {
    timeout: 30_000,
  });

  // Accept via the real UI form → ACCEPTED.
  await page.getByTestId('accept-refund-input').fill(seed.buyerRefundAddress);
  await page.getByTestId('accept-submit').click();
  await waitForUiStatus(page, 'ACCEPTED', { timeoutMs: 30_000, intervalMs: 2_000 });

  // Escrow dispatch job + fake trade self-drive → ITEM_ESCROWED.
  await waitForUiStatus(page, 'ITEM_ESCROWED', { timeoutMs: 180_000 });

  // Buyer pays (fake control surface) → PAYMENT_RECEIVED.
  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  await waitForUiStatus(page, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });

  // Delivery dispatch + fake accept → ITEM_DELIVERED.
  await waitForUiStatus(page, 'ITEM_DELIVERED', { timeoutMs: 180_000 });

  // Seller payout pipeline → COMPLETED.
  await waitForUiStatus(page, 'COMPLETED', { timeoutMs: 240_000 });
});
