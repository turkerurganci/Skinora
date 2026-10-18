import { describe, expect, it } from 'vitest';
import {
  BUYER,
  HOT,
  SPAM,
  USDC,
  USDT,
  deposit,
  keysOf,
  monitorHarness,
} from '../testing/monitorHarness.js';

/**
 * The Ethereum payment monitor over a fake chain that interprets the real
 * eth_getLogs / eth_call requests (T151 — 08 §3.7.4, K5). "Sahte zincir;
 * reorg ve sayfa sınırı" of the task's test expectation.
 */

const FIVE_MIN = 5 * 60_000;

/** Lets the re-arm window pass, so a new PAYMENT deposit counts as fresh. */
function afterRearmWindow(h: ReturnType<typeof monitorHarness>): void {
  h.clock.now += FIVE_MIN + 1;
}

describe('payment detection — finalized blocks only (K5)', () => {
  it('reports a USDT payment in a finalized block as detected then confirmed, with the chain log index', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick(); // boot at the finalized head
    afterRearmWindow(h);
    const d = deposit(1);
    h.arm(d);
    await h.scanner.tick(); // picks the fresh deposit up (empty)

    const { block, txHashes } = h.chain.mine([
      { contract: USDT, from: BUYER, to: d, value: 20_400_000n },
    ]);
    h.chain.mineTo(block + 70n);
    h.chain.finalize(block + 40n);
    await h.scanner.tick();

    expect(h.sent.map((s) => s.event)).toEqual(['payment.detected', 'payment.confirmed']);
    expect(h.sent[0].data).toMatchObject({
      paymentAddressId: 'pa-0001',
      transactionId: 'tx-0001',
      txHash: txHashes[0],
      eventIndex: 0,
      fromAddress: BUYER,
      toAddress: d,
      contractAddress: USDT,
      tokenSymbol: 'USDT',
      amount: '20.400000',
      blockTimestampMs: h.chain.timestampOf(block) * 1000,
    });
    expect(h.sent[1].data).toMatchObject({
      txHash: txHashes[0],
      eventIndex: 0,
      blockNumber: Number(block),
      confirmationCount: 40,
    });
    expect(h.scanner.scannedBlock()).toBe(block + 40n);
  });

  it('does not report a transfer that is only in latest — it waits for finalized', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(2);
    h.arm(d);
    await h.scanner.tick();

    const { block } = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 5_000_000n }]);
    h.chain.mineTo(block + 60n);
    h.chain.finalize(block - 1n); // the transfer's block is past finalized
    await h.scanner.tick();
    expect(h.sent).toEqual([]);

    h.chain.finalize(block);
    await h.scanner.tick();
    expect(h.sent.map((s) => s.event)).toEqual(['payment.detected', 'payment.confirmed']);
    expect(h.sent[1].data.confirmationCount).toBe(0);
  });

  it('a reorg above finalized takes back a transfer that was never reported; the replacement is reported once', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(3);
    h.arm(d);
    await h.scanner.tick();

    const orphan = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 1_000_000n }]);
    h.chain.mineTo(orphan.block + 5n);
    await h.scanner.tick();
    h.chain.reorg(orphan.block); // the block with the transfer is replaced
    const kept = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 2_000_000n }]);
    h.chain.mineTo(kept.block + 40n);
    h.chain.finalize(kept.block + 10n);
    await h.scanner.tick();
    await h.scanner.tick();

    expect(keysOf(h.sent)).toEqual([
      `payment.detected:${kept.txHashes[0]}:0`,
      `payment.confirmed:${kept.txHashes[0]}:0`,
    ]);
    expect(h.sent.some((s) => s.data.txHash === orphan.txHashes[0])).toBe(false);
  });

  it('ignores a finalized head that goes backwards (a lagging provider) and reports nothing twice', async () => {
    const h = monitorHarness();
    h.chain.mineTo(200n);
    h.chain.finalize(150n);
    await h.scanner.tick();
    expect(h.scanner.scannedBlock()).toBe(150n);
    h.arm(deposit(99), { cadence: 'HOLDING' });
    await h.scanner.tick();
    const reads = h.chain.count('eth_getLogs');
    h.head.override = 120n; // the head was read from a provider 30 blocks behind
    await h.scanner.tick();
    expect(h.scanner.scannedBlock()).toBe(150n);
    expect(h.chain.count('eth_getLogs')).toBe(reads);
  });
});

describe('token classification (08 §3.7.4 izin listesi)', () => {
  it('USDC on a USDT deposit is a wrong token, an unknown contract is spam, an ERC-721 Transfer is ignored', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(4);
    h.arm(d);
    await h.scanner.tick();

    const usdc = h.chain.mine([{ contract: USDC, from: BUYER, to: d, value: 7_000_000n }]);
    const spam = h.chain.mine([{ contract: SPAM, from: BUYER, to: d, value: 10n ** 18n }]);
    h.chain.mine([
      {
        contract: SPAM,
        from: BUYER,
        to: d,
        value: 0n,
        extraTopic: '0x' + '1'.padStart(64, '0'), // ERC-721 token id
      },
    ]);
    h.chain.mineTo(spam.block + 50n);
    h.chain.finalize(spam.block + 20n);
    await h.scanner.tick();

    expect(h.sent.map((s) => s.event)).toEqual(['payment.wrong_token', 'payment.spam_token']);
    expect(h.sent[0].data).toMatchObject({
      txHash: usdc.txHashes[0],
      expectedContractAddress: USDT,
      actualContractAddress: USDC,
      actualTokenSymbol: 'USDC',
      amount: '7.000000',
    });
    expect(h.sent[1].data).toMatchObject({
      txHash: spam.txHashes[0],
      actualContractAddress: SPAM,
      amount: '1.000000000000000000',
    });
  });

  it('matches the expected contract whatever letter case the backend sends it in', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(5);
    h.arm(d.toUpperCase().replace('0X', '0x'), {
      expectedContract: '0xdAC17F958D2ee523a2206206994597C13D831ec7',
    });
    await h.scanner.tick();
    const { block } = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 1n }]);
    h.chain.mineTo(block + 40n);
    h.chain.finalize(block);
    await h.scanner.tick();
    expect(h.sent.map((s) => s.event)).toEqual(['payment.detected', 'payment.confirmed']);
  });
});

describe('a provider that refuses eth_getLogs without an address (Sepolia PublicNode, T151 probe)', () => {
  it('still sees the payment and the wrong token through the allowlisted contracts; only spam is lost', async () => {
    const h = monitorHarness();
    h.chain.refuseAddressless = true;
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(20);
    h.arm(d);
    await h.scanner.tick();
    h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 1_000_000n }]);
    h.chain.mine([{ contract: USDC, from: BUYER, to: d, value: 2_000_000n }]);
    const spam = h.chain.mine([{ contract: SPAM, from: BUYER, to: d, value: 3n }]);
    h.chain.mineTo(spam.block + 40n);
    h.chain.finalize(spam.block);
    await h.scanner.tick();
    expect(h.sent.map((s) => s.event)).toEqual([
      'payment.detected',
      'payment.confirmed',
      'payment.wrong_token',
    ]);
    expect(h.scanner.scannedBlock()).toBe(spam.block);
    // The fallback still names the deposits as recipients (not every USDT transfer on chain).
    const fallback = h.chain.calls.filter((c) => {
      const f = c.params[0] as { address?: unknown; topics?: unknown[] } | undefined;
      return c.method === 'eth_getLogs' && Array.isArray(f?.address) && f?.topics?.[1] === null;
    });
    expect(fallback.length).toBeGreaterThan(0);
    for (const c of fallback) {
      expect((c.params[0] as { topics: unknown[] }).topics[2]).toEqual([
        '0x' + d.slice(2).padStart(64, '0'),
      ]);
    }
  });
});

describe('wrong chain (08 §3.7.1)', () => {
  it('a provider on another chain is never read: nothing is reported and the deposit is not marked read', async () => {
    const h = monitorHarness({ chainId: 1, configuredChainId: 11155111 });
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    // The head is read by the FinalityTracker in production; here the snapshot
    // comes from the fake directly, so the scan's own reads meet the check.
    await h.scanner.tick();
    const d = deposit(6);
    h.arm(d);
    const { block } = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 1n }]);
    h.chain.mineTo(block + 40n);
    h.chain.finalize(block + 1n);
    await h.scanner.tick();
    await h.scanner.tick();
    expect(h.sent).toEqual([]);
    // Picked up from 0 (lookback past genesis) and never moved: every read was refused.
    expect(h.registry.entries()[0].scan.scannedThrough).toBe(0n);
    expect(h.chain.calls.filter((c) => c.method === 'eth_getLogs')).toEqual([]);
  });
});

describe('query limits — the address list and the block range are split (08 §3.7.4 "Sorgu")', () => {
  it('1,500 deposits, a 10-block provider range: payments to the first and the last deposit are each reported once', async () => {
    const h = monitorHarness({ maxBlockRange: 10, maxAddresses: 1000 });
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    for (let i = 0; i < 1500; i++) {
      h.registry.startActive({
        address: deposit(1000 + i),
        paymentAddressId: `pa${i}`,
        transactionId: `t${i}`,
        expectedContract: USDT,
        expectedSymbol: 'USDT',
        cadence: 'HOLDING', // read back instead of probed: 1,500 probes are not the point here
      });
    }
    await h.scanner.tick();
    await h.scanner.tick();
    const first = h.chain.mine([{ contract: USDT, from: BUYER, to: deposit(1000), value: 1n }]);
    h.chain.mineTo(first.block + 20n);
    const last = h.chain.mine([{ contract: USDT, from: BUYER, to: deposit(2499), value: 2n }]);
    h.chain.mineTo(last.block + 40n);
    h.chain.finalize(last.block + 3n); // 35 blocks: four ranges
    await h.scanner.tick();

    const detected = h.sent.filter((s) => s.event === 'payment.detected');
    expect(detected.map((s) => s.data.toAddress)).toEqual([deposit(1000), deposit(2499)]);
    // No eth_getLogs request carried more than 1,000 recipients or 10 blocks.
    for (const call of h.chain.calls.filter((c) => c.method === 'eth_getLogs')) {
      const f = call.params[0] as { fromBlock: string; toBlock: string; topics: unknown[] };
      expect(BigInt(f.toBlock) - BigInt(f.fromBlock) + 1n).toBeLessThanOrEqual(10n);
      for (const t of f.topics) if (Array.isArray(t)) expect(t.length).toBeLessThanOrEqual(1000);
    }
  });

  it('a range refused for too many results is halved and nothing is lost', async () => {
    const h = monitorHarness({ maxResults: 3, providerRange: 10 });
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(7);
    h.arm(d);
    await h.scanner.tick();
    const transfers = [];
    for (let i = 0; i < 8; i++) {
      transfers.push(h.chain.mine([{ contract: USDC, from: BUYER, to: d, value: BigInt(i + 1) }]));
    }
    h.chain.mineTo(110n);
    h.chain.finalize(110n);
    await h.scanner.tick();
    expect(h.sent.filter((s) => s.event === 'payment.wrong_token')).toHaveLength(8);
  });
});

describe('re-reading never double-reports a transfer under another key', () => {
  it('a retryable webhook failure keeps the range; the next pass sends only what was not delivered', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(8);
    h.arm(d);
    await h.scanner.tick();
    const a = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 1n }]);
    const b = h.chain.mine([{ contract: USDC, from: BUYER, to: d, value: 2n }]);
    h.chain.mineTo(b.block + 40n);
    h.chain.finalize(b.block);
    // The third send (the wrong-token event) fails once with 503.
    h.failAt.set(h.attempts() + 3, 503);

    await h.scanner.tick();
    expect(h.scanner.scannedBlock()).toBe(100n); // the range was not passed
    await h.scanner.tick();

    expect(keysOf(h.sent)).toEqual([
      `payment.detected:${a.txHashes[0]}:0`,
      `payment.confirmed:${a.txHashes[0]}:0`,
      `payment.wrong_token:${b.txHashes[0]}:0`,
    ]);
    expect(h.scanner.scannedBlock()).toBe(b.block);
  });

  it('a backend 4xx is final: logged, not re-sent, and the range is passed', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(9);
    h.arm(d);
    await h.scanner.tick();
    const { block } = h.chain.mine([{ contract: USDC, from: BUYER, to: d, value: 1n }]);
    h.chain.mineTo(block + 40n);
    h.chain.finalize(block);
    h.failAt.set(h.attempts() + 1, 409);
    await h.scanner.tick();
    await h.scanner.tick();
    expect(h.sent).toEqual([]);
    expect(h.scanner.scannedBlock()).toBe(block);
  });

  it('several transfers in one transaction are reported once each, under their own log index', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(10);
    h.arm(d);
    await h.scanner.tick();
    const tx = h.chain.mineOneTransaction([
      { contract: SPAM, from: BUYER, to: HOT, value: 5n }, // log 0, not ours
      { contract: USDT, from: BUYER, to: d, value: 1_000_000n }, // log 1
      { contract: USDT, from: BUYER, to: d, value: 2_000_000n }, // log 2
    ]);
    h.chain.mineTo(tx.block + 40n);
    h.chain.finalize(tx.block);
    await h.scanner.tick();
    await h.scanner.tick();
    expect(keysOf(h.sent)).toEqual([
      `payment.detected:${tx.txHash}:1`,
      `payment.confirmed:${tx.txHash}:1`,
      `payment.detected:${tx.txHash}:2`,
      `payment.confirmed:${tx.txHash}:2`,
    ]);
  });

  it('after a restart the re-armed deposit is read back: the old payment comes again under the SAME key (backend dedup)', async () => {
    const h = monitorHarness({ restartLookbackBlocks: 300 });
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(11);
    h.arm(d);
    await h.scanner.tick();
    const pay = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 9n }]);
    h.chain.mineTo(pay.block + 40n);
    h.chain.finalize(pay.block + 5n);
    await h.scanner.tick();
    const before = keysOf(h.sent);

    // "Restart": a new registry and scanner; the backend re-arms within a minute.
    const { DepositRegistry } = await import('./DepositRegistry.js');
    const registry = new DepositRegistry({ clock: () => new Date(h.clock.now) });
    const restarted = h.makeScanner(registry);
    h.chain.mineTo(h.chain.latest + 20n);
    h.chain.finalize(pay.block + 15n);
    await restarted.tick(); // boots at the finalized head
    registry.startActive({
      address: d,
      paymentAddressId: 'pa-000b',
      transactionId: 'tx-000b',
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cadence: 'HOLDING',
    });
    h.sent.length = 0;
    await restarted.tick();
    expect(keysOf(h.sent)).toEqual(before);
  });
});

describe('where a deposit starts (restart, late arming, hand-over)', () => {
  it('a fresh PAYMENT deposit with nothing on it starts at the finalized head — no read-back', async () => {
    const h = monitorHarness();
    h.chain.mineTo(500n);
    h.chain.finalize(500n);
    await h.scanner.tick();
    afterRearmWindow(h);
    h.arm(deposit(12));
    const before = h.chain.count('eth_getLogs');
    await h.scanner.tick();
    expect(h.chain.count('eth_getLogs')).toBe(before);
    expect(h.registry.entries()[0].scan.scannedThrough).toBe(500n);
  });

  it('a fresh deposit armed late (the buyer paid before the start arrived) is read back and the payment reported', async () => {
    const h = monitorHarness({ restartLookbackBlocks: 300 });
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    const d = deposit(13);
    const pay = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 3n }]);
    h.chain.mineTo(pay.block + 60n);
    h.chain.finalize(pay.block + 30n);
    await h.scanner.tick();
    afterRearmWindow(h);
    h.arm(d); // outbox delivered the start after the payment was final
    await h.scanner.tick();
    expect(keysOf(h.sent)).toEqual([
      `payment.detected:${pay.txHashes[0]}:0`,
      `payment.confirmed:${pay.txHashes[0]}:0`,
    ]);
  });

  it('a payment older than the read-back is not found by the log path (the reconciliation is the second path)', async () => {
    const h = monitorHarness({ restartLookbackBlocks: 50 });
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    const d = deposit(14);
    const pay = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 3n }]);
    h.chain.mineTo(pay.block + 200n);
    h.chain.finalize(pay.block + 150n);
    await h.scanner.tick();
    h.arm(d, { cadence: 'HOLDING' });
    await h.scanner.tick();
    expect(h.sent).toEqual([]);
  });

  it('active → post-cancel hand-over keeps what was read: no read-back, a late payment is reported as late', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(15);
    h.arm(d);
    await h.scanner.tick();
    h.chain.mineTo(140n);
    h.chain.finalize(140n);
    await h.scanner.tick();

    h.registry.stopActive(d);
    h.registry.startPostCancel({
      address: d,
      paymentAddressId: 'pa-000f',
      transactionId: 'tx-000f',
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt: new Date(h.clock.now),
    });
    expect(h.registry.entries()[0].scan.scannedThrough).toBe(140n);

    const late = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 4_000_000n }]);
    h.chain.mineTo(late.block + 40n);
    h.chain.finalize(late.block);
    await h.scanner.tick();
    expect(h.sent.map((s) => s.event)).toEqual(['payment.late_detected']);
    expect(h.sent[0].data).toMatchObject({
      txHash: late.txHashes[0],
      eventIndex: 0,
      amount: '4.000000',
      monitorState: 'POST_CANCEL_24H',
      tokenSymbol: 'USDT',
    });
  });

  it('an address held by both registries (backend drift) is treated as cancelled: a late payment, not a payment', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(18);
    h.arm(d);
    await h.scanner.tick();
    h.registry.startPostCancel({
      address: d,
      paymentAddressId: 'pa-0012',
      transactionId: 'tx-0012',
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt: new Date(h.clock.now),
    });
    const pay = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 1n }]);
    h.chain.mineTo(pay.block + 40n);
    h.chain.finalize(pay.block);
    await h.scanner.tick();
    expect(h.sent.map((s) => s.event)).toEqual(['payment.late_detected']);
  });

  it('post-cancel states advance with the wall clock and stop at 30 days', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    const d = deposit(16);
    h.registry.startPostCancel({
      address: d,
      paymentAddressId: 'pa',
      transactionId: 'tx',
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt: new Date(h.clock.now),
    });
    h.clock.now += 24 * 3600_000;
    await h.scanner.tick();
    h.clock.now += 30 * 24 * 3600_000;
    await h.scanner.tick();
    expect(h.sent.map((s) => [s.data.previousState, s.data.newState])).toEqual([
      ['POST_CANCEL_24H', 'POST_CANCEL_7D'],
      ['POST_CANCEL_7D', 'POST_CANCEL_30D'],
      ['POST_CANCEL_30D', 'STOPPED'],
    ]);
    expect(h.registry.size()).toBe(0);
  });

  it('records the allowlisted tokens leaving a deposit (sweep, refund) for the reconciliation, without reporting them', async () => {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(17);
    h.arm(d);
    await h.scanner.tick();
    h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 10n }]);
    h.chain.mine([{ contract: USDT, from: d, to: HOT, value: 10n }]);
    h.chain.mineTo(150n);
    h.chain.finalize(150n);
    await h.scanner.tick();
    expect(h.sent.map((s) => s.event)).toEqual(['payment.detected', 'payment.confirmed']);
    const flows = [...h.registry.entries()[0].scan.flows].sort((a, b) => Number(a.block - b.block));
    expect(flows.map((f) => [f.symbol, f.delta])).toEqual([
      ['USDT', 10n],
      ['USDT', -10n],
    ]);
  });
});

describe('address list limit — exactly at the provider limit and one over (T151 doğrulaması)', () => {
  /** One finalized block read with <c>n</c> deposits; returns the eth_getLogs list lengths of that tick. */
  async function listsFor(n: number) {
    const h = monitorHarness({ maxBlockRange: 10, maxAddresses: 1000 });
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    for (let i = 0; i < n; i++) {
      h.registry.startActive({
        address: deposit(1000 + i),
        paymentAddressId: `pa${i}`,
        transactionId: `t${i}`,
        expectedContract: USDT,
        expectedSymbol: 'USDT',
        cadence: 'HOLDING',
      });
    }
    await h.scanner.tick();
    await h.scanner.tick();
    const mined = h.chain.mine([
      { contract: USDT, from: BUYER, to: deposit(1000 + n - 1), value: 3n },
    ]);
    h.chain.mineTo(mined.block + 2n);
    h.chain.finalize(mined.block + 2n);
    const before = h.chain.calls.length;
    await h.scanner.tick();
    const lists = h.chain.calls
      .slice(before)
      .filter((c) => c.method === 'eth_getLogs')
      .map((c) =>
        (c.params[0] as { topics: unknown[] }).topics
          .filter((t): t is string[] => Array.isArray(t))
          .map((t) => t.length),
      );
    const detected = h.sent.filter((x) => x.event === 'payment.detected');
    return { lists, detected };
  }

  it('1,000 deposits fit one request per direction (not split)', async () => {
    const { lists, detected } = await listsFor(1000);
    expect(lists).toEqual([[1000], [1000]]);
    expect(detected).toHaveLength(1);
  });

  it('1,001 deposits are split into 1,000 + 1 in each direction and the payment to the last one is reported once', async () => {
    const { lists, detected } = await listsFor(1001);
    expect(lists.map((l) => l[0]).sort((a, b) => a - b)).toEqual([1, 1, 1000, 1000]);
    expect(detected).toHaveLength(1);
  });
});

describe('logs a provider must not be trusted with are dropped (T151 doğrulaması)', () => {
  async function scenario(
    tamper: ((logs: Array<Record<string, unknown>>) => Array<Record<string, unknown>>) | null,
  ) {
    const h = monitorHarness();
    h.chain.mineTo(100n);
    h.chain.finalize(100n);
    await h.scanner.tick();
    afterRearmWindow(h);
    const d = deposit(3);
    h.arm(d);
    await h.scanner.tick();
    const { block } = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 7n }]);
    h.chain.mineTo(block + 5n);
    h.chain.finalize(block + 5n);
    h.chain.tamperLogs = tamper;
    await h.scanner.tick();
    return h.sent.map((s) => s.event);
  }

  it('control: the same transfer without tampering is reported', async () => {
    expect(await scenario(null)).toEqual(['payment.detected', 'payment.confirmed']);
  });

  it('a log flagged removed:true inside a finalized range is not reported', async () => {
    expect(await scenario((logs) => logs.map((l) => ({ ...l, removed: true })))).toEqual([]);
  });

  it('a log whose block is outside the requested range is not reported', async () => {
    expect(await scenario((logs) => logs.map((l) => ({ ...l, blockNumber: '0x1' })))).toEqual([]);
    expect(await scenario((logs) => logs.map((l) => ({ ...l, blockNumber: '0xffff' })))).toEqual(
      [],
    );
  });
});
