import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { afterAll, describe, expect, it } from 'vitest';

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
/** T171a — production reads the key only from its Docker secret file. */
const FILE_KEY = 'test-file-internal-key-'.padEnd(64, 'f');
const dir = mkdtempSync(join(tmpdir(), 'steam-secrets-'));
const files = {
  key: join(dir, 'steam_sidecar_internal_key'),
  short: join(dir, 'short'),
  directory: join(dir, 'made-by-docker'),
  none: join(dir, 'none'),
  steamApiKey: join(dir, 'steam_api_key'),
  redisPassword: join(dir, 'redis_password'),
};
/** T171b — the Steam Web API key and the Redis password come from files too. */
const STEAM_KEY = 'TESTSTEAMKEY'.padEnd(32, '7');
const REDIS_PASSWORD = 'test-redis-password-'.padEnd(40, 'r');
writeFileSync(files.key, FILE_KEY);
writeFileSync(files.steamApiKey, STEAM_KEY + '\n');
writeFileSync(files.redisPassword, REDIS_PASSWORD + '\n');
writeFileSync(files.short, 'test-short-key');
mkdirSync(files.directory);
afterAll(() => rmSync(dir, { recursive: true, force: true }));

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
        ...Object.fromEntries(
          Object.entries(process.env).filter(([name]) => !name.startsWith('INTERNAL_KEY')),
        ),
        NODE_ENV: 'test',
        PORT: '0',
        LOKI_URL: 'http://127.0.0.1:9',
        REDIS_URL: '',
        STEAM_API_KEY: '',
        STEAM_API_KEY_FILE: files.none,
        REDIS_PASSWORD_FILE: files.none,
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

/**
 * T171a — production (NODE_ENV=production) reads the internal key only from
 * its Docker secret file (DEFERRED_BACKLOG `SharedSecretsInPlainEnv`).
 */
describe('sidecar startup — the internal key comes from its secret file in production (T171a)', () => {
  const production = { NODE_ENV: 'production', INTERNAL_KEY: '', INTERNAL_KEY_FILE: files.key };

  it('starts from the file and logs the source only', async () => {
    const startup = await start(production);

    expect(startup.line, startup.output).toBeDefined();
    expect(startup.output).toContain('"internalKeySource":"file"');
    expect(startup.output).not.toContain(FILE_KEY);
  }, 30_000);

  it.each([
    [
      'the key as a plain variable',
      { INTERNAL_KEY: 'test-env-key-'.padEnd(64, 'e') },
      'SHARED_SECRET_IN_ENV',
    ],
    ['no key file', { INTERNAL_KEY_FILE: files.none }, 'INTERNAL_KEY_MISSING'],
    ['a short key file', { INTERNAL_KEY_FILE: files.short }, 'INTERNAL_KEY_TOO_SHORT'],
    [
      'a directory where the file should be',
      { INTERNAL_KEY_FILE: files.directory },
      'SHARED_SECRET_UNREADABLE',
    ],
  ])(
    'refuses to start with %s',
    async (_label, env, code) => {
      const startup = await start({ ...production, ...env });

      expect(startup.line, startup.output).toBeUndefined();
      expect(startup.exitCode ?? 0).toBeGreaterThan(0);
      expect(startup.output).toContain(code);
      expect(startup.output).not.toContain(FILE_KEY);
      expect(startup.output).not.toContain('test-env-key-');
      expect(startup.output).not.toContain('test-short-key');
    },
    30_000,
  );
});

/**
 * T171b — the Steam Web API key and the Redis password (DEFERRED_BACKLOG
 * `ThirdPartyCredentialsInPlainEnv`): in production only from their Docker
 * secret files; the Redis URL carries no password; never echoed.
 */
describe('sidecar startup — provider credentials come from their secret files in production (T171b)', () => {
  const production = {
    NODE_ENV: 'production',
    INTERNAL_KEY: '',
    INTERNAL_KEY_FILE: files.key,
    STEAM_API_KEY_FILE: files.steamApiKey,
    REDIS_PASSWORD_FILE: files.redisPassword,
    // A closed port: the cache never connects, the process still listens.
    REDIS_URL: 'redis://127.0.0.1:9',
  };

  it('starts from the files and logs the sources only', async () => {
    const startup = await start(production);

    expect(startup.line, startup.output).toBeDefined();
    expect(startup.output).toContain('"steamApiKeySource":"file"');
    expect(startup.output).toContain('"redisPasswordSource":"file"');
    expect(startup.output).not.toContain(STEAM_KEY);
    expect(startup.output).not.toContain(REDIS_PASSWORD);
  }, 30_000);

  it.each([
    ['the Steam key as a plain variable', { STEAM_API_KEY: STEAM_KEY }, 'CREDENTIAL_IN_ENV'],
    [
      'a Redis URL that carries the password',
      { REDIS_PASSWORD_FILE: files.none, REDIS_URL: `redis://:${REDIS_PASSWORD}@127.0.0.1:9` },
      'CREDENTIAL_IN_ENV',
    ],
    [
      'a directory where the Redis password file should be',
      { REDIS_PASSWORD_FILE: files.directory },
      'CREDENTIAL_UNREADABLE',
    ],
    [
      'a directory where the Steam key file should be',
      { STEAM_API_KEY_FILE: files.directory },
      'CREDENTIAL_UNREADABLE',
    ],
  ])(
    'refuses to start with %s',
    async (_label, env, code) => {
      const startup = await start({ ...production, ...env });

      expect(startup.line, startup.output).toBeUndefined();
      expect(startup.exitCode ?? 0).toBeGreaterThan(0);
      expect(startup.output).toContain(code);
      expect(startup.output).not.toContain(STEAM_KEY);
      expect(startup.output).not.toContain(REDIS_PASSWORD);
    },
    30_000,
  );
});
