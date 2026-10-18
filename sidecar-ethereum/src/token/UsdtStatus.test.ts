import { describe, expect, it } from 'vitest';
import { UsdtStatusMonitor, usdtStateOf } from './UsdtStatus.js';
import { paymentReadiness } from '../health/paymentReadiness.js';
import { USDT, monitorHarness } from '../testing/monitorHarness.js';

/** USDT fee / pause / deprecation switches (T151 — 08 §3.7.1) and the acceptsPayments flag. */

describe('usdtStateOf', () => {
  const off = { basisPointsRate: 0n, maximumFee: 0n, paused: false, deprecated: false };
  it.each([
    ['all switches off', off, 'ok'],
    ['a fee rate', { ...off, basisPointsRate: 1n }, 'fee_enabled'],
    ['a maximum fee', { ...off, maximumFee: 1n }, 'fee_enabled'],
    ['paused', { ...off, paused: true }, 'paused'],
    ['deprecated (wins over paused)', { ...off, paused: true, deprecated: true }, 'deprecated'],
  ])('%s → %s', (_label, switches, state) => {
    expect(usdtStateOf(switches)).toBe(state);
  });
});

describe('UsdtStatusMonitor — reads the contract at the finalized block', () => {
  function monitor(contract = USDT) {
    const h = monitorHarness();
    h.chain.mineTo(50n);
    h.chain.finalize(40n);
    return {
      h,
      m: new UsdtStatusMonitor({ rpc: h.rpc, contract, finality: h.finality, intervalMs: 600_000 }),
    };
  }

  it('is unknown before the first read, ok after it when every switch is off', async () => {
    const { h, m } = monitor();
    expect(m.status().state).toBe('unknown');
    await m.checkOnce();
    expect(m.status()).toMatchObject({ state: 'ok', block: 40n });
    const calls = h.chain.calls.filter((c) => c.method === 'eth_call');
    expect(calls.map((c) => c.params[1])).toEqual(['0x28', '0x28', '0x28', '0x28']);
  });

  it.each([
    [{ paused: true }, 'paused'],
    [{ deprecated: true }, 'deprecated'],
    [{ basisPointsRate: 10n }, 'fee_enabled'],
  ])('%o → %s', async (switches, state) => {
    const { h, m } = monitor();
    h.chain.setSwitches(USDT, switches);
    await m.checkOnce();
    expect(m.status().state).toBe(state);
  });

  it('a failed first read stays unknown (closed); a later failure keeps the last known state', async () => {
    const { h, m } = monitor();
    h.chain.down = true;
    await m.checkOnce();
    expect(m.status().state).toBe('unknown');
    h.chain.down = false;
    await m.checkOnce();
    h.chain.down = true;
    await m.checkOnce();
    expect(m.status().state).toBe('ok');
    expect(m.status().lastError).not.toBeNull();
  });

  it('is not_configured without a USDT contract and never reads', async () => {
    const { h, m } = monitor('');
    await m.checkOnce();
    expect(m.status().state).toBe('not_configured');
    expect(h.chain.calls).toEqual([]);
  });
});

describe('paymentReadiness — the acceptsPayments flag (T151 kod düzeyi kapı)', () => {
  it('accepts only with the monitor running, the webhook secret set and USDT ok', () => {
    expect(paymentReadiness({ monitorRunning: true, webhookConfigured: true, usdt: 'ok' })).toEqual(
      {
        acceptsPayments: true,
        reasons: [],
        usdt: 'ok',
      },
    );
  });

  it.each([
    [
      { monitorRunning: false, webhookConfigured: true, usdt: 'ok' as const },
      'MONITOR_NOT_RUNNING',
    ],
    [
      { monitorRunning: true, webhookConfigured: false, usdt: 'ok' as const },
      'WEBHOOK_SECRET_MISSING',
    ],
    [{ monitorRunning: true, webhookConfigured: true, usdt: 'paused' as const }, 'USDT_PAUSED'],
    [
      { monitorRunning: true, webhookConfigured: true, usdt: 'deprecated' as const },
      'USDT_DEPRECATED',
    ],
    [
      { monitorRunning: true, webhookConfigured: true, usdt: 'fee_enabled' as const },
      'USDT_FEE_ENABLED',
    ],
    [{ monitorRunning: true, webhookConfigured: true, usdt: 'unknown' as const }, 'USDT_UNKNOWN'],
    [
      { monitorRunning: true, webhookConfigured: true, usdt: 'not_configured' as const },
      'USDT_NOT_CONFIGURED',
    ],
  ])('%o → not accepting (%s)', (input, reason) => {
    const r = paymentReadiness(input);
    expect(r.acceptsPayments).toBe(false);
    expect(r.reasons).toContain(reason);
  });
});
