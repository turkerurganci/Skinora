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

const DEPOSIT_ADDRESS = 'TDeposit1234567890DepositAddrFakeXX';
const PAYMENT_ADDRESS_ID = '11111111-1111-1111-1111-111111111111';
const TRANSACTION_ID = '22222222-2222-2222-2222-222222222222';
const USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const SPAM_TOKEN = 'TSpam111111111111111111111111111111';

type ListCall = {
  contractAddress?: string;
  fingerprint?: string;
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
          });
          return phase1Queue.shift() ?? { records: [], fingerprint: null };
        }
        callsPhase2.push({ fingerprint: options.fingerprint });
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
    clock: () => fixedNow,
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

  it('falls back to index 0 when the solidity node has no logs yet (no regression)', async () => {
    const registry = buildRegistry({ client: fake.client, sender: sender.sender });
    startMonitor(registry);
    // No setEventIndices → resolver returns [] → status-quo single-event index 0.
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
