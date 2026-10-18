import { test, expect } from '@playwright/test';
import {
  seedHappyPath,
  seedEthereumPayoutAddress,
  getDeposit,
  getChainRows,
  pollChainRowConfirmed,
  getTransactionNetwork,
  setPayoutEligibleNow,
  pollSettlementVerified,
  closePool,
  seed,
  fakeBuyerWallet,
  fakeEthereumBuyerWallet,
  type ChainRow,
} from '../src/db';
import { mintAccessToken } from '../src/jwt';
import * as api from '../src/api';

/**
 * T155 — the two payment networks of F8 end to end (11 §F8 K1–K9), against the
 * docker-compose.e2e.yml stack. The fake answers TRON on :5200 and Ethereum on
 * :5300, each with its own router, formats and webhook secret, and journals
 * every call the backend makes to either — so "the money moved on the network
 * the buyer paid on" (K3) is checked on BOTH sides of the seam: the backend's
 * rows say which network it DECIDED, the fake's journal says which network's
 * SERVICE it asked.
 *
 *   1. Both networks' happy path, side by side: one listing per item accepts
 *      TRON + Ethereum; the buyer pays one on TRON and one on Ethereum; both
 *      reach COMPLETED. Fee lines come from the services' estimates (K8: TRON
 *      2.90 from the sweep estimate — not the 3.00 fallback; Ethereum 0.00);
 *      sweep + payout leave on the paid network to that network's addresses;
 *      the Ethereum seller is paid the full price (T149 ix).
 *   2. Refund on each network (K9): an underpayment on TRON and on Ethereum is
 *      refunded on its own network to the refund address fixed at acceptance —
 *      never to the paying wallet, never through the other network's service.
 *   3. Wrong-network webhooks are refused (T148): an Ethereum deposit's payment
 *      claimed as TRON, signed with TRON's secret, or both, records nothing.
 *   4. A listing refuses a network it does not accept (07 §1.3).
 *
 * The two happy paths run CONCURRENTLY in one test: the settlement window
 * (02 §4.5.1) costs up to ~8 minutes of real cron time per test that reaches
 * COMPLETED, and the two transactions wait it out together.
 */

const TRON_PAYOUT = seed.sellerPayoutAddress;
const ETH_PAYOUT = seed.sellerEthereumPayoutAddress.toLowerCase();
const ETH_REFUND = seed.buyerEthereumRefundAddress;

test.beforeEach(async () => {
  await api.resetFakeSteamState();
  await api.resetFakeBlockchain();
});

test.afterAll(async () => {
  await api.resetFakeSteamState();
  await api.resetFakeBlockchain();
  await closePool();
});

function tokens(): { sellerToken: string; buyerToken: string } {
  return {
    sellerToken: mintAccessToken({ userId: seed.sellerId, steamId: seed.sellerSteamId }),
    buyerToken: mintAccessToken({ userId: seed.buyerId, steamId: seed.buyerSteamId }),
  };
}

async function createListing(
  sellerToken: string,
  itemAssetId: string,
  acceptedNetworks: string[],
): Promise<string> {
  const create = await api.createTransaction(sellerToken, {
    itemAssetId,
    stablecoin: 'USDT',
    price: seed.price,
    paymentTimeoutHours: 1,
    buyerIdentificationMethod: 'STEAM_ID',
    buyerSteamId: seed.buyerSteamId,
    acceptedNetworks,
  });
  expect(create.ok, `create failed: ${JSON.stringify(create.body)}`).toBeTruthy();
  const created = api.unwrap(create.body);
  expect(created.status).toBe('CREATED');
  return String(created.id);
}

/** Buyer accepts on `network` with that network's refund address, seller
 *  confirms readiness → SELLER_CONFIRMED with a deposit on `network`. Returns
 *  the accept response (network, totalAmount, networkFeeAmount). */
async function acceptAndConfirm(
  txId: string,
  network: 'TRON' | 'ETHEREUM',
  refundAddress: string,
): Promise<Record<string, unknown>> {
  const { sellerToken, buyerToken } = tokens();
  const accept = await api.acceptTransaction(
    buyerToken,
    txId,
    refundAddress,
    seed.buyerTradeUrl,
    network,
  );
  expect(accept.ok, `accept (${network}) failed: ${JSON.stringify(accept.body)}`).toBeTruthy();
  const accepted = api.unwrap(accept.body);
  expect(accepted.status).toBe('ACCEPTED');
  expect(accepted.network).toBe(network);

  const ready = await api.confirmReady(sellerToken, txId);
  expect(ready.ok, `confirm-ready failed: ${JSON.stringify(ready.body)}`).toBeTruthy();
  expect(api.unwrap(ready.body).status).toBe('SELLER_CONFIRMED');
  return accepted;
}

/** Poll until the deposit is allocated (the allocator runs right after
 *  acceptance, best effort — T147 iii) and return it. */
async function pollDeposit(txId: string) {
  for (let i = 0; i < 30; i++) {
    const dep = await getDeposit(txId);
    if (dep) return dep;
    await new Promise((res) => setTimeout(res, 2_000));
  }
  throw new Error(`no deposit allocated for ${txId}`);
}

/** Poll until the fake is watching `address` on `network` (the monitor start
 *  rides the outbox, so it lands shortly after confirm-ready). */
async function pollMonitor(network: 'TRON' | 'ETHEREUM', address: string) {
  const same = (a: string) =>
    network === 'ETHEREUM' ? a.toLowerCase() === address.toLowerCase() : a === address;
  for (let i = 0; i < 40; i++) {
    const hit = (await api.getFakeMonitors(network)).find((m) => same(m.address));
    if (hit) return hit;
    await new Promise((res) => setTimeout(res, 2_000));
  }
  throw new Error(`${network} monitor for ${address} never started`);
}

function rowOf(rows: ChainRow[], type: string): ChainRow {
  const row = rows.find((r) => r.type === type);
  expect(row, `${type} row missing: ${JSON.stringify(rows)}`).toBeTruthy();
  return row!;
}

/** Every outgoing-transfer call the fake saw for these row ids, per network. */
async function transferCallsFor(rowIds: string[]) {
  const all = await api.getFakeBlockchainCalls({ path: '/api/transfer/' });
  const ids = new Set(rowIds.map((id) => id.toLowerCase()));
  return all.filter((c) => {
    const id = c.body?.blockchainTransactionId;
    return typeof id === 'string' && ids.has(id.toLowerCase());
  });
}

test('both networks: TRON and Ethereum sales complete on the network the buyer paid on', async () => {
  await seedHappyPath();
  await seedEthereumPayoutAddress();
  const { sellerToken, buyerToken } = tokens();

  // 1. Two listings, each accepting both networks (K3). The seller has a
  //    payout address on both, so both are offered.
  const tronTx = await createListing(sellerToken, seed.itemAssetId, ['TRON', 'ETHEREUM']);
  const ethTx = await createListing(sellerToken, seed.secondItemAssetId, ['TRON', 'ETHEREUM']);

  // 2. Before acceptance the buyer sees, per network, what they would pay
  //    (07 §7.5 networkQuotes). TRON carries the K8 network fee line, priced
  //    from the TRON service's sweep estimate: 8,441,349 SUN × 0.34 × 1.007 =
  //    2.8901 → 2.90. The 3.00 fallback here would mean the estimate never
  //    answered (the T149 validation's finding). Ethereum's line is 0.00 —
  //    the platform carries Ethereum's fee.
  const detail = api.unwrap((await api.getTransaction(buyerToken, tronTx)).body);
  expect(detail.acceptedNetworks).toEqual(['TRON', 'ETHEREUM']);
  expect(detail.networkQuotes).toEqual([
    { network: 'TRON', networkFeeAmount: '2.90', totalAmount: '104.90' },
    { network: 'ETHEREUM', networkFeeAmount: '0.00', totalAmount: '102.00' },
  ]);

  // 3. The buyer picks a network per listing; the line is fixed at acceptance.
  const tronAccepted = await acceptAndConfirm(tronTx, 'TRON', seed.buyerRefundAddress);
  expect(tronAccepted).toMatchObject({ networkFeeAmount: '2.90', totalAmount: '104.90' });
  const ethAccepted = await acceptAndConfirm(ethTx, 'ETHEREUM', ETH_REFUND);
  expect(ethAccepted).toMatchObject({ networkFeeAmount: '0.00', totalAmount: '102.00' });

  // 4. Each deposit is allocated on its own network, in that network's format,
  //    expecting exactly the accepted total (T144: 2 decimals).
  const tronDep = await pollDeposit(tronTx);
  const ethDep = await pollDeposit(ethTx);
  expect(tronDep).toMatchObject({ network: 'TRON', expectedAmount: 104.9 });
  expect(tronDep.address).toMatch(/^T[1-9A-HJ-NP-Za-km-z]{33}$/);
  expect(ethDep).toMatchObject({ network: 'ETHEREUM', expectedAmount: 102 });
  expect(ethDep.address).toMatch(/^0x[0-9a-f]{40}$/);

  // The address shown to the buyer is EIP-55 (T147 ix) — the backend's own
  // Keccak; it must equal what the Ethereum service answered (the fake's,
  // an independent implementation).
  const ethDetail = api.unwrap((await api.getTransaction(buyerToken, ethTx)).body);
  expect(ethDetail.network).toBe('ETHEREUM');
  const shown = String((ethDetail.payment as Record<string, unknown>).address);
  expect(shown).toBe(await api.fakeEthereumDerive(ethDep.hdWalletIndex));
  expect(shown).not.toBe(shown.toLowerCase());

  // Each deposit was derived through its own network's service …
  const derives = await api.getFakeBlockchainCalls({ path: '/api/wallet/derive' });
  const derivedOn = (index: number, network: string) =>
    derives.some((c) => c.network === network && c.body?.index === index && c.status === 200);
  expect(derivedOn(ethDep.hdWalletIndex, 'ETHEREUM'), JSON.stringify(derives)).toBe(true);
  expect(derivedOn(tronDep.hdWalletIndex, 'TRON'), JSON.stringify(derives)).toBe(true);

  // … and is watched there, for that network's USDT contract.
  const ethMonitor = await pollMonitor('ETHEREUM', ethDep.address);
  expect(ethMonitor.expectedContract.toLowerCase()).toBe(
    '0xdac17f958d2ee523a2206206994597c13d831ec7',
  );
  const tronMonitor = await pollMonitor('TRON', tronDep.address);
  expect(tronMonitor.expectedContract).toBe('TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t');

  // 5. Pay both (the fake speaks for each deposit's own network) → K5 payment
  //    accepted on each.
  for (const txId of [tronTx, ethTx]) {
    const pay = await api.payViaFake(txId);
    expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  }
  await api.pollStatus(buyerToken, tronTx, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });
  await api.pollStatus(buyerToken, ethTx, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });

  const ethPayment = rowOf(await getChainRows(ethTx), 'BUYER_PAYMENT');
  expect(ethPayment).toMatchObject({ network: 'ETHEREUM', status: 'CONFIRMED', amount: 102 });
  expect(ethPayment.txHash).toMatch(/^0x[0-9a-f]{64}$/);
  const tronPayment = rowOf(await getChainRows(tronTx), 'BUYER_PAYMENT');
  expect(tronPayment).toMatchObject({ network: 'TRON', status: 'CONFIRMED', amount: 104.9 });
  expect(tronPayment.txHash).toMatch(/^[0-9a-f]{64}$/);

  // 6. Delivery (P2P trade + buyer confirmation) and the settlement window,
  //    for both at once.
  const moves: Array<[string, string]> = [
    [tronTx, seed.itemAssetId],
    [ethTx, seed.secondItemAssetId],
  ];
  for (const [txId, assetId] of moves) {
    const trade = await api.simulateFakeTrade(seed.sellerSteamId, seed.buyerSteamId, assetId);
    expect(trade.ok, `trade failed: ${JSON.stringify(trade.body)}`).toBeTruthy();
    const receipt = await api.confirmReceipt(buyerToken, txId);
    expect(receipt.ok, `confirm-receipt failed: ${JSON.stringify(receipt.body)}`).toBeTruthy();
    expect(api.unwrap(receipt.body).status).toBe('ITEM_DELIVERED');
    await setPayoutEligibleNow(txId);
  }
  for (const txId of [tronTx, ethTx]) {
    const verified = await pollSettlementVerified(txId);
    expect(verified?.settlementVerifiedAt, `settlement for ${txId}`).toBeTruthy();
  }

  // 7. Sweep → payout → COMPLETED, each on its own network.
  await api.pollStatus(buyerToken, ethTx, 'COMPLETED', { timeoutMs: 480_000 });
  await api.pollStatus(buyerToken, tronTx, 'COMPLETED', { timeoutMs: 480_000 });

  const ethRows = await getChainRows(ethTx);
  const ethSweep = rowOf(ethRows, 'SWEEP');
  const ethPayout = rowOf(ethRows, 'SELLER_PAYOUT');
  expect(ethSweep).toMatchObject({ network: 'ETHEREUM', status: 'CONFIRMED' });
  expect(ethSweep.toAddress).toBe(seed.ethereumHotWallet);
  expect(ethPayout).toMatchObject({
    network: 'ETHEREUM',
    status: 'CONFIRMED',
    toAddress: ETH_PAYOUT,
  });
  // T149 ix — on Ethereum the seller gets the full price; the fee the platform
  // carries is the Ethereum service's estimate (0.05), not the 0.50 fallback.
  expect(ethPayout.amount).toBe(100);
  expect(ethPayout.gasFee).toBe(0.05);
  for (const row of [ethSweep, ethPayout]) expect(row.txHash).toMatch(/^0x[0-9a-f]{64}$/);

  const tronRows = await getChainRows(tronTx);
  const tronSweep = rowOf(tronRows, 'SWEEP');
  const tronPayout = rowOf(tronRows, 'SELLER_PAYOUT');
  expect(tronSweep).toMatchObject({ network: 'TRON', status: 'CONFIRMED' });
  expect(tronSweep.toAddress).toBe(seed.tronHotWallet);
  expect(tronPayout).toMatchObject({
    network: 'TRON',
    status: 'CONFIRMED',
    toAddress: TRON_PAYOUT,
  });
  // K8 — on TRON the seller bears the payout fee above 10% of the commission:
  // estimate 2.00 − 0.20 (10% × 2.00 commission) = 1.80 off a 100 price.
  expect(tronPayout.gasFee).toBe(2);
  expect(tronPayout.amount).toBe(98.2);

  // 8. The other side of the seam: every broadcast and status read of the
  //    Ethereum rows went to the Ethereum service and nowhere else; the TRON
  //    rows' to the TRON service.
  const ethCalls = await transferCallsFor([ethSweep.id, ethPayout.id]);
  expect(ethCalls.length, JSON.stringify(ethCalls)).toBeGreaterThanOrEqual(2);
  expect(ethCalls.every((c) => c.network === 'ETHEREUM' && c.status === 200)).toBe(true);
  expect(ethCalls.find((c) => c.path === '/api/transfer/payout')?.body?.toAddress).toBe(ETH_PAYOUT);
  const tronCalls = await transferCallsFor([tronSweep.id, tronPayout.id]);
  expect(tronCalls.length, JSON.stringify(tronCalls)).toBeGreaterThanOrEqual(2);
  expect(tronCalls.every((c) => c.network === 'TRON' && c.status === 200)).toBe(true);

  const statusReads = await api.getFakeBlockchainCalls({ path: '/api/transfer/status/' });
  const readOn = (hash: string | null) => statusReads.filter((c) => c.path.endsWith(`/${hash}`));
  for (const row of [ethSweep, ethPayout]) {
    const reads = readOn(row.txHash);
    expect(reads.length, `status of ${row.type} never read`).toBeGreaterThan(0);
    expect(reads.every((c) => c.network === 'ETHEREUM')).toBe(true);
  }
  for (const row of [tronSweep, tronPayout]) {
    expect(readOn(row.txHash).every((c) => c.network === 'TRON')).toBe(true);
  }

  // Nothing the backend sent either service was refused for naming the wrong
  // network or carrying the other network's address format.
  const refused = (await api.getFakeBlockchainCalls()).filter((c) => c.status === 400);
  expect(refused, JSON.stringify(refused)).toEqual([]);
});

test('refund on each network goes to the refund address fixed at acceptance (K9)', async () => {
  await seedHappyPath();
  await seedEthereumPayoutAddress();
  const { sellerToken } = tokens();

  const tronTx = await createListing(sellerToken, seed.itemAssetId, ['TRON']);
  const ethTx = await createListing(sellerToken, seed.secondItemAssetId, ['ETHEREUM']);
  await acceptAndConfirm(tronTx, 'TRON', seed.buyerRefundAddress);
  await acceptAndConfirm(ethTx, 'ETHEREUM', ETH_REFUND);
  const ethDep = await pollDeposit(ethTx);
  const tronDep = await pollDeposit(tronTx);

  // 03 §5.1 — the buyer underpays on both networks. 10 − fee clears the
  // 2 × fee minimum on each (TRON fee 2.00, Ethereum 0.05).
  for (const txId of [tronTx, ethTx]) {
    const pay = await api.payViaFake(txId, { amount: '10.00' });
    expect(pay.ok, `pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  }

  const ethRefund = await pollChainRowConfirmed(ethTx, 'INCORRECT_AMOUNT_REFUND');
  expect(ethRefund, 'Ethereum INCORRECT_AMOUNT_REFUND never confirmed').not.toBeNull();
  expect(ethRefund).toMatchObject({ network: 'ETHEREUM', status: 'CONFIRMED', amount: 10 });
  expect(ethRefund?.toAddress).toBe(ETH_REFUND.toLowerCase());
  expect(ethRefund?.toAddress).not.toBe(fakeEthereumBuyerWallet);
  expect(ethRefund?.txHash).toMatch(/^0x[0-9a-f]{64}$/);

  const tronRefund = await pollChainRowConfirmed(tronTx, 'INCORRECT_AMOUNT_REFUND');
  expect(tronRefund, 'TRON INCORRECT_AMOUNT_REFUND never confirmed').not.toBeNull();
  expect(tronRefund).toMatchObject({ network: 'TRON', status: 'CONFIRMED', amount: 10 });
  expect(tronRefund?.toAddress).toBe(seed.buyerRefundAddress);
  expect(tronRefund?.toAddress).not.toBe(fakeBuyerWallet);

  // The refund left from the deposit through that deposit's network service,
  // and its fee was that service's estimate for a deposit-sourced transfer.
  const [ethCall] = await transferCallsFor([ethRefund!.id]);
  expect(ethCall).toMatchObject({ network: 'ETHEREUM', path: '/api/transfer/refund', status: 200 });
  expect(String(ethCall.body?.toBuyerAddress).toLowerCase()).toBe(ETH_REFUND.toLowerCase());
  expect(String(ethCall.body?.depositAddress).toLowerCase()).toBe(ethDep.address);
  const [tronCall] = await transferCallsFor([tronRefund!.id]);
  expect(tronCall).toMatchObject({ network: 'TRON', path: '/api/transfer/refund', status: 200 });
  expect(tronCall.body?.depositAddress).toBe(tronDep.address);

  const ethEstimates = await api.getFakeBlockchainCalls({
    network: 'ETHEREUM',
    path: '/api/transfer/estimate-fee',
  });
  expect(
    ethEstimates.some(
      (c) => String(c.body?.fromAddress).toLowerCase() === ethDep.address && c.status === 200,
    ),
    JSON.stringify(ethEstimates),
  ).toBe(true);

  // The payment was not accepted on either network (03 §5.1 step 4).
  for (const txId of [tronTx, ethTx]) {
    expect((await getTransactionNetwork(txId))?.status).toBe('SELLER_CONFIRMED');
  }
  const refused = (await api.getFakeBlockchainCalls()).filter((c) => c.status === 400);
  expect(refused, JSON.stringify(refused)).toEqual([]);
});

test('a payment webhook naming the wrong network is refused and records nothing (T148)', async () => {
  await seedHappyPath();
  await seedEthereumPayoutAddress();
  const { sellerToken, buyerToken } = tokens();
  const ethTx = await createListing(sellerToken, seed.secondItemAssetId, ['ETHEREUM']);
  await acceptAndConfirm(ethTx, 'ETHEREUM', ETH_REFUND);
  await pollDeposit(ethTx);

  // Each lever changes ONE thing about an otherwise valid Ethereum payment:
  //   • claims TRON, signed by Ethereum → body network ≠ signer's network;
  //   • claims Ethereum, signed by TRON → same, the other way round;
  //   • claims TRON, signed by TRON     → consistent caller, but the deposit is
  //     an Ethereum deposit (PaymentAddress.Network) — a leaked TRON secret
  //     cannot speak for Ethereum money.
  const cases: Array<{ claimNetwork?: string; signAs?: string }> = [
    { claimNetwork: 'TRON' },
    { signAs: 'TRON' },
    { claimNetwork: 'TRON', signAs: 'TRON' },
  ];
  for (const lever of cases) {
    const pay = await api.payViaFake(ethTx, lever);
    expect(pay.ok, `${JSON.stringify(lever)} was accepted: ${JSON.stringify(pay.body)}`).toBe(
      false,
    );
    const body = pay.body as {
      backendStatus?: number;
      backendBody?: { error?: { code?: string } };
    };
    expect(body.backendStatus, JSON.stringify(pay.body)).toBe(409);
    expect(JSON.stringify(body.backendBody)).toContain('WEBHOOK_NETWORK_MISMATCH');
  }
  expect((await getChainRows(ethTx)).filter((r) => r.type === 'BUYER_PAYMENT')).toEqual([]);
  expect((await getTransactionNetwork(ethTx))?.status).toBe('SELLER_CONFIRMED');

  // The same payment, as Ethereum's service would send it, is accepted — so
  // the refusals above were about the network, not about the payment.
  const pay = await api.payViaFake(ethTx);
  expect(pay.ok, `valid pay failed: ${JSON.stringify(pay.body)}`).toBeTruthy();
  await api.pollStatus(buyerToken, ethTx, 'PAYMENT_RECEIVED', { timeoutMs: 90_000 });
});

test('a listing refuses a network it does not accept (07 §1.3 NETWORK_NOT_ACCEPTED)', async () => {
  await seedHappyPath();
  await seedEthereumPayoutAddress();
  const { sellerToken, buyerToken } = tokens();
  const tronOnly = await createListing(sellerToken, seed.itemAssetId, ['TRON']);

  const accept = await api.acceptTransaction(
    buyerToken,
    tronOnly,
    ETH_REFUND,
    seed.buyerTradeUrl,
    'ETHEREUM',
  );
  expect(accept.status, JSON.stringify(accept.body)).toBe(422);
  expect(JSON.stringify(accept.body)).toContain('NETWORK_NOT_ACCEPTED');
  const row = await getTransactionNetwork(tronOnly);
  expect(row).toMatchObject({ status: 'CREATED', network: null, buyerRefundAddress: null });
  expect(await getDeposit(tronOnly)).toBeNull();
});
