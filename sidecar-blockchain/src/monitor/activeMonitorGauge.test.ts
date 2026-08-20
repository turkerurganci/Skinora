import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { activeMonitors } from '../metrics.js';
import { reportActiveMonitorCount, resetActiveMonitorCounts } from './activeMonitorGauge.js';
import { MonitorRegistry, type MonitorRegistryDeps } from './MonitorRegistry.js';
import {
  PostCancelMonitorRegistry,
  type PostCancelMonitorRegistryDeps,
} from './PostCancelMonitor.js';
import type { TronGridClient } from '../tron/TronGridClient.js';

const USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const ACTIVE_ADDRESS = 'TActiveDepositAddrFakeFakeFakeFake1';
const POST_CANCEL_ADDRESS = 'TPostCancelDepositAddrFakeFakeFake1';
const PAYMENT_ADDRESS_ID = '11111111-1111-1111-1111-111111111111';
const TRANSACTION_ID = '22222222-2222-2222-2222-222222222222';

// Neither start(), stop() nor shutdown() touches the TronGrid client — only
// tick() does, and these tests never tick.
const UNUSED_CLIENT = {} as unknown as TronGridClient;

async function gaugeValue(): Promise<number> {
  const metric = await activeMonitors.get();
  return metric.values[0]?.value ?? 0;
}

function buildActiveRegistry(): MonitorRegistry {
  const deps: MonitorRegistryDeps = {
    client: UNUSED_CLIENT,
    allowlist: { USDT, USDC },
    intervalMs: 3_600_000, // long — nothing may tick during these tests
    minConfirmations: 20,
    pageLimit: 20,
    webhookEndpoints: {
      paymentDetected: '/payment-detected',
      paymentConfirmed: '/payment-confirmed',
      wrongTokenIncoming: '/wrong-token',
      spamTokenIncoming: '/spam-token',
    },
  };
  return new MonitorRegistry(deps);
}

function buildPostCancelRegistry(): PostCancelMonitorRegistry {
  const deps: PostCancelMonitorRegistryDeps = {
    client: UNUSED_CLIENT,
    allowlist: { USDT, USDC },
    tickIntervalMs: 3_600_000,
    pageLimit: 20,
    webhookEndpoints: {
      latePaymentDetected: '/late-payment-detected',
      postCancelMonitorStateChanged: '/post-cancel-monitor-state-changed',
      wrongTokenIncoming: '/wrong-token',
      spamTokenIncoming: '/spam-token',
    },
  };
  return new PostCancelMonitorRegistry(deps);
}

/**
 * T139 doğrulaması, bulgu N2 (tur 2). `skinora_blockchain_active_monitors`
 * carries no labels and both registries wrote it with a bare `.set(size)`, so
 * the exported value was whichever registry wrote last. T139 made that number
 * load-bearing (08 §3.4 capacity planning, DEPLOY_RUNBOOK §G.4 arm proof, the
 * integration-metrics Grafana panel, and T139-ActiveMonitorQuotaAlarm), so the
 * gauge now has to be the total.
 */
describe('activeMonitorGauge', () => {
  beforeEach(() => {
    resetActiveMonitorCounts();
  });

  afterEach(() => {
    resetActiveMonitorCounts();
  });

  it('publishes the sum across both registries, not the last writer', async () => {
    reportActiveMonitorCount('active', 7);
    reportActiveMonitorCount('post_cancel', 5);

    expect(await gaugeValue()).toBe(12);
  });

  it('lets each source move independently', async () => {
    reportActiveMonitorCount('active', 3);
    reportActiveMonitorCount('post_cancel', 4);
    reportActiveMonitorCount('active', 1);

    expect(await gaugeValue()).toBe(5);
  });

  it('keeps the other source when one drops to zero', async () => {
    reportActiveMonitorCount('active', 2);
    reportActiveMonitorCount('post_cancel', 6);
    reportActiveMonitorCount('active', 0);

    expect(await gaugeValue()).toBe(6);
  });

  it('counts a live monitor from each registry exactly once', async () => {
    const active = buildActiveRegistry();
    const postCancel = buildPostCancelRegistry();

    try {
      active.start({
        address: ACTIVE_ADDRESS,
        paymentAddressId: PAYMENT_ADDRESS_ID,
        transactionId: TRANSACTION_ID,
        expectedContract: USDT,
        expectedSymbol: 'USDT',
      });
      expect(await gaugeValue()).toBe(1);

      postCancel.start({
        address: POST_CANCEL_ADDRESS,
        paymentAddressId: PAYMENT_ADDRESS_ID,
        transactionId: TRANSACTION_ID,
        expectedContract: USDT,
        expectedSymbol: 'USDT',
        cancelledAt: new Date('2026-08-20T09:00:00Z'),
      });

      // Before the fix this read 1: the post-cancel registry's `.set(1)`
      // overwrote the active registry's `.set(1)`.
      expect(await gaugeValue()).toBe(2);
    } finally {
      await active.shutdown();
      await postCancel.shutdown();
    }
  });

  it('does not zero the gauge when only one registry shuts down', async () => {
    const active = buildActiveRegistry();
    const postCancel = buildPostCancelRegistry();

    try {
      active.start({
        address: ACTIVE_ADDRESS,
        paymentAddressId: PAYMENT_ADDRESS_ID,
        transactionId: TRANSACTION_ID,
        expectedContract: USDT,
        expectedSymbol: 'USDT',
      });
      postCancel.start({
        address: POST_CANCEL_ADDRESS,
        paymentAddressId: PAYMENT_ADDRESS_ID,
        transactionId: TRANSACTION_ID,
        expectedContract: USDT,
        expectedSymbol: 'USDT',
        cancelledAt: new Date('2026-08-20T09:00:00Z'),
      });

      await postCancel.shutdown();

      // Before the fix `shutdown()` published a flat 0 while the active
      // registry was still polling its address.
      expect(await gaugeValue()).toBe(1);
    } finally {
      await active.shutdown();
    }
  });
});
