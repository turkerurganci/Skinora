import { spawn } from 'node:child_process';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * T161 — the entry point, started the way production starts it (environment
 * only), must refuse to start without its internal key (05 §3.4).
 *
 * <para>
 * Before T161 an empty INTERNAL_KEY made the auth middleware skip the check
 * ("dev mode"), so a container started without it answered every /api route —
 * inventories, trade-hold probes, limited-account checks — to whoever reached
 * its port. Unit tests build the router with a key in hand; this file checks
 * the one place that decides whether the process listens at all.
 * </para>
 */
const SIDECAR_ROOT = resolve(__dirname, '../..');
const TSX_CLI = resolve(SIDECAR_ROOT, 'node_modules/tsx/dist/cli.mjs');
const LISTENING = 'Steam sidecar listening';

interface Startup {
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
        REDIS_URL: '',
        STEAM_API_KEY: '',
        INTERNAL_KEY: 'test-internal-key-'.padEnd(64, 'k'),
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
        if (!raw.includes(LISTENING)) continue;
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

describe('sidecar startup — the internal key is required (T161)', () => {
  it.each([
    ['unset', { INTERNAL_KEY: '' }, 'INTERNAL_KEY_MISSING'],
    ['blank', { INTERNAL_KEY: '   ' }, 'INTERNAL_KEY_MISSING'],
    [
      'shorter than 32 characters',
      { INTERNAL_KEY: 'test-short-'.padEnd(31, 'k') },
      'INTERNAL_KEY_TOO_SHORT',
    ],
  ])(
    'refuses to start when the key is %s',
    async (_label, env, code) => {
      const startup = await start(env);

      expect(startup.line, startup.output).toBeUndefined();
      // Exited on its own — a process still running at the timeout has no code.
      expect(startup.exitCode ?? 0).toBeGreaterThan(0);
      expect(startup.output).toContain(code);
    },
    30_000,
  );

  it('starts and listens with a key', async () => {
    const startup = await start({});

    expect(startup.line, startup.output).toBeDefined();
    expect(JSON.stringify(startup.line)).toContain(LISTENING);
  }, 30_000);
});
