import { test, expect } from '@playwright/test';
import { seedHappyPath, getNotificationTypes, closePool, seed } from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T107 happy-path smoke — drives the full escrow flow at the API level against
 * the docker-compose.e2e.yml stack. The fake sidecar simulates Steam trade
 * acceptance + on-chain payment/finality at the backend webhook/client seam.
 * The browser/UI assertions (data-testid) are PR-3.
 */
test.afterAll(async () => {
  await closePool();
});

test('happy path: CREATED → COMPLETED with WP19 notifications', async () => {
  await seedHappyPath();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });
  const buyerToken = mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId });

  // 1. Seller creates the transaction (status CREATED, not FLAGGED).
  const create = await api.createTransaction(sellerToken, {
    itemAssetId: seed.itemAssetId,
    stablecoin: 'USDT',
    price: seed.price,
    // 1h = 60 min, within the e2e PAYMENT_TIMEOUT_MIN/MAX (15/60). The smoke
    // pays immediately, so the exact deadline is irrelevant.
    paymentTimeoutHours: 1,
    buyerIdentificationMethod: 'STEAM_ID',
    buyerSteamId: seed.buyerSteamId,
    sellerWalletAddress: seed.sellerPayoutAddress,
  });
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const created = api.unwrap(create.body);
  expect(created.status).toBe('CREATED');
  const txId = String(created.id);

  // 2. Buyer accepts → ACCEPTED.
  const accept = await api.acceptTransaction(buyerToken, txId, seed.buyerRefundAddress);
  expect(accept.ok, `accept failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  expect(api.unwrap(accept.body).status).toBe('ACCEPTED');

  // 3. Escrow dispatch job + fake trade_offer.sent/accepted → ITEM_ESCROWED.
  await api.pollStatus(buyerToken, txId, 'ITEM_ESCROWED', { timeoutMs: 180_000 });

  // 4. Buyer pays (fake control endpoint) → PAYMENT_RECEIVED.
  const pay = await api.payViaFake(txId);
  expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, txId, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });

  // 5. Delivery dispatch job + fake accept → ITEM_DELIVERED.
  await api.pollStatus(buyerToken, txId, 'ITEM_DELIVERED', { timeoutMs: 180_000 });

  // 6. Seller payout pipeline (queue → dispatch → confirm) → COMPLETED.
  await api.pollStatus(buyerToken, txId, 'COMPLETED', { timeoutMs: 240_000 });

  // 7. WP19 notifications produced for the parties.
  const notifTypes = await getNotificationTypes();
  expect(notifTypes, `notifications: ${JSON.stringify(notifTypes)}`).toContain(
    'TRANSACTION_COMPLETED',
  );
});
