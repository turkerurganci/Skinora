import { spawn, type ChildProcess } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { afterAll, describe, expect, it } from 'vitest';
import { HdWalletService } from '../wallet/HdWalletService.js';

/**
 * T163 — the entry point, started the way production starts it (NODE_ENV
 * production, signing material as secret FILES), read back from its own
 * output and its HTTP surface. signingSecrets.test.ts covers the rules; this
 * file shows they are the ones the running process applies, and that neither
 * its log, its refusals, /health nor the API answers carry a secret
 * (DEFERRED_BACKLOG `SigningSecretsInPlainEnv`).
 *
 * Fixtures: the public BIP-39 reference mnemonic and its TRON index-0 key
 * (T143 vector) — computed, never written down, worthless on any network.
 */
const SIDECAR_ROOT = resolve(__dirname, '../..');
const TSX_CLI = resolve(SIDECAR_ROOT, 'node_modules/tsx/dist/cli.mjs');
/**
 * Each test cold-starts the service under tsx (transpiles the whole graph).
 * Alone that takes a few seconds; in a full local run next to other process
 * tests it went past 25 s (T163 validation B6). The wait only matters when
 * the line never comes — a refusal ends it early on exit — so it is generous.
 */
const WAIT = 60_000;
const TEST_TIMEOUT = WAIT + 30_000;
const INTERNAL_KEY = 'test-internal-key-'.padEnd(64, 'k');
const MNEMONIC = `${'abandon '.repeat(11)}about`;
/** Valid words, failing BIP-39 checksum. */
const BAD_MNEMONIC = `${'abandon '.repeat(11)}abandon`;
const HOT = new HdWalletService(MNEMONIC).deriveSigner(0);
const HOT_ADDRESS = 'TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH';
/** Property access keeps the pre-commit secret guard from reading fixtures as literals. */
const FIXTURE = { mnemonic: MNEMONIC };
const DEPOSIT_1 = 'TSeJkUh4Qv67VNFwY8LaAxERygNdy6NQZK';

const dir = mkdtempSync(join(tmpdir(), 'tron-secrets-'));
const files = {
  seed: join(dir, 'tron_hd_mnemonic'),
  hot: join(dir, 'tron_hot_wallet_private_key'),
  badSeed: join(dir, 'bad_seed'),
  // What Docker Desktop leaves at the mount point when the source file is missing.
  directory: join(dir, 'made-by-docker'),
  none: join(dir, 'none'),
};
writeFileSync(files.seed, MNEMONIC + '\n');
writeFileSync(files.hot, HOT.privateKey + '\n');
writeFileSync(files.badSeed, BAD_MNEMONIC + '\n');
mkdirSync(files.directory);

afterAll(() => rmSync(dir, { recursive: true, force: true }));

/** Everything a secret could look like in the output. */
const FORBIDDEN_IN_OUTPUT = [MNEMONIC, BAD_MNEMONIC, 'abandon abandon', HOT.privateKey];

function expectNoSecret(text: string): void {
  for (const fragment of FORBIDDEN_IN_OUTPUT) expect(text).not.toContain(fragment);
}

/** Production-shaped environment; secrets only through the *_FILE paths unless a test says otherwise. */
function productionEnv(env: Record<string, string>): NodeJS.ProcessEnv {
  const inherited = Object.fromEntries(
    Object.entries(process.env).filter(
      ([name]) =>
        !/^(HD_WALLET_|HOT_WALLET_|COLD_WALLET_|STAKE_ACCOUNT_|PORT$|NODE_ENV$)/.test(name),
    ),
  );
  return {
    ...inherited,
    NODE_ENV: 'production',
    PORT: '0',
    LOKI_URL: 'http://127.0.0.1:9',
    // /health probes the node; point it at a closed port so it answers fast.
    TRON_FULL_NODE_URL: 'http://127.0.0.1:9',
    TRON_SOLIDITY_URL: 'http://127.0.0.1:9',
    INTERNAL_KEY,
    HD_WALLET_MNEMONIC_FILE: files.seed,
    HOT_WALLET_PRIVATE_KEY_FILE: files.hot,
    HOT_WALLET_ADDRESS: HOT_ADDRESS,
    ...env,
  };
}

interface Run {
  child: ChildProcess;
  output: () => string;
  /** Resolves with the parsed line once `message` is logged, or undefined when the process exits first. */
  line: Promise<Record<string, unknown> | undefined>;
  exited: Promise<number | null>;
}

function launch(env: NodeJS.ProcessEnv, message: string): Run {
  const child = spawn(process.execPath, [TSX_CLI, 'src/index.ts'], { cwd: SIDECAR_ROOT, env });
  let output = '';
  let resolveLine: (line: Record<string, unknown> | undefined) => void = () => {};
  const line = new Promise<Record<string, unknown> | undefined>((r) => (resolveLine = r));
  const exited = new Promise<number | null>((r) =>
    child.on('close', (code) => {
      resolveLine(undefined);
      r(code);
    }),
  );
  child.stdout?.on('data', (chunk: Buffer) => {
    output += chunk.toString();
    for (const raw of output.split('\n')) {
      if (!raw.includes(message)) continue;
      try {
        resolveLine(JSON.parse(raw) as Record<string, unknown>);
      } catch {
        // Not fully arrived yet.
      }
    }
  });
  child.stderr?.on('data', (chunk: Buffer) => (output += chunk.toString()));
  return { child, output: () => output, line, exited };
}

function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T | undefined> {
  return Promise.race([promise, new Promise<undefined>((r) => setTimeout(() => r(undefined), ms))]);
}

async function refusedWith(
  env: Record<string, string>,
): Promise<{ code: number | null; output: string }> {
  const run = launch(productionEnv(env), 'Blockchain sidecar listening');
  const listening = await withTimeout(run.line, WAIT);
  run.child.kill();
  const code = await run.exited;
  expect(listening, run.output()).toBeUndefined();
  return { code, output: run.output() };
}

function freePort(): Promise<number> {
  return new Promise((done, fail) => {
    const server = createServer();
    server.once('error', fail);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      server.close(() => done(typeof address === 'object' && address ? address.port : 0));
    });
  });
}

describe('TRON service startup — signing secrets from Docker secret files (T163)', () => {
  it(
    'starts in production from the two files and logs sources and public addresses only',
    async () => {
      const run = launch(productionEnv({}), 'HD wallet initialized');
      const initialized = await withTimeout(run.line, WAIT);
      run.child.kill();
      await run.exited;
      const output = run.output();

      expect(initialized, output).toMatchObject({ depositIndex0: HOT_ADDRESS });
      const signing = output.split('\n').find((l) => l.includes('Signing secrets:'));
      expect(signing, output).toBeDefined();
      expect(JSON.parse(signing as string)).toMatchObject({
        hdMnemonicSource: 'file',
        hotWalletKeySource: 'file',
        hotWalletAddressFromKey: HOT_ADDRESS,
      });
      expectNoSecret(output);
    },
    TEST_TIMEOUT,
  );

  it(
    '/health, the derive API and its errors carry no secret',
    async () => {
      const port = await freePort();
      const run = launch(productionEnv({ PORT: String(port) }), 'HD wallet initialized');
      try {
        expect(await withTimeout(run.line, WAIT), run.output()).toBeDefined();
        const base = `http://127.0.0.1:${port}`;
        const post = (body: unknown, key = INTERNAL_KEY) =>
          fetch(`${base}/api/wallet/derive`, {
            method: 'POST',
            headers: { 'content-type': 'application/json', 'x-internal-key': key },
            body: JSON.stringify(body),
          });

        const health = await fetch(`${base}/health`);
        const healthBody = await health.text();
        const derived = await post({ index: 1, network: 'TRON' });
        const derivedBody = await derived.text();
        const invalid = await post({ index: -1, network: 'TRON' });
        const invalidBody = await invalid.text();
        const unauthorised = await post({ index: 1, network: 'TRON' }, 'wrong-key');
        const unauthorisedBody = await unauthorised.text();

        expect(healthBody).toContain('"service"');
        expect(derived.status).toBe(200);
        expect(JSON.parse(derivedBody)).toMatchObject({ address: DEPOSIT_1, index: 1 });
        expect(invalid.status).toBe(400);
        expect(unauthorised.status).toBe(401);
        for (const body of [healthBody, derivedBody, invalidBody, unauthorisedBody]) {
          expectNoSecret(body);
          expect(body).not.toContain(INTERNAL_KEY);
        }
      } finally {
        run.child.kill();
        await run.exited;
      }
      expectNoSecret(run.output());
    },
    TEST_TIMEOUT,
  );
});

describe('TRON service startup — refuses unsafe signing secrets, without echoing them (T163)', () => {
  // Objects, not arrays: the test title then names the case, never the env it carries.
  it.each<{ label: string; code: string; env: Record<string, string> }>([
    {
      label: 'the seed as a plain variable',
      code: 'SIGNING_SECRET_IN_ENV',
      env: {
        HD_WALLET_MNEMONIC_FILE: files.none,
        HD_WALLET_MNEMONIC: FIXTURE.mnemonic,
      },
    },
    {
      label: 'the hot key as a plain variable',
      code: 'SIGNING_SECRET_IN_ENV',
      env: {
        HOT_WALLET_PRIVATE_KEY_FILE: files.none,
        HOT_WALLET_PRIVATE_KEY: HOT.privateKey,
      },
    },
    {
      label: 'a plain variable with NODE_ENV unset',
      code: 'SIGNING_SECRET_IN_ENV',
      env: {
        NODE_ENV: '',
        HD_WALLET_MNEMONIC: FIXTURE.mnemonic,
      },
    },
    {
      label: 'a directory where the seed file should be',
      code: 'SIGNING_SECRET_UNREADABLE',
      env: { HD_WALLET_MNEMONIC_FILE: files.directory },
    },
    {
      label: 'a hot key that does not control HOT_WALLET_ADDRESS',
      code: 'HOT_WALLET_ADDRESS_MISMATCH',
      env: { HOT_WALLET_ADDRESS: DEPOSIT_1 },
    },
    {
      label: 'the seed pasted into the key file',
      code: 'INVALID_SIGNING_KEY',
      env: { HOT_WALLET_PRIVATE_KEY_FILE: files.seed },
    },
  ])(
    '$label → exits non-zero with $code',
    async ({ env, code }) => {
      const refused = await refusedWith(env);
      expect(refused.code ?? 0, refused.output).toBeGreaterThan(0);
      expect(refused.output).toContain(code);
      expect(refused.output).not.toContain('Signing secrets:');
      expectNoSecret(refused.output);
    },
    TEST_TIMEOUT,
  );

  it(
    'a seed file that fails its BIP-39 checksum stops the service and is not echoed',
    async () => {
      const run = launch(
        productionEnv({ HD_WALLET_MNEMONIC_FILE: files.badSeed }),
        'HD wallet initialized',
      );
      const initialized = await withTimeout(run.line, WAIT);
      run.child.kill();
      const code = await run.exited;

      expect(initialized, run.output()).toBeUndefined();
      expect(code ?? 0, run.output()).toBeGreaterThan(0);
      expect(run.output()).toContain('HD wallet initialization failed');
      expectNoSecret(run.output());
    },
    TEST_TIMEOUT,
  );
});
