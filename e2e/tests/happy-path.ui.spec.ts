import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  setPayoutEligibleNow,
  pollSettlementVerified,
  closePool,
  seed,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import { injectLogin, waitForUiStatus } from '../src/browser';
import * as api from '../src/api';

/**
 * T107 happy-path UI smoke (AC3), rewritten for P2P (T138) — drives the escrow
 * flow in a real browser and asserts every state transition reaches the
 * transaction detail page's status badge. Targets the nginx origin (relative
 * /api/v1 + /hubs proxied to the backend), same as production.
 *
 * Why this suite got BIGGER in the rewrite. In the custody model the browser had
 * exactly one thing to do — accept — and the platform's bots drove the rest, so
 * a single buyer page could watch the whole chain go by. P2P moved two decisions
 * onto real people, one per side: the seller's `confirm-ready` and the buyer's
 * `confirm-receipt`. Both are UI surfaces T135 built and nothing else in the
 * eight-leg matrix exercises, so this spec now drives TWO browser contexts and
 * presses both buttons. Watching them from one side would leave the halves of
 * the 04 §7.3 state × role matrix that actually move the flow unmeasured.
 *
 * The two steps a browser cannot perform stay at the fake's control surface, for
 * the same reason they do in the API smoke: the on-chain payment and the
 * seller → buyer Steam trade happen outside the platform entirely (02 §2.1).
 * The settlement clock is brought forward with the DEPLOY_RUNBOOK §G.4
 * control-10a shortcut — the window's 7-day floor is not shortenable.
 */
test.afterAll(async () => {
  await api.resetFakeSteamState();
  await closePool();
});

test('UI happy path: badge tracks CREATED → COMPLETED for both parties', async ({
  page,
  context,
  browser,
}) => {
  await api.resetFakeSteamState();

  // Mainline shape: the buyer is a pre-registered STEAM_ID user, so create sets
  // the transaction's BuyerId. This exercises the WP20 canAccept fix — a
  // registered STEAM_ID buyer (BuyerId set) must still see the UI accept form
  // enabled (the gate no longer requires BuyerId=null).
  await seedHappyPath();
  const sellerToken = mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId });
  const buyerToken = mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId });

  // Seller creates the transaction (API) — the create wizard is out of scope;
  // AC3 is about the UI reflecting state, exercised from the parties' detail views.
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

  // Buyer logs in (JWT-inject) and opens the detail page.
  await injectLogin(context, buyerToken);
  await page.goto(`/en/transactions/${txId}`, { waitUntil: 'domcontentloaded' });

  // Seller gets their own context — the confirm-ready button belongs to their
  // row of the 04 §7.3 matrix and is invisible from the buyer's page.
  const sellerContext = await browser.newContext();
  await injectLogin(sellerContext, sellerToken);
  const sellerPage = await sellerContext.newPage();

  try {
    // CREATED — badge + accept form visible on the buyer's page.
    await expect(page.getByTestId('tx-status-badge')).toHaveAttribute('data-status', 'CREATED', {
      timeout: 30_000,
    });

    // Accept via the real UI form → ACCEPTED. v3.0 made the Steam trade URL a
    // second mandatory field (07 §7.6): in P2P it is the address the seller
    // will actually send the item to, so the accept form collects it up front.
    await page.getByTestId('accept-refund-input').fill(seed.buyerRefundAddress);
    await page.getByTestId('accept-trade-url-input').fill(seed.buyerTradeUrl);
    await page.getByTestId('accept-submit').click();
    await waitForUiStatus(page, 'ACCEPTED', { timeoutMs: 30_000, intervalMs: 2_000 });

    // Seller's page: ACCEPTED × seller is the confirm-ready row. Pressing it
    // runs the three Steam gates and opens the payment window.
    await sellerPage.goto(`/en/transactions/${txId}`, { waitUntil: 'domcontentloaded' });
    await waitForUiStatus(sellerPage, 'ACCEPTED', { timeoutMs: 30_000, intervalMs: 2_000 });
    await sellerPage.getByTestId('confirm-ready-submit').click();
    await waitForUiStatus(sellerPage, 'SELLER_CONFIRMED', { timeoutMs: 30_000, intervalMs: 2_000 });

    // Both parties see the transition; only from here does the buyer's page
    // carry the deposit address block (07 §7.5 populates payment from
    // SELLER_CONFIRMED onwards).
    await waitForUiStatus(page, 'SELLER_CONFIRMED', { timeoutMs: 30_000, intervalMs: 2_000 });

    // Buyer pays (fake control surface) → PAYMENT_RECEIVED.
    const pay = await api.payViaFake(txId);
    expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
    await waitForUiStatus(page, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });

    // PAYMENT_RECEIVED × seller is the "send the item now" row: the CTA carries
    // the buyer's own trade URL and is returned to the SELLER ONLY (02 §2.2
    // step 6). Its presence is the UI half of "the platform is not a party".
    await waitForUiStatus(sellerPage, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });
    await expect(sellerPage.getByTestId('seller-trade-cta')).toBeVisible({ timeout: 30_000 });

    // The trade itself — outside the platform, simulated on the fake.
    const trade = await api.simulateFakeTrade(
      seed.sellerSteamId,
      seed.buyerSteamId,
      seed.itemAssetId,
    );
    expect(trade.ok, `trade simulation failed: ${JSON.stringify(trade.body)}`).toBeTruthy();

    // Buyer confirms receipt through the real modal → ITEM_DELIVERED.
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.getByTestId('confirm-receipt-open').click();
    await page.getByTestId('confirm-receipt-submit').click();
    await waitForUiStatus(page, 'ITEM_DELIVERED', { timeoutMs: 60_000, intervalMs: 2_000 });

    // ITEM_DELIVERED is the settlement wait, not the end: both parties get the
    // notice rather than an action (02 §4.5.1).
    await expect(page.getByTestId('settlement-notice-buyer')).toBeVisible({ timeout: 30_000 });
    await waitForUiStatus(sellerPage, 'ITEM_DELIVERED', { timeoutMs: 60_000, intervalMs: 2_000 });
    await expect(sellerPage.getByTestId('settlement-notice-seller')).toBeVisible({
      timeout: 30_000,
    });

    // Bring the settlement window forward, then let the real jobs run.
    await setPayoutEligibleNow(txId);
    const verified = await pollSettlementVerified(txId);
    expect(
      verified?.settlementVerifiedAt,
      `settlement never verified: ${JSON.stringify(verified)}`,
    ).toBeTruthy();

    // Seller payout pipeline → COMPLETED, seen by both badges.
    await waitForUiStatus(page, 'COMPLETED', { timeoutMs: 300_000 });
    await waitForUiStatus(sellerPage, 'COMPLETED', { timeoutMs: 60_000, intervalMs: 3_000 });
  } finally {
    await sellerContext.close();
  }
});
