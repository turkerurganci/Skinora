import { spawn } from 'node:child_process';
import { resolve } from 'node:path';
import { describe, it, expect } from 'vitest';

/**
 * The entry point, started the way production starts it (environment only),
 * read back from its own startup line.
 *
 * <para>
 * composeWiring.test.ts pins the first link: .env → compose → this container.
 * The two after it had no test. Reading STAKE_ACCOUNT_ADDRESS or
 * STAKE_ACCOUNT_PERMISSION_ID under a different name, index.ts not handing
 * either to the delegation flow, or index.ts giving the refund estimate some
 * other stake source — each left all 355 tests green (#325 re-validation, 5 of
 * 45 mutations). Deployed that way the sidecar quietly keeps the pre-split
 * arrangement and burns every sweep and refund while the stake sits idle.
 * </para>
 */
const SIDECAR_ROOT = resolve(__dirname, '../..');
const TSX_CLI = resolve(SIDECAR_ROOT, 'node_modules/tsx/dist/cli.mjs');
const HOT_WALLET = 'TP6e9Yqa1wFFDbJzKaSgTwBq2LHax9YSFD';
/** The Nile stake account measured 2026-09-18 — a real address, as the flow requires. */
const STAKE_ACCOUNT = 'TVqvXQhRcmcCQudYx6uhq1WWuNHVmEEyQ6';
const STARTUP_MESSAGE = 'Energy delegation:';

interface Startup {
  /** The parsed startup line, if the process got that far. */
  line?: Record<string, unknown>;
  exitCode?: number | null;
  output: string;
}

function start(env: Record<string, string>, message: string = STARTUP_MESSAGE): Promise<Startup> {
  return new Promise((done) => {
    const child = spawn(process.execPath, [TSX_CLI, 'src/index.ts'], {
      cwd: SIDECAR_ROOT,
      env: {
        ...process.env,
        NODE_ENV: 'test',
        PORT: '0',
        LOKI_URL: 'http://127.0.0.1:9',
        HOT_WALLET_ADDRESS: HOT_WALLET,
        HOT_WALLET_PRIVATE_KEY: '',
        COLD_WALLET_ADDRESS: '',
        STAKE_ACCOUNT_ADDRESS: '',
        STAKE_ACCOUNT_PERMISSION_ID: '',
        ENERGY_STAKING_ENABLED: '',
        ...env,
      },
    });
    let output = '';
    let settled = false;
    const finish = (result: Startup) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      child.kill();
      done(result);
    };
    const timer = setTimeout(() => finish({ output }), 20_000);
    child.stdout.on('data', (chunk: Buffer) => {
      output += chunk.toString();
      for (const raw of output.split('\n')) {
        if (!raw.includes(message)) continue;
        try {
          finish({ line: JSON.parse(raw) as Record<string, unknown>, output });
        } catch {
          // The line has not fully arrived yet.
        }
      }
    });
    child.stderr.on('data', (chunk: Buffer) => (output += chunk.toString()));
    child.on('close', (code) => finish({ exitCode: code, output }));
  });
}

describe('sidecar startup — the stake account reaches both services index.ts builds', () => {
  it('delegates from the stake account under its permission id, and the refund estimate reads the same account', async () => {
    // A non-default id, so an id that never arrives (default 2) shows.
    const startup = await start({
      ENERGY_STAKING_ENABLED: 'true',
      STAKE_ACCOUNT_ADDRESS: STAKE_ACCOUNT,
      STAKE_ACCOUNT_PERMISSION_ID: '5',
    });

    expect(startup.line, startup.output).toMatchObject({
      stakingEnabled: true,
      delegationOwner: STAKE_ACCOUNT,
      permissionId: 5,
      refundEstimateReadsStakeFrom: STAKE_ACCOUNT,
    });
  }, 30_000);

  it('keeps the hot wallet as its own stake holder when no stake account is set', async () => {
    const startup = await start({ ENERGY_STAKING_ENABLED: 'true' });

    expect(startup.line, startup.output).toMatchObject({
      stakingEnabled: true,
      delegationOwner: HOT_WALLET,
      permissionId: null,
      refundEstimateReadsStakeFrom: HOT_WALLET,
    });
  }, 30_000);

  it('refuses to start on a stake address the flow cannot use', async () => {
    const startup = await start({
      ENERGY_STAKING_ENABLED: 'true',
      STAKE_ACCOUNT_ADDRESS: 'TStakeAccountHoldingTheFrozenTrx',
    });

    expect(startup.line).toBeUndefined();
    // Exited on its own — a process still running at the timeout has no code.
    expect(startup.exitCode ?? 0).toBeGreaterThan(0);
    expect(startup.output).toContain('STAKE_ACCOUNT_MISCONFIGURED');
  }, 30_000);
});

/**
 * T149 (11 §F8, öneriyle kaydedildi — ⚠) — the hybrid stake is dormant behind
 * ENERGY_STAKING_ENABLED, default OFF. Started the way production starts it,
 * the process must report it off when the variable is unset — even with a
 * stake account left in .env — and refuse a value it cannot read.
 */
describe('sidecar startup — the dormant stake switch', () => {
  it('is OFF when ENERGY_STAKING_ENABLED is unset, and a configured stake account is then ignored', async () => {
    const startup = await start({
      STAKE_ACCOUNT_ADDRESS: STAKE_ACCOUNT,
      STAKE_ACCOUNT_PERMISSION_ID: '5',
    });

    expect(startup.line, startup.output).toMatchObject({
      stakingEnabled: false,
      delegationOwner: HOT_WALLET,
      permissionId: null,
    });
    expect(String(startup.line?.message ?? startup.output)).toContain('OFF');
  }, 30_000);

  it('starts with a leftover malformed stake account while the switch is off', async () => {
    const startup = await start({ STAKE_ACCOUNT_ADDRESS: 'TStakeAccountHoldingTheFrozenTrx' });

    expect(startup.line, startup.output).toMatchObject({ stakingEnabled: false });
  }, 30_000);

  it('refuses to start on a switch value it cannot read', async () => {
    const startup = await start({ ENERGY_STAKING_ENABLED: 'yes' });

    expect(startup.line).toBeUndefined();
    expect(startup.exitCode ?? 0).toBeGreaterThan(0);
    expect(startup.output).toContain('ENERGY_STAKING_ENABLED');
  }, 30_000);
});

/**
 * T139-ActiveMonitorQuotaAlarm — the holding cadence and the TronGrid budget,
 * read back from the MonitorRegistry and the budget gauge of the running process.
 * composeWiring.test.ts pins .env → compose; this pins env → the objects.
 */
describe('sidecar startup — the polling cadences and the quota budget reach the process', () => {
  const MONITORING_MESSAGE = 'Payment monitoring:';

  it('polls a confirmed payment at the configured holding interval and publishes the configured budget', async () => {
    // Non-default values, so a variable read under another name (or not read
    // at all) shows up as its default.
    const startup = await start(
      {
        PAYMENT_HOLDING_POLLING_INTERVAL_MS: '600000',
        TRONGRID_DAILY_REQUEST_BUDGET: '250000',
      },
      MONITORING_MESSAGE,
    );

    expect(startup.line, startup.output).toMatchObject({
      paymentPollingIntervalMs: 3000,
      holdingPollingIntervalMs: 600000,
      tronGridDailyRequestBudget: 250000,
    });
  }, 30_000);

  it('refuses to start on a holding interval it could not parse', async () => {
    const startup = await start(
      { PAYMENT_HOLDING_POLLING_INTERVAL_MS: 'fifteen-minutes' },
      MONITORING_MESSAGE,
    );

    expect(startup.line).toBeUndefined();
    expect(startup.exitCode ?? 0).toBeGreaterThan(0);
    expect(startup.output).toContain('PAYMENT_HOLDING_POLLING_INTERVAL_MS');
  }, 30_000);
});
