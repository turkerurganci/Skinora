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

function start(env: Record<string, string>): Promise<Startup> {
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
        if (!raw.includes(STARTUP_MESSAGE)) continue;
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
      STAKE_ACCOUNT_ADDRESS: STAKE_ACCOUNT,
      STAKE_ACCOUNT_PERMISSION_ID: '5',
    });

    expect(startup.line, startup.output).toMatchObject({
      delegationOwner: STAKE_ACCOUNT,
      permissionId: 5,
      refundEstimateReadsStakeFrom: STAKE_ACCOUNT,
    });
  }, 30_000);

  it('keeps the hot wallet as its own stake holder when no stake account is set', async () => {
    const startup = await start({});

    expect(startup.line, startup.output).toMatchObject({
      delegationOwner: HOT_WALLET,
      permissionId: null,
      refundEstimateReadsStakeFrom: HOT_WALLET,
    });
  }, 30_000);

  it('refuses to start on a stake address the flow cannot use', async () => {
    const startup = await start({ STAKE_ACCOUNT_ADDRESS: 'TStakeAccountHoldingTheFrozenTrx' });

    expect(startup.line).toBeUndefined();
    // Exited on its own — a process still running at the timeout has no code.
    expect(startup.exitCode ?? 0).toBeGreaterThan(0);
    expect(startup.output).toContain('STAKE_ACCOUNT_MISCONFIGURED');
  }, 30_000);
});
