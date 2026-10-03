import { describe, it, expect, beforeEach } from 'vitest';
import { MonitorRegistry, type MonitorRegistryDeps } from './MonitorRegistry.js';
import type {
  ListTrc20Options,
  Trc20ListResponse,
  Trc20Record,
  TransactionInfo,
  TransferLogEntry,
} from '../tron/TronGridClient.js';
import { WebhookDeliveryError } from '../webhook/WebhookClient.js';
import type { AnyBlockchainWebhookPayload } from '../webhook/WebhookPayloads.js';
import { FakeTronGridLedger, type LedgerRecord } from '../testing/FakeTronGridLedger.js';
import { LOG_WAIT_MS } from './EventIndexResolver.js';

const DEPOSIT_ADDRESS = 'TDeposit1234567890DepositAddrFakeXX';
const PAYMENT_ADDRESS_ID = '11111111-1111-1111-1111-111111111111';
const TRANSACTION_ID = '22222222-2222-2222-2222-222222222222';
const USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const SPAM_TOKEN = 'TSpam111111111111111111111111111111';

type ListCall = {
  contractAddress?: string;
  fingerprint?: string;
  minTimestamp?: number;
};

interface FakeTronClient {
  enqueuePhase1(response: Trc20ListResponse): void;
  enqueuePhase2(response: Trc20ListResponse): void;
  setSolidBlock(block: number): void;
  setTxInfo(txHash: string, info: TransactionInfo | null): void;
  /** Override the on-chain log entries resolved for a (txHash, contract) pair. */
  setEventIndices(txHash: string, contract: string, entries: TransferLogEntry[]): void;
  client: MonitorRegistryDeps['client'];
  callsPhase1: ListCall[];
  callsPhase2: ListCall[];
  txInfoCalls: string[];
}

function createFakeClient(): FakeTronClient {
  const phase1Queue: Trc20ListResponse[] = [];
  const phase2Queue: Trc20ListResponse[] = [];
  const txInfo = new Map<string, TransactionInfo | null>();
  const eventIndices = new Map<string, TransferLogEntry[]>();
  const callsPhase1: ListCall[] = [];
  const callsPhase2: ListCall[] = [];
  const txInfoCalls: string[] = [];
  let currentSolid = 0;

  const fake: FakeTronClient = {
    callsPhase1,
    callsPhase2,
    txInfoCalls,
    enqueuePhase1(r) {
      phase1Queue.push(r);
    },
    enqueuePhase2(r) {
      phase2Queue.push(r);
    },
    setSolidBlock(b) {
      currentSolid = b;
    },
    setTxInfo(h, info) {
      txInfo.set(h, info);
    },
    setEventIndices(txHash, contract, entries) {
      eventIndices.set(`${txHash}:${contract}`, entries);
    },
    client: {
      // eslint-disable-next-line @typescript-eslint/require-await
      async listTrc20(options: ListTrc20Options): Promise<Trc20ListResponse> {
        if (options.contractAddress) {
          callsPhase1.push({
            contractAddress: options.contractAddress,
            fingerprint: options.fingerprint,
            minTimestamp: options.minTimestamp,
          });
          return phase1Queue.shift() ?? { records: [], fingerprint: null };
        }
        callsPhase2.push({ fingerprint: options.fingerprint, minTimestamp: options.minTimestamp });
        return phase2Queue.shift() ?? { records: [], fingerprint: null };
      },
      // eslint-disable-next-line @typescript-eslint/require-await
      async getNowSolidBlock(): Promise<number> {
        return currentSolid;
      },
      // eslint-disable-next-line @typescript-eslint/require-await
      async getTransactionInfoById(txHash: string): Promise<TransactionInfo | null> {
        txInfoCalls.push(txHash);
        return txInfo.get(txHash) ?? null;
      },
      // eslint-disable-next-line @typescript-eslint/require-await
      async resolveTransferEventIndices(
        txHash: string,
        contractAddress: string,
      ): Promise<TransferLogEntry[]> {
        return eventIndices.get(`${txHash}:${contractAddress}`) ?? [];
      },
    } as unknown as MonitorRegistryDeps['client'],
  };
  return fake;
}

interface SentWebhook {
  endpoint: string;
  envelope: AnyBlockchainWebhookPayload;
  correlationId: string;
}

function createFakeSender(opts: { failNext?: () => Error | null } = {}) {
  const sent: SentWebhook[] = [];
  const sender: MonitorRegistryDeps['webhookSender'] = async (
    endpoint,
    envelope,
    correlationId,
  ) => {
    const err = opts.failNext?.();
    if (err) {
      throw err;
    }
    sent.push({ endpoint, envelope, correlationId });
  };
  return { sender, sent };
}

const ENDPOINTS = {
  paymentDetected: '/api/v1/webhooks/blockchain/payment-detected',
  paymentConfirmed: '/api/v1/webhooks/blockchain/payment-confirmed',
  wrongTokenIncoming: '/api/v1/webhooks/blockchain/wrong-token',
  spamTokenIncoming: '/api/v1/webhooks/blockchain/spam-token',
};

function buildRegistry(opts: {
  client: MonitorRegistryDeps['client'];
  sender: MonitorRegistryDeps['webhookSender'];
  now?: Date;
  clock?: () => Date;
  minConfirmations?: number;
}): MonitorRegistry {
  const fixedNow = opts.now ?? new Date('2026-05-16T12:00:00Z');
  return new MonitorRegistry({
    client: opts.client,
    allowlist: { USDT, USDC },
    intervalMs: 60_000, // long — tests call tick() directly
    minConfirmations: opts.minConfirmations ?? 20,
    pageLimit: 20,
    webhookEndpoints: ENDPOINTS,
    clock: opts.clock ?? (() => fixedNow),
    webhookSender: opts.sender,
  });
}

function transferRecord(overrides: Partial<Trc20Record>): Trc20Record {
  return {
    transaction_id: overrides.transaction_id ?? 'txhash-1',
    token_info: overrides.token_info ?? { address: USDT, decimals: 6, symbol: 'USDT' },
    block_timestamp: overrides.block_timestamp ?? 1_778_000_000_000,
    from: overrides.from ?? 'TFrom111111111111111111111111111111',
    to: overrides.to ?? DEPOSIT_ADDRESS,
    type: overrides.type ?? 'Transfer',
    value: overrides.value ?? '100000000', // 100 USDT
  };
}

describe('MonitorRegistry', () => {
  let fake: FakeTronClient;
  let sender: ReturnType<typeof createFakeSender>;

  beforeEach(() => {
    fake = createFakeClient();
    sender = createFakeSender();
  });

  it('starts a monitor and reports it as active', () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    const result = registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });
    expect(result.started).toBe(true);
    expect(registry.size()).toBe(1);
  });

  it('start is idempotent for the same address', () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });
    const second = registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });
    expect(second.started).toBe(false);
    expect(registry.size()).toBe(1);
  });

  it('stop returns true only when address was monitored', () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    expect(registry.stop(DEPOSIT_ADDRESS).stopped).toBe(false);
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });
    expect(registry.stop(DEPOSIT_ADDRESS).stopped).toBe(true);
    expect(registry.size()).toBe(0);
  });

  it('emits PaymentDetected on phase 1 hit and tracks pending finality', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });

    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-1' })],
      fingerprint: 'fp-phase1-1',
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    // Tx not yet on solid node — finality check returns null.
    fake.setSolidBlock(1_000_000);

    await registry.tick();

    expect(sender.sent).toHaveLength(1);
    const sent = sender.sent[0];
    expect(sent.endpoint).toBe(ENDPOINTS.paymentDetected);
    expect(sent.envelope.event).toBe('payment.detected');
    const data = (
      sent.envelope as { data: { txHash: string; amount: string; tokenSymbol: string } }
    ).data;
    expect(data.txHash).toBe('tx-1');
    expect(data.amount).toBe('100.000000');
    expect(data.tokenSymbol).toBe('USDT');
  });

  it('skips Approval / non-Transfer records', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });

    fake.enqueuePhase1({
      records: [
        transferRecord({ transaction_id: 'tx-approval', type: 'Approval' }),
        transferRecord({ transaction_id: 'tx-auth', type: 'Authorization' }),
      ],
      fingerprint: 'fp-phase1-skip',
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    fake.setSolidBlock(1_000_000);

    await registry.tick();

    expect(sender.sent).toHaveLength(0);
  });

  it('skips outbound records where deposit address is the sender', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });

    fake.enqueuePhase1({
      records: [
        transferRecord({
          transaction_id: 'tx-outbound',
          from: DEPOSIT_ADDRESS,
          to: 'TElsewhere111111111111111111111111111',
        }),
      ],
      fingerprint: 'fp',
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    fake.setSolidBlock(1_000_000);

    await registry.tick();

    expect(sender.sent).toHaveLength(0);
  });

  it('idempotency: the same txHash does not re-emit on subsequent ticks', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });

    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-dup' })],
      fingerprint: 'fp-1',
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    fake.setSolidBlock(1_000_000);
    await registry.tick();

    // Same tx returned on next poll — fingerprint did not advance.
    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-dup' })],
      fingerprint: 'fp-1',
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    await registry.tick();

    expect(sender.sent).toHaveLength(1);
  });

  it('emits WrongTokenIncoming for allowlisted but non-expected tokens (phase 2)', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });

    fake.enqueuePhase1({ records: [], fingerprint: null });
    fake.enqueuePhase2({
      records: [
        transferRecord({
          transaction_id: 'tx-wrong',
          token_info: { address: USDC, decimals: 6, symbol: 'USDC' },
          value: '50000000',
        }),
      ],
      fingerprint: 'fp-2',
    });
    fake.setSolidBlock(1_000_000);

    await registry.tick();

    expect(sender.sent).toHaveLength(1);
    const sent = sender.sent[0];
    expect(sent.endpoint).toBe(ENDPOINTS.wrongTokenIncoming);
    expect(sent.envelope.event).toBe('payment.wrong_token');
    const data = (sent.envelope as { data: { actualTokenSymbol: string; amount: string } }).data;
    expect(data.actualTokenSymbol).toBe('USDC');
    expect(data.amount).toBe('50.000000');
  });

  it('emits SpamTokenIncoming for tokens not on the allowlist', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });

    fake.enqueuePhase1({ records: [], fingerprint: null });
    fake.enqueuePhase2({
      records: [
        transferRecord({
          transaction_id: 'tx-spam',
          token_info: { address: SPAM_TOKEN, decimals: 4, symbol: 'SPAM' },
          value: '999999999',
        }),
      ],
      fingerprint: 'fp-spam',
    });
    fake.setSolidBlock(1_000_000);

    await registry.tick();

    expect(sender.sent).toHaveLength(1);
    expect(sender.sent[0].endpoint).toBe(ENDPOINTS.spamTokenIncoming);
    expect(sender.sent[0].envelope.event).toBe('payment.spam_token');
  });

  it('emits PaymentConfirmed once finality is reached (delta >= 20)', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });

    // Tick 1: detect, finality call returns no block yet.
    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-final' })],
      fingerprint: 'fp-finality-1',
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    fake.setSolidBlock(1_000_000);
    fake.setTxInfo('tx-final', null);
    await registry.tick();
    expect(sender.sent).toHaveLength(1); // PaymentDetected only

    // Tick 2: tx now on solid node at block 999_990; delta 10 < 20.
    fake.enqueuePhase1({ records: [], fingerprint: null });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    fake.setSolidBlock(1_000_000);
    fake.setTxInfo('tx-final', { blockNumber: 999_990, contractRet: 'SUCCESS' });
    await registry.tick();
    expect(sender.sent).toHaveLength(1); // still no PaymentConfirmed

    // Tick 3: solid block advanced — delta now 20 (exactly meets threshold).
    fake.enqueuePhase1({ records: [], fingerprint: null });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    fake.setSolidBlock(1_000_010);
    await registry.tick();
    expect(sender.sent).toHaveLength(2);
    const confirmed = sender.sent[1];
    expect(confirmed.endpoint).toBe(ENDPOINTS.paymentConfirmed);
    expect(confirmed.envelope.event).toBe('payment.confirmed');
    const data = (
      confirmed.envelope as {
        data: { txHash: string; blockNumber: number; confirmationCount: number };
      }
    ).data;
    expect(data.txHash).toBe('tx-final');
    expect(data.blockNumber).toBe(999_990);
    expect(data.confirmationCount).toBe(20);
  });

  it('does not retry phase 2 records that were emitted as expected via phase 1', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });

    // Phase 1 reports the tx.
    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-both' })],
      fingerprint: 'fp-p1',
    });
    // Phase 2 (unfiltered) also reports the same tx — should not re-emit.
    fake.enqueuePhase2({
      records: [transferRecord({ transaction_id: 'tx-both' })],
      fingerprint: 'fp-p2',
    });
    fake.setSolidBlock(1_000_000);

    await registry.tick();

    expect(sender.sent).toHaveLength(1);
    expect(sender.sent[0].endpoint).toBe(ENDPOINTS.paymentDetected);
  });

  it('skips webhook delivery silently for non-retryable failures (4xx)', async () => {
    const failingSender = createFakeSender({
      failNext: () => new WebhookDeliveryError(400, 'Bad Request', 'payment.detected'),
    });
    const registry = buildRegistry({ client: fake.client, sender: failingSender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });

    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-4xx' })],
      fingerprint: 'fp',
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    fake.setSolidBlock(1_000_000);

    // Tick should not throw and should still complete the loop.
    await registry.tick();

    expect(failingSender.sent).toHaveLength(0);
    // Address is still in seen set — next tick will not re-emit.
    expect(registry.size()).toBe(1);
  });

  it('shutdown clears all monitors and zeroes the count', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });
    await registry.shutdown();
    expect(registry.size()).toBe(0);
    expect(() =>
      registry.start({
        address: DEPOSIT_ADDRESS,
        paymentAddressId: PAYMENT_ADDRESS_ID,
        transactionId: TRANSACTION_ID,
        expectedContract: USDT,
        expectedSymbol: 'USDT',
      }),
    ).toThrow(/shut down/);
  });
});

describe('MonitorRegistry — per-event dedup (WP10, 08 §3.4)', () => {
  let fake: FakeTronClient;
  let sender: ReturnType<typeof createFakeSender>;

  beforeEach(() => {
    fake = createFakeClient();
    sender = createFakeSender();
  });

  function startMonitor(registry: MonitorRegistry): void {
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });
  }

  it('stamps the real on-chain event index on a single-transfer detection', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    startMonitor(registry);
    fake.setEventIndices('tx-1', USDT, [{ index: 0, value: '100000000' }]);
    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-1' })],
      fingerprint: null,
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });

    await registry.tick();

    expect(sender.sent).toHaveLength(1);
    const data = (sender.sent[0].envelope as { data: { eventIndex: number } }).data;
    expect(data.eventIndex).toBe(0);
  });

  it('emits one detection per Transfer event for a multi-transfer transaction', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    startMonitor(registry);
    // One tx (tx-multi) carrying two transfers to the deposit address with
    // distinct amounts and distinct on-chain log indices.
    fake.setEventIndices('tx-multi', USDT, [
      { index: 0, value: '50000000' },
      { index: 1, value: '30000000' },
    ]);
    fake.enqueuePhase1({
      records: [
        transferRecord({ transaction_id: 'tx-multi', value: '50000000' }),
        transferRecord({ transaction_id: 'tx-multi', value: '30000000' }),
      ],
      fingerprint: null,
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });

    await registry.tick();

    expect(sender.sent).toHaveLength(2);
    const events = sender.sent.map((s) => {
      const d = (s.envelope as { data: { eventIndex: number; amount: string } }).data;
      return { eventIndex: d.eventIndex, amount: d.amount };
    });
    expect(events).toEqual([
      { eventIndex: 0, amount: '50.000000' },
      { eventIndex: 1, amount: '30.000000' },
    ]);
  });

  it('does not re-emit the same (txHash, eventIndex) across ticks', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    startMonitor(registry);
    fake.setEventIndices('tx-1', USDT, [{ index: 0, value: '100000000' }]);
    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-1' })],
      fingerprint: null,
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    await registry.tick();

    // Same record returns again next tick (polling overlap).
    fake.setEventIndices('tx-1', USDT, [{ index: 0, value: '100000000' }]);
    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-1' })],
      fingerprint: null,
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    await registry.tick();

    const detected = sender.sent.filter((s) => s.endpoint === ENDPOINTS.paymentDetected);
    expect(detected).toHaveLength(1);
  });

  it('falls back to index 0 when the node knows the transaction but no log matches', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    startMonitor(registry);
    // No setEventIndices → resolver returns [] (final, not null) → index 0, no wait.
    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-lag' })],
      fingerprint: null,
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });

    await registry.tick();

    expect(sender.sent).toHaveLength(1);
    const data = (sender.sent[0].envelope as { data: { eventIndex: number } }).data;
    expect(data.eventIndex).toBe(0);
  });

  it('confirms each event of a multi-transfer transaction with its own event index', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    startMonitor(registry);
    fake.setEventIndices('tx-multi', USDT, [
      { index: 0, value: '50000000' },
      { index: 1, value: '30000000' },
    ]);
    fake.enqueuePhase1({
      records: [
        transferRecord({ transaction_id: 'tx-multi', value: '50000000' }),
        transferRecord({ transaction_id: 'tx-multi', value: '30000000' }),
      ],
      fingerprint: null,
    });
    fake.enqueuePhase2({ records: [], fingerprint: null });
    // Block is solid + far enough for 20-confirmation finality.
    fake.setTxInfo('tx-multi', { blockNumber: 1000, contractRet: 'SUCCESS' });
    fake.setSolidBlock(1100);

    await registry.tick();

    const confirmed = sender.sent.filter((s) => s.endpoint === ENDPOINTS.paymentConfirmed);
    const indices = confirmed.map(
      (s) => (s.envelope as { data: { eventIndex: number } }).data.eventIndex,
    );
    expect(indices.sort()).toEqual([0, 1]);
  });
});

/**
 * T139-ActiveMonitorQuotaAlarm — owner decision 2026-10-02: 3 s only while the
 * payment is awaited, 15 min once it is confirmed (08 §3.4). At 3 s with two
 * list queries per tick one address cost 57,600 TronGrid requests a day for
 * its whole ~8-day window; a ~100,000/day plan held about two transactions.
 */
describe('MonitorRegistry — cadence (08 §3.4)', () => {
  const TICK_MS = 3_000;
  const HOLDING_MS = 900_000;
  let fake: FakeTronClient;
  let sender: ReturnType<typeof createFakeSender>;
  let nowMs: number;

  function cadenceRegistry(holdingIntervalMs: number = HOLDING_MS): MonitorRegistry {
    return new MonitorRegistry({
      client: fake.client,
      allowlist: { USDT, USDC },
      intervalMs: TICK_MS,
      holdingIntervalMs,
      minConfirmations: 20,
      pageLimit: 20,
      webhookEndpoints: ENDPOINTS,
      clock: () => new Date(nowMs),
      webhookSender: sender.sender,
    });
  }

  function startOptions(cadence?: 'PAYMENT' | 'HOLDING') {
    return {
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT' as const,
      ...(cadence ? { cadence } : {}),
    };
  }

  async function advance(ms: number, registry: MonitorRegistry): Promise<void> {
    nowMs += ms;
    await registry.tick();
  }

  beforeEach(() => {
    fake = createFakeClient();
    sender = createFakeSender();
    nowMs = Date.parse('2026-10-02T12:00:00Z');
  });

  it('polls a PAYMENT address on every tick', async () => {
    const registry = cadenceRegistry();
    registry.start(startOptions('PAYMENT'));

    await registry.tick();
    await advance(TICK_MS, registry);
    await advance(TICK_MS, registry);

    expect(fake.callsPhase1).toHaveLength(3);
    expect(fake.callsPhase2).toHaveLength(3);
  });

  it('polls a PAYMENT address on a tick that arrives a little early — setInterval drifts', async () => {
    const registry = cadenceRegistry();
    registry.start(startOptions('PAYMENT'));

    await registry.tick();
    await advance(TICK_MS - 1, registry);

    expect(fake.callsPhase1).toHaveLength(2);
  });

  it('treats a start without a cadence as PAYMENT — what a backend predating cadences relies on', async () => {
    const registry = cadenceRegistry();
    const result = registry.start(startOptions());

    await registry.tick();
    await advance(TICK_MS, registry);

    expect(result.cadence).toBe('PAYMENT');
    expect(registry.cadenceOf(DEPOSIT_ADDRESS)).toBe('PAYMENT');
    expect(fake.callsPhase1).toHaveLength(2);
  });

  it('polls a HOLDING address once, then not again until the holding interval has passed', async () => {
    const registry = cadenceRegistry();
    registry.start(startOptions('HOLDING'));

    await registry.tick(); // armed now → looked at once immediately
    for (let i = 0; i < 299; i++) await advance(TICK_MS, registry); // 897 s

    expect(fake.callsPhase1).toHaveLength(1);
    expect(fake.callsPhase2).toHaveLength(1);

    await advance(TICK_MS, registry); // 900 s → due

    expect(fake.callsPhase1).toHaveLength(2);
  });

  it('moves an address from PAYMENT to HOLDING when the backend re-arms it — cursors kept', async () => {
    const registry = cadenceRegistry();
    registry.start(startOptions('PAYMENT'));
    fake.enqueuePhase1({
      records: [transferRecord({ block_timestamp: 1_790_111_046_000 })],
      fingerprint: null,
    });
    await registry.tick();

    const rearmed = registry.start(startOptions('HOLDING'));

    expect(rearmed).toEqual({ started: false, cadence: 'HOLDING' });
    await advance(TICK_MS, registry);
    expect(fake.callsPhase1).toHaveLength(1);

    await advance(HOLDING_MS, registry);
    expect(fake.callsPhase1).toHaveLength(2);
    expect(fake.callsPhase1[0].minTimestamp).toBeUndefined();
    expect(fake.callsPhase1[1].minTimestamp).toBe(1_790_111_046_000);
  });

  it('returns to every-tick polling when re-armed as PAYMENT', async () => {
    const registry = cadenceRegistry();
    registry.start(startOptions('HOLDING'));
    await registry.tick();

    registry.start(startOptions('PAYMENT'));
    await advance(TICK_MS, registry);
    await advance(TICK_MS, registry);

    expect(fake.callsPhase1).toHaveLength(3);
  });

  it('keeps finality checks on the tick for a transfer seen on a HOLDING address', async () => {
    const registry = cadenceRegistry();
    registry.start(startOptions('HOLDING'));
    fake.enqueuePhase1({
      records: [transferRecord({ transaction_id: 'tx-late' })],
      fingerprint: null,
    });
    fake.setSolidBlock(1_000);
    await registry.tick(); // detected; not on the solid node yet

    fake.setTxInfo('tx-late', { blockNumber: 900 });
    await advance(TICK_MS, registry); // not due for a list poll — finality still runs

    expect(fake.callsPhase1).toHaveLength(1);
    expect(sender.sent.map((s) => s.endpoint)).toEqual([
      ENDPOINTS.paymentDetected,
      ENDPOINTS.paymentConfirmed,
    ]);
  });

  it.each([Number.NaN, TICK_MS - 1])(
    'refuses a holding interval of %s — a HOLDING address would never be due, or due faster than the tick',
    (holding) => {
      expect(() => cadenceRegistry(holding)).toThrow(/PAYMENT_HOLDING_POLLING_INTERVAL_MS/);
    },
  );

  it('reports the interval each cadence stands for — what the startup line prints', () => {
    const registry = cadenceRegistry();

    expect(registry.cadenceIntervalMs('PAYMENT')).toBe(TICK_MS);
    expect(registry.cadenceIntervalMs('HOLDING')).toBe(HOLDING_MS);
  });
});

/**
 * The registry against a ledger that answers like TronGrid does (Nile probes
 * 2026-10-02/03) — backlog `MonitorCursorPagesBackward`. The queue fakes above
 * return whatever was enqueued whatever the query, which is how a cursor
 * that walked toward older records passed every test.
 */
describe('MonitorRegistry — forward scan against a TronGrid-shaped ledger', () => {
  const T0 = 1_790_000_000_000;
  let ledger: FakeTronGridLedger;
  let sender: ReturnType<typeof createFakeSender>;

  beforeEach(() => {
    ledger = new FakeTronGridLedger();
    sender = createFakeSender();
  });

  function ledgerRecord(
    overrides: Partial<LedgerRecord> & { transaction_id: string },
  ): LedgerRecord {
    return { ...transferRecord(overrides), logIndex: overrides.logIndex, noLog: overrides.noLog };
  }

  function start(registry: MonitorRegistry): void {
    registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });
  }

  function sentTo(endpoint: string): Array<{ txHash: string; eventIndex: number }> {
    return sender.sent
      .filter((s) => s.endpoint === endpoint)
      .map((s) => s.envelope.data as unknown as { txHash: string; eventIndex: number })
      .map(({ txHash, eventIndex }) => ({ txHash, eventIndex }));
  }

  it('sees a wrong-token transfer that arrives after 25 spam transfers filled phase 2', async () => {
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    for (let i = 0; i < 25; i += 1) {
      ledger.add(
        ledgerRecord({
          transaction_id: `spam-${i}`,
          token_info: { address: SPAM_TOKEN, decimals: 6 },
          block_timestamp: T0 + i * 3000,
        }),
      );
    }
    await registry.tick();
    await registry.tick();

    ledger.add(
      ledgerRecord({
        transaction_id: 'usdc-late',
        token_info: { address: USDC, decimals: 6, symbol: 'USDC' },
        block_timestamp: T0 + 100 * 3000,
      }),
    );
    await registry.tick();

    expect(sentTo(ENDPOINTS.wrongTokenIncoming)).toEqual([{ txHash: 'usdc-late', eventIndex: 0 }]);
    expect(sentTo(ENDPOINTS.spamTokenIncoming)).toHaveLength(25);
  });

  it('sees a late USDT payment after more than a page of USDT transfers (zero-value poisoning)', async () => {
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    for (let i = 0; i < 21; i += 1) {
      ledger.add(
        ledgerRecord({ transaction_id: `poison-${i}`, value: '0', block_timestamp: T0 + i * 3000 }),
      );
    }
    await registry.tick();
    await registry.tick();

    ledger.add(ledgerRecord({ transaction_id: 'late-payment', block_timestamp: T0 + 100 * 3000 }));
    await registry.tick();

    const detected = sentTo(ENDPOINTS.paymentDetected).filter((d) => d.txHash === 'late-payment');
    expect(detected).toEqual([{ txHash: 'late-payment', eventIndex: 0 }]);
  });

  it('reports a transfer at log index 2 once — not again under index 0 on the re-read', async () => {
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    ledger.add(ledgerRecord({ transaction_id: 'tx-contract', logIndex: 2, block_timestamp: T0 }));

    await registry.tick();
    await registry.tick();
    await registry.tick();

    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([{ txHash: 'tx-contract', eventIndex: 2 }]);
  });

  it('a failed log lookup reports nothing; the next tick reports the real index once', async () => {
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    ledger.add(ledgerRecord({ transaction_id: 'tx-contract', logIndex: 2, block_timestamp: T0 }));
    ledger.failLogLookups = 1;

    await registry.tick();
    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([]);

    await registry.tick();
    await registry.tick();
    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([{ txHash: 'tx-contract', eventIndex: 2 }]);
  });

  it('a transfer the solidity node does not know yet waits, then is reported once under its real index', async () => {
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    ledger.add(ledgerRecord({ transaction_id: 'tx-contract', logIndex: 2, block_timestamp: T0 }));
    // Unknown to both phases of the first poll.
    ledger.unknownToNode.set('tx-contract', 2);

    await registry.tick();
    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([]);

    await registry.tick();
    await registry.tick();
    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([{ txHash: 'tx-contract', eventIndex: 2 }]);
  });

  it('a transfer reported under its real index is not reported again when a lagging node then answers "unknown" for longer than the wait', async () => {
    // Validation finding 1 (2026-10-03): the re-read went to the index-0 guess.
    let now = Date.parse('2026-10-03T12:00:00Z');
    const registry = buildRegistry({
      client: ledger.client as never,
      sender: sender.sender,
      clock: () => new Date(now),
    });
    start(registry);
    ledger.add(ledgerRecord({ transaction_id: 'tx-contract', logIndex: 2, block_timestamp: T0 }));
    await registry.tick();

    ledger.unknownToNode.set('tx-contract', Infinity);
    for (let i = 0; i < 3; i += 1) {
      now += LOG_WAIT_MS;
      await registry.tick();
    }

    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([{ txHash: 'tx-contract', eventIndex: 2 }]);
  });

  it('waits per token: a known USDT log in the same transaction does not keep restarting the USDC wait', async () => {
    // Validation finding 2: phase 1's known USDT answer cleared a wait keyed
    // by tx alone and phase 2's unknown USDC restarted it, every poll.
    let now = Date.parse('2026-10-03T12:00:00Z');
    const registry = buildRegistry({
      client: ledger.client as never,
      sender: sender.sender,
      clock: () => new Date(now),
    });
    start(registry);
    ledger.add(
      ledgerRecord({ transaction_id: 'tx-two', logIndex: 1, block_timestamp: T0 }),
      ledgerRecord({
        transaction_id: 'tx-two',
        token_info: { address: USDC, decimals: 6, symbol: 'USDC' },
        value: '7000000',
        logIndex: 3,
        block_timestamp: T0,
      }),
    );
    ledger.unknownToNode.set(`tx-two:${USDC}`, Infinity);

    await registry.tick();
    now += LOG_WAIT_MS;
    await registry.tick();

    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([{ txHash: 'tx-two', eventIndex: 1 }]);
    expect(sentTo(ENDPOINTS.wrongTokenIncoming)).toEqual([{ txHash: 'tx-two', eventIndex: 0 }]);
  });

  it('a waiting record holds back neither the finality checks, the payments after it nor phase 2', async () => {
    // Validation finding 3: the wait aborted the whole poll.
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    ledger.add(ledgerRecord({ transaction_id: 'tx-paid', block_timestamp: T0 }));
    await registry.tick();

    ledger.add(
      ledgerRecord({ transaction_id: 'tx-stuck', logIndex: 2, block_timestamp: T0 + 3000 }),
      ledgerRecord({ transaction_id: 'tx-next', block_timestamp: T0 + 6000 }),
      ledgerRecord({
        transaction_id: 'usdc-next',
        token_info: { address: USDC, decimals: 6, symbol: 'USDC' },
        block_timestamp: T0 + 9000,
      }),
    );
    ledger.unknownToNode.set('tx-stuck', Infinity);
    ledger.solidBlock = 1_000;
    ledger.txInfo.set('tx-paid', { blockNumber: 900 } as never);
    await registry.tick();

    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([
      { txHash: 'tx-paid', eventIndex: 0 },
      { txHash: 'tx-next', eventIndex: 0 },
    ]);
    expect(sentTo(ENDPOINTS.paymentConfirmed)).toEqual([{ txHash: 'tx-paid', eventIndex: 0 }]);
    expect(sentTo(ENDPOINTS.wrongTokenIncoming)).toEqual([{ txHash: 'usdc-next', eventIndex: 0 }]);
  });

  it.each([
    { phase: 'phase 1 (USDT)', token: USDT, endpoint: ENDPOINTS.paymentDetected },
    { phase: 'phase 2 only (USDC)', token: USDC, endpoint: ENDPOINTS.wrongTokenIncoming },
  ])(
    'a HOLDING address whose record waits in $phase is polled again on the next tick, then back on its cadence',
    async ({ token, endpoint }) => {
      // Validation finding 4: nextPollAt had moved a whole holding interval
      // ahead, so the second look came after the wait had run out.
      let now = Date.parse('2026-10-03T12:00:00Z');
      const registry = new MonitorRegistry({
        client: ledger.client as never,
        allowlist: { USDT, USDC },
        intervalMs: 3_000,
        holdingIntervalMs: 900_000,
        minConfirmations: 20,
        pageLimit: 20,
        webhookEndpoints: ENDPOINTS,
        clock: () => new Date(now),
        webhookSender: sender.sender,
      });
      registry.start({
        address: DEPOSIT_ADDRESS,
        paymentAddressId: PAYMENT_ADDRESS_ID,
        transactionId: TRANSACTION_ID,
        expectedContract: USDT,
        expectedSymbol: 'USDT',
        cadence: 'HOLDING',
      });
      ledger.add(
        ledgerRecord({
          transaction_id: 'tx-over',
          token_info: { address: token, decimals: 6 },
          logIndex: 2,
          block_timestamp: T0,
        }),
      );
      // USDT is asked in both phases of the first poll, USDC only in phase 2.
      ledger.unknownToNode.set('tx-over', token === USDT ? 2 : 1);

      await registry.tick();
      now += 3_000;
      await registry.tick();
      expect(sentTo(endpoint)).toEqual([{ txHash: 'tx-over', eventIndex: 2 }]);

      const lists = ledger.listCalls.length;
      now += 3_000;
      await registry.tick();
      expect(ledger.listCalls.length).toBe(lists);
    },
  );

  it('a guess made in phase 2 is remembered: the real index arriving later is not reported again', async () => {
    let now = Date.parse('2026-10-03T12:00:00Z');
    const registry = buildRegistry({
      client: ledger.client as never,
      sender: sender.sender,
      clock: () => new Date(now),
    });
    start(registry);
    ledger.add(
      ledgerRecord({
        transaction_id: 'spam-stuck',
        token_info: { address: SPAM_TOKEN, decimals: 6 },
        logIndex: 2,
        block_timestamp: T0,
      }),
    );
    ledger.unknownToNode.set('spam-stuck', Infinity);

    await registry.tick();
    now += LOG_WAIT_MS;
    await registry.tick();
    expect(sentTo(ENDPOINTS.spamTokenIncoming)).toEqual([{ txHash: 'spam-stuck', eventIndex: 0 }]);

    ledger.unknownToNode.delete('spam-stuck');
    await registry.tick();
    await registry.tick();
    expect(sentTo(ENDPOINTS.spamTokenIncoming)).toEqual([{ txHash: 'spam-stuck', eventIndex: 0 }]);
  });

  it('a token whose transaction the node knows but has no matching log is reported at once — no wait', async () => {
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    ledger.add(
      ledgerRecord({
        transaction_id: 'spam-nolog',
        token_info: { address: SPAM_TOKEN, decimals: 6 },
        noLog: true,
        block_timestamp: T0,
      }),
    );

    await registry.tick();
    await registry.tick();

    expect(sentTo(ENDPOINTS.spamTokenIncoming)).toEqual([{ txHash: 'spam-nolog', eventIndex: 0 }]);
  });

  it('the wait is bounded: after two minutes the transfer is reported under 0, and the real index arriving later is not reported again', async () => {
    let now = Date.parse('2026-10-03T12:00:00Z');
    const registry = buildRegistry({
      client: ledger.client as never,
      sender: sender.sender,
      clock: () => new Date(now),
    });
    start(registry);
    // Same second: the watermark block, so every poll re-reads both.
    ledger.add(
      ledgerRecord({ transaction_id: 'tx-stuck', logIndex: 2, block_timestamp: T0 }),
      ledgerRecord({ transaction_id: 'tx-behind', block_timestamp: T0 + 500 }),
    );
    ledger.unknownToNode.set('tx-stuck', Infinity);

    // The record behind the waiting one does not wait with it.
    await registry.tick();
    now += LOG_WAIT_MS - 1;
    await registry.tick();
    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([{ txHash: 'tx-behind', eventIndex: 0 }]);

    now += 1;
    await registry.tick();
    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([
      { txHash: 'tx-behind', eventIndex: 0 },
      { txHash: 'tx-stuck', eventIndex: 0 },
    ]);

    ledger.unknownToNode.delete('tx-stuck');
    const lookups = ledger.logLookups.length;
    await registry.tick();
    await registry.tick();
    expect(ledger.logLookups.slice(lookups)).toContain('tx-stuck');
    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([
      { txHash: 'tx-behind', eventIndex: 0 },
      { txHash: 'tx-stuck', eventIndex: 0 },
    ]);
  });

  it('a retryable webhook failure is delivered on the next tick, nothing twice', async () => {
    let failures = 1;
    sender = createFakeSender({
      failNext: () =>
        failures-- > 0
          ? new WebhookDeliveryError(503, 'Service Unavailable', 'payment.detected')
          : null,
    });
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    ledger.add(
      ledgerRecord({ transaction_id: 'tx-a', block_timestamp: T0 }),
      ledgerRecord({ transaction_id: 'tx-b', block_timestamp: T0 + 3000 }),
    );

    await registry.tick();
    await registry.tick();
    await registry.tick();

    expect(sentTo(ENDPOINTS.paymentDetected).map((d) => d.txHash)).toEqual(['tx-a', 'tx-b']);
  });

  it('a quiet poll lists once per phase, re-reads only the newest block and asks the node nothing', async () => {
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    for (let i = 0; i < 45; i += 1) {
      ledger.add(
        ledgerRecord({
          transaction_id: `spam-${i}`,
          token_info: { address: SPAM_TOKEN, decimals: 6 },
          block_timestamp: T0 + i * 3000,
        }),
      );
    }
    await registry.tick();
    const lists = ledger.listCalls.length;
    const lookups = ledger.logLookups.length;

    await registry.tick();

    expect(ledger.listCalls.length - lists).toBe(2);
    // The newest block is re-read, but the node's answer for it is kept — not asked again.
    expect(ledger.logLookups.slice(lookups)).toEqual([]);
  });

  it('keeps a separate watermark per phase — a newer USDT transfer does not hide older spam from phase 2', async () => {
    const registry = buildRegistry({ client: ledger.client as never, sender: sender.sender });
    start(registry);
    ledger.add(
      ledgerRecord({
        transaction_id: 'usdc-early',
        token_info: { address: USDC, decimals: 6, symbol: 'USDC' },
        block_timestamp: T0,
      }),
      ledgerRecord({ transaction_id: 'usdt-later', block_timestamp: T0 + 60_000 }),
    );

    await registry.tick();

    expect(sentTo(ENDPOINTS.wrongTokenIncoming)).toEqual([{ txHash: 'usdc-early', eventIndex: 0 }]);
    expect(sentTo(ENDPOINTS.paymentDetected)).toEqual([{ txHash: 'usdt-later', eventIndex: 0 }]);
  });
});
