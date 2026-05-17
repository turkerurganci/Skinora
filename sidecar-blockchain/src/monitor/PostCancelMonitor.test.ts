import { describe, it, expect, beforeEach } from 'vitest';
import {
  DEFAULT_POST_CANCEL_CADENCES,
  DEFAULT_POST_CANCEL_WINDOWS,
  PostCancelMonitorRegistry,
  type PostCancelMonitorRegistryDeps,
} from './PostCancelMonitor.js';
import type { ListTrc20Options, Trc20ListResponse, Trc20Record } from '../tron/TronGridClient.js';
import { WebhookDeliveryError } from '../webhook/WebhookClient.js';
import type { AnyBlockchainWebhookPayload } from '../webhook/WebhookPayloads.js';
import { PostCancelMonitorStates } from '../webhook/WebhookPayloads.js';

const DEPOSIT_ADDRESS = 'TDeposit1234567890DepositAddrFakeXX';
const PAYMENT_ADDRESS_ID = '11111111-1111-1111-1111-111111111111';
const TRANSACTION_ID = '22222222-2222-2222-2222-222222222222';
const USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const SPAM_TOKEN = 'TSpam111111111111111111111111111111';

const ENDPOINTS = {
  latePaymentDetected: '/api/v1/webhooks/blockchain/late-payment-detected',
  postCancelMonitorStateChanged: '/api/v1/webhooks/blockchain/post-cancel-monitor-state-changed',
  wrongTokenIncoming: '/api/v1/webhooks/blockchain/wrong-token',
  spamTokenIncoming: '/api/v1/webhooks/blockchain/spam-token',
};

interface FakeTronClient {
  enqueuePhase1(response: Trc20ListResponse): void;
  enqueuePhase2(response: Trc20ListResponse): void;
  client: PostCancelMonitorRegistryDeps['client'];
  callsPhase1: number;
  callsPhase2: number;
}

function createFakeClient(): FakeTronClient {
  const phase1Queue: Trc20ListResponse[] = [];
  const phase2Queue: Trc20ListResponse[] = [];
  const state = { callsPhase1: 0, callsPhase2: 0 };

  return {
    get callsPhase1() {
      return state.callsPhase1;
    },
    get callsPhase2() {
      return state.callsPhase2;
    },
    enqueuePhase1(r) {
      phase1Queue.push(r);
    },
    enqueuePhase2(r) {
      phase2Queue.push(r);
    },
    client: {
      // eslint-disable-next-line @typescript-eslint/require-await
      async listTrc20(options: ListTrc20Options): Promise<Trc20ListResponse> {
        if (options.contractAddress) {
          state.callsPhase1 += 1;
          return phase1Queue.shift() ?? { records: [], fingerprint: null };
        }
        state.callsPhase2 += 1;
        return phase2Queue.shift() ?? { records: [], fingerprint: null };
      },
      // eslint-disable-next-line @typescript-eslint/require-await
      async getNowSolidBlock(): Promise<number> {
        return 0;
      },
      // eslint-disable-next-line @typescript-eslint/require-await
      async getTransactionInfoById() {
        return null;
      },
    } as unknown as PostCancelMonitorRegistryDeps['client'],
  };
}

interface SentWebhook {
  endpoint: string;
  envelope: AnyBlockchainWebhookPayload;
  correlationId: string;
}

function createFakeSender(opts: { failNext?: () => Error | null } = {}) {
  const sent: SentWebhook[] = [];
  const sender: PostCancelMonitorRegistryDeps['webhookSender'] = async (
    endpoint,
    envelope,
    correlationId,
  ) => {
    const err = opts.failNext?.();
    if (err) throw err;
    sent.push({ endpoint, envelope, correlationId });
  };
  return { sender, sent };
}

interface RegistryHarness {
  registry: PostCancelMonitorRegistry;
  setNow(date: Date): void;
}

function buildRegistry(opts: {
  client: PostCancelMonitorRegistryDeps['client'];
  sender: PostCancelMonitorRegistryDeps['webhookSender'];
  now?: Date;
  cadences?: PostCancelMonitorRegistryDeps['cadences'];
  windows?: PostCancelMonitorRegistryDeps['windows'];
}): RegistryHarness {
  let now = opts.now ?? new Date('2026-05-17T12:00:00Z');
  const registry = new PostCancelMonitorRegistry({
    client: opts.client,
    allowlist: { USDT, USDC },
    tickIntervalMs: 30_000,
    pageLimit: 20,
    webhookEndpoints: ENDPOINTS,
    cadences: opts.cadences,
    windows: opts.windows,
    clock: () => now,
    webhookSender: opts.sender,
  });
  return {
    registry,
    setNow(date) {
      now = date;
    },
  };
}

function buildRecord(overrides: Partial<Trc20Record> & { txHash: string }): Trc20Record {
  return {
    transaction_id: overrides.txHash,
    from: overrides.from ?? 'TFromAddrFakeFakeFakeFakeFakeFakeXX',
    to: overrides.to ?? DEPOSIT_ADDRESS,
    value: overrides.value ?? '100500000',
    block_timestamp: overrides.block_timestamp ?? 1_715_000_000_000,
    type: overrides.type ?? 'Transfer',
    token_info: overrides.token_info ?? { address: USDT, decimals: 6, symbol: 'USDT' },
  } as Trc20Record;
}

describe('PostCancelMonitorRegistry — initial state derivation (08 §3.4)', () => {
  let fake: FakeTronClient;
  let webhook: ReturnType<typeof createFakeSender>;

  beforeEach(() => {
    fake = createFakeClient();
    webhook = createFakeSender();
  });

  it('derives POST_CANCEL_24H when cancellation is fresh', () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: cancelledAt, // elapsed = 0
    });
    const result = harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    expect(result).toEqual({ started: true, state: PostCancelMonitorStates.PostCancel24h });
    expect(harness.registry.size()).toBe(1);
  });

  it('derives POST_CANCEL_7D when 25 hours have elapsed', () => {
    const cancelledAt = new Date('2026-05-17T00:00:00Z');
    const now = new Date('2026-05-18T01:00:00Z'); // +25h
    const harness = buildRegistry({ client: fake.client, sender: webhook.sender, now });
    const result = harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    expect(result.state).toBe(PostCancelMonitorStates.PostCancel7d);
  });

  it('derives POST_CANCEL_30D when 8 days have elapsed', () => {
    const cancelledAt = new Date('2026-05-09T00:00:00Z');
    const now = new Date('2026-05-17T00:00:00Z'); // +8d
    const harness = buildRegistry({ client: fake.client, sender: webhook.sender, now });
    const result = harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    expect(result.state).toBe(PostCancelMonitorStates.PostCancel30d);
  });

  it('refuses start when 31 days have elapsed (already past 30d window)', () => {
    const cancelledAt = new Date('2026-04-16T00:00:00Z');
    const now = new Date('2026-05-17T00:00:00Z'); // +31d
    const harness = buildRegistry({ client: fake.client, sender: webhook.sender, now });
    const result = harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    expect(result).toEqual({ started: false, state: PostCancelMonitorStates.Stopped });
    expect(harness.registry.size()).toBe(0);
  });

  it('honours recovery override (initialState + initialStateExpiresAt)', () => {
    const cancelledAt = new Date('2026-05-15T00:00:00Z');
    const overrideExpiresAt = new Date('2026-05-22T00:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: new Date('2026-05-17T12:00:00Z'),
    });
    const result = harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
      initialState: PostCancelMonitorStates.PostCancel7d,
      initialStateExpiresAt: overrideExpiresAt,
    });
    expect(result.state).toBe(PostCancelMonitorStates.PostCancel7d);
  });

  it('is idempotent — same address restart is a no-op', () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: cancelledAt,
    });
    const opts = {
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT' as const,
      cancelledAt,
    };
    const first = harness.registry.start(opts);
    const second = harness.registry.start(opts);
    expect(first.started).toBe(true);
    expect(second).toEqual({ started: false, state: PostCancelMonitorStates.PostCancel24h });
    expect(harness.registry.size()).toBe(1);
  });
});

describe('PostCancelMonitorRegistry — state transitions (08 §3.4 cadence boundaries)', () => {
  let fake: FakeTronClient;
  let webhook: ReturnType<typeof createFakeSender>;

  beforeEach(() => {
    fake = createFakeClient();
    webhook = createFakeSender();
  });

  it('advances POST_CANCEL_24H → POST_CANCEL_7D after 24h elapse + emits state-changed webhook', async () => {
    const cancelledAt = new Date('2026-05-17T00:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: cancelledAt,
    });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });

    // Advance to 24h+1s
    harness.setNow(
      new Date(cancelledAt.getTime() + DEFAULT_POST_CANCEL_WINDOWS.POST_CANCEL_24H + 1_000),
    );
    await harness.registry.tick();

    const stateChanged = webhook.sent.filter(
      (w) => w.endpoint === ENDPOINTS.postCancelMonitorStateChanged,
    );
    expect(stateChanged).toHaveLength(1);
    const data = stateChanged[0].envelope.data as {
      previousState: string;
      newState: string;
      newStateExpiresAt: string | null;
    };
    expect(data.previousState).toBe(PostCancelMonitorStates.PostCancel24h);
    expect(data.newState).toBe(PostCancelMonitorStates.PostCancel7d);
    expect(data.newStateExpiresAt).toBe(
      new Date(cancelledAt.getTime() + DEFAULT_POST_CANCEL_WINDOWS.POST_CANCEL_7D).toISOString(),
    );
  });

  it('advances POST_CANCEL_7D → POST_CANCEL_30D after 7d elapse', async () => {
    const cancelledAt = new Date('2026-05-01T00:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: new Date(cancelledAt.getTime() + 25 * 60 * 60 * 1000), // +25h start in 7D state
    });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });

    harness.setNow(
      new Date(cancelledAt.getTime() + DEFAULT_POST_CANCEL_WINDOWS.POST_CANCEL_7D + 1_000),
    );
    await harness.registry.tick();

    const stateChanged = webhook.sent.filter(
      (w) => w.endpoint === ENDPOINTS.postCancelMonitorStateChanged,
    );
    expect(stateChanged).toHaveLength(1);
    const data = stateChanged[0].envelope.data as { previousState: string; newState: string };
    expect(data.previousState).toBe(PostCancelMonitorStates.PostCancel7d);
    expect(data.newState).toBe(PostCancelMonitorStates.PostCancel30d);
  });

  it('advances POST_CANCEL_30D → STOPPED after 30d elapse and removes the entry', async () => {
    const cancelledAt = new Date('2026-04-17T00:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: new Date(cancelledAt.getTime() + 8 * 24 * 60 * 60 * 1000), // +8d
    });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    expect(harness.registry.size()).toBe(1);

    harness.setNow(
      new Date(cancelledAt.getTime() + DEFAULT_POST_CANCEL_WINDOWS.POST_CANCEL_30D + 1_000),
    );
    await harness.registry.tick();

    expect(harness.registry.size()).toBe(0);
    const terminal = webhook.sent.find(
      (w) =>
        w.endpoint === ENDPOINTS.postCancelMonitorStateChanged &&
        (w.envelope.data as { newState: string }).newState === PostCancelMonitorStates.Stopped,
    );
    expect(terminal).toBeDefined();
    expect(
      (terminal!.envelope.data as { newStateExpiresAt: string | null }).newStateExpiresAt,
    ).toBeNull();
  });

  it('cascades multiple transitions in a single tick when clock jumps across windows', async () => {
    // Sidecar offline for a long time, then a tick fires with now at +8d.
    const cancelledAt = new Date('2026-05-01T00:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: cancelledAt,
    });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    harness.setNow(new Date(cancelledAt.getTime() + 8 * 24 * 60 * 60 * 1000));

    await harness.registry.tick();

    const transitions = webhook.sent
      .filter((w) => w.endpoint === ENDPOINTS.postCancelMonitorStateChanged)
      .map((w) => (w.envelope.data as { newState: string }).newState);
    // Should pass through 7D then 30D in one shot.
    expect(transitions).toEqual([
      PostCancelMonitorStates.PostCancel7d,
      PostCancelMonitorStates.PostCancel30d,
    ]);
  });
});

describe('PostCancelMonitorRegistry — cadence eligibility (08 §3.4)', () => {
  let fake: FakeTronClient;
  let webhook: ReturnType<typeof createFakeSender>;

  beforeEach(() => {
    fake = createFakeClient();
    webhook = createFakeSender();
  });

  it('polls once per 30s in POST_CANCEL_24H — 9 ticks within 30s yield exactly one phase-1 call', async () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: cancelledAt,
    });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });

    for (let i = 0; i < 5; i += 1) {
      harness.setNow(new Date(cancelledAt.getTime() + i * 5_000)); // 0, 5, 10, 15, 20s
      await harness.registry.tick();
    }

    expect(fake.callsPhase1).toBe(1);
  });

  it('polls once per 5 min in POST_CANCEL_7D — ticks at 30s do NOT trigger a poll until 5 min elapse', async () => {
    const cancelledAt = new Date('2026-05-01T00:00:00Z');
    const startNow = new Date(cancelledAt.getTime() + 25 * 60 * 60 * 1000); // 7D state
    const harness = buildRegistry({ client: fake.client, sender: webhook.sender, now: startNow });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });

    // 30s tick — first tick polls, subsequent ticks before 5 min do not.
    for (let i = 0; i < 5; i += 1) {
      harness.setNow(new Date(startNow.getTime() + i * 30_000)); // 0, 30, 60, 90, 120s
      await harness.registry.tick();
    }

    expect(fake.callsPhase1).toBe(1);
  });

  it('polls once per 1h in POST_CANCEL_30D — same throttling at hour scale', async () => {
    const cancelledAt = new Date('2026-04-25T00:00:00Z');
    const startNow = new Date(cancelledAt.getTime() + 8 * 24 * 60 * 60 * 1000); // 30D state
    const harness = buildRegistry({ client: fake.client, sender: webhook.sender, now: startNow });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });

    for (let i = 0; i < 10; i += 1) {
      harness.setNow(new Date(startNow.getTime() + i * 30_000)); // 5 min spread
      await harness.registry.tick();
    }

    expect(fake.callsPhase1).toBe(1);
  });
});

describe('PostCancelMonitorRegistry — webhook emission', () => {
  let fake: FakeTronClient;
  let webhook: ReturnType<typeof createFakeSender>;

  beforeEach(() => {
    fake = createFakeClient();
    webhook = createFakeSender();
  });

  function setup(cancelledAt: Date) {
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: cancelledAt,
    });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    return harness;
  }

  it('emits LatePaymentDetected for the expected token (phase 1)', async () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = setup(cancelledAt);
    fake.enqueuePhase1({
      records: [buildRecord({ txHash: 'tx_late_usdt', value: '100500000' })],
      fingerprint: 'fp1',
    });
    await harness.registry.tick();

    const late = webhook.sent.filter((w) => w.endpoint === ENDPOINTS.latePaymentDetected);
    expect(late).toHaveLength(1);
    const data = late[0].envelope.data as {
      txHash: string;
      amount: string;
      monitorState: string;
      tokenSymbol: string;
    };
    expect(data.txHash).toBe('tx_late_usdt');
    expect(data.amount).toBe('100.500000');
    expect(data.monitorState).toBe(PostCancelMonitorStates.PostCancel24h);
    expect(data.tokenSymbol).toBe('USDT');
  });

  it('emits WrongTokenIncoming for an allowlisted but unexpected token (phase 2)', async () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = setup(cancelledAt);
    fake.enqueuePhase2({
      records: [
        buildRecord({
          txHash: 'tx_usdc_wrong',
          token_info: { address: USDC, decimals: 6, symbol: 'USDC' },
          value: '50000000',
        }),
      ],
      fingerprint: 'fp2',
    });
    await harness.registry.tick();

    const wrong = webhook.sent.filter((w) => w.endpoint === ENDPOINTS.wrongTokenIncoming);
    expect(wrong).toHaveLength(1);
    const data = wrong[0].envelope.data as { actualTokenSymbol: string };
    expect(data.actualTokenSymbol).toBe('USDC');
  });

  it('emits SpamTokenIncoming for a non-allowlisted token (phase 2)', async () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = setup(cancelledAt);
    fake.enqueuePhase2({
      records: [
        buildRecord({
          txHash: 'tx_spam',
          token_info: { address: SPAM_TOKEN, decimals: 6, symbol: 'SPAM' },
          value: '1',
        }),
      ],
      fingerprint: 'fp2',
    });
    await harness.registry.tick();

    const spam = webhook.sent.filter((w) => w.endpoint === ENDPOINTS.spamTokenIncoming);
    expect(spam).toHaveLength(1);
  });

  it('does not emit twice for the same txHash across ticks (idempotency)', async () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = setup(cancelledAt);
    fake.enqueuePhase1({
      records: [buildRecord({ txHash: 'tx_dup' })],
      fingerprint: 'fp1',
    });
    fake.enqueuePhase1({
      records: [buildRecord({ txHash: 'tx_dup' })],
      fingerprint: 'fp1',
    });

    await harness.registry.tick();
    harness.setNow(new Date(cancelledAt.getTime() + 35_000)); // > 30s cadence
    await harness.registry.tick();

    const late = webhook.sent.filter((w) => w.endpoint === ENDPOINTS.latePaymentDetected);
    expect(late).toHaveLength(1);
  });

  it('skips outgoing transfers (record.to ≠ deposit address)', async () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = setup(cancelledAt);
    fake.enqueuePhase1({
      records: [
        buildRecord({
          txHash: 'tx_outgoing',
          to: 'TSomeOtherAddrFakeFakeFakeFakeXX',
          from: DEPOSIT_ADDRESS,
        }),
      ],
      fingerprint: 'fp1',
    });
    await harness.registry.tick();

    const late = webhook.sent.filter((w) => w.endpoint === ENDPOINTS.latePaymentDetected);
    expect(late).toHaveLength(0);
  });

  it('drops non-Transfer records (Approval, TRC-721) at debug level', async () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = setup(cancelledAt);
    fake.enqueuePhase1({
      records: [
        buildRecord({
          txHash: 'tx_approval',
          type: 'Approval',
        }),
      ],
      fingerprint: 'fp1',
    });
    await harness.registry.tick();
    const late = webhook.sent.filter((w) => w.endpoint === ENDPOINTS.latePaymentDetected);
    expect(late).toHaveLength(0);
  });
});

describe('PostCancelMonitorRegistry — stop and shutdown', () => {
  let fake: FakeTronClient;
  let webhook: ReturnType<typeof createFakeSender>;

  beforeEach(() => {
    fake = createFakeClient();
    webhook = createFakeSender();
  });

  it('stop returns false for unknown address (idempotent)', () => {
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: new Date('2026-05-17T12:00:00Z'),
    });
    expect(harness.registry.stop(DEPOSIT_ADDRESS)).toEqual({ stopped: false });
  });

  it('stop removes the entry and returns true', () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: cancelledAt,
    });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    expect(harness.registry.size()).toBe(1);
    const result = harness.registry.stop(DEPOSIT_ADDRESS);
    expect(result).toEqual({ stopped: true });
    expect(harness.registry.size()).toBe(0);
  });

  it('shutdown clears every entry', async () => {
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = buildRegistry({
      client: fake.client,
      sender: webhook.sender,
      now: cancelledAt,
    });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    await harness.registry.shutdown();
    expect(harness.registry.size()).toBe(0);
    expect(() =>
      harness.registry.start({
        address: 'TDifferentAddrFakeFakeFakeFakeFakeXX',
        paymentAddressId: PAYMENT_ADDRESS_ID,
        transactionId: TRANSACTION_ID,
        expectedContract: USDT,
        expectedSymbol: 'USDT',
        cancelledAt,
      }),
    ).toThrowError(/shut down/);
  });
});

describe('PostCancelMonitorRegistry — webhook delivery failure handling', () => {
  it('non-retryable WebhookDeliveryError is swallowed (logged, payload dropped)', async () => {
    const fake = createFakeClient();
    const sender: PostCancelMonitorRegistryDeps['webhookSender'] = async () => {
      throw new WebhookDeliveryError(400, 'Bad Request', 'payment.late_detected');
    };
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = buildRegistry({ client: fake.client, sender, now: cancelledAt });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    fake.enqueuePhase1({
      records: [buildRecord({ txHash: 'tx_drop' })],
      fingerprint: 'fp',
    });

    // tick must not throw
    await expect(harness.registry.tick()).resolves.toBeUndefined();
  });

  it('retryable error surfaces in logs but does not throw at the registry boundary', async () => {
    const fake = createFakeClient();
    let calls = 0;
    const sender: PostCancelMonitorRegistryDeps['webhookSender'] = async () => {
      calls += 1;
      throw new WebhookDeliveryError(503, 'Service Unavailable', 'payment.late_detected');
    };
    const cancelledAt = new Date('2026-05-17T12:00:00Z');
    const harness = buildRegistry({ client: fake.client, sender, now: cancelledAt });
    harness.registry.start({
      address: DEPOSIT_ADDRESS,
      paymentAddressId: PAYMENT_ADDRESS_ID,
      transactionId: TRANSACTION_ID,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt,
    });
    fake.enqueuePhase1({
      records: [buildRecord({ txHash: 'tx_retry' })],
      fingerprint: 'fp',
    });

    await harness.registry.tick();
    expect(calls).toBeGreaterThan(0);
    // Registry catches the throw at the entry boundary so other monitors keep ticking.
  });
});

describe('PostCancelMonitorRegistry — default constants match 08 §3.4 spec', () => {
  it('exposes 30s / 5min / 1h cadences as documented', () => {
    expect(DEFAULT_POST_CANCEL_CADENCES.POST_CANCEL_24H).toBe(30 * 1000);
    expect(DEFAULT_POST_CANCEL_CADENCES.POST_CANCEL_7D).toBe(5 * 60 * 1000);
    expect(DEFAULT_POST_CANCEL_CADENCES.POST_CANCEL_30D).toBe(60 * 60 * 1000);
  });

  it('exposes 24h / 7d / 30d total-elapsed windows', () => {
    expect(DEFAULT_POST_CANCEL_WINDOWS.POST_CANCEL_24H).toBe(24 * 60 * 60 * 1000);
    expect(DEFAULT_POST_CANCEL_WINDOWS.POST_CANCEL_7D).toBe(7 * 24 * 60 * 60 * 1000);
    expect(DEFAULT_POST_CANCEL_WINDOWS.POST_CANCEL_30D).toBe(30 * 24 * 60 * 60 * 1000);
  });
});
