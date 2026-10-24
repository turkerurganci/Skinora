import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { afterAll, describe, expect, it } from 'vitest';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';

/**
 * The entry point, started the way production starts it (environment and
 * secret files only), read back from its own log lines. Unit tests build every
 * object with the values in hand; this is what shows the service REFUSES to
 * start without its internal key, its seed or with one key for two wallets
 * (08 §3.7.2), and that the values it starts with are the ones configured.
 */
const ROOT = resolve(__dirname, '../..');
const TSX_CLI = resolve(ROOT, 'node_modules/tsx/dist/cli.mjs');
const MNEMONIC = `${'abandon '.repeat(11)}about`;
const dir = mkdtempSync(join(tmpdir(), 'eth-sidecar-'));
const files = {
  seed: join(dir, 'seed'),
  hot: join(dir, 'hot'),
  fuel: join(dir, 'fuel'),
  missing: join(dir, 'does-not-exist'),
  journal: join(dir, 'tx-journal'),
  internalKey: join(dir, 'ethereum_sidecar_internal_key'),
  webhookSecret: join(dir, 'ethereum_webhook_secret'),
  rpcPrimaryUrl: join(dir, 'ethereum_rpc_primary_url'),
  rpcBackupUrl: join(dir, 'ethereum_rpc_backup_url'),
};
/** T171b — production reads the RPC URLs (they carry the API key) from files. */
const RPC = {
  primary: 'http://127.0.0.1:9/test-rpc-key-in-file-path',
  backup: 'http://127.0.0.1:9/test-rpc-backup-key-in-file',
};
const SHARED = {
  internalKey: 'test-eth-internal-key-'.padEnd(64, 'k'),
  webhookSecret: 'test-eth-webhook-secret-'.padEnd(64, 'w'),
};
const keys = new EthereumHdWallet(MNEMONIC);
writeFileSync(files.seed, MNEMONIC + '\n');
writeFileSync(files.hot, keys.deriveSigner(30).privateKey.slice(2) + '\n');
writeFileSync(files.fuel, keys.deriveSigner(31).privateKey.slice(2) + '\n');
writeFileSync(files.internalKey, SHARED.internalKey + '\n');
writeFileSync(files.rpcPrimaryUrl, RPC.primary + '\n');
writeFileSync(files.rpcBackupUrl, RPC.backup + '\n');
writeFileSync(files.webhookSecret, SHARED.webhookSecret + '\n');

// A started service may still be writing into its journal directory (permit
// store, write probe) while the tree is removed; retry instead of failing the
// run on ENOTEMPTY (CI run 40007951627).
afterAll(() => rmSync(dir, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 }));

/** The host's environment minus anything this service reads, so only the test's values count. */
function withoutEthereumVariables(env: NodeJS.ProcessEnv): NodeJS.ProcessEnv {
  return Object.fromEntries(
    Object.entries(env).filter(
      ([name]) =>
        !name.startsWith('ETHEREUM_') &&
        ![
          'INTERNAL_KEY',
          'INTERNAL_KEY_FILE',
          'WEBHOOK_SECRET',
          'WEBHOOK_SECRET_FILE',
          'PORT',
        ].includes(name),
    ),
  );
}

interface Startup {
  line?: Record<string, unknown>;
  exitCode?: number | null;
  output: string;
}

function start(env: Record<string, string>, message: string): Promise<Startup> {
  return new Promise((done) => {
    const child = spawn(process.execPath, [TSX_CLI, 'src/index.ts'], {
      cwd: ROOT,
      env: {
        ...withoutEthereumVariables(process.env),
        NODE_ENV: 'test',
        PORT: '0',
        LOKI_URL: 'http://127.0.0.1:9',
        INTERNAL_KEY: 'test-internal-key-'.padEnd(64, 'k'),
        ETHEREUM_HD_MNEMONIC_FILE: files.seed,
        ETHEREUM_TX_JOURNAL_DIR: files.journal,
        ETHEREUM_HOT_WALLET_PRIVATE_KEY_FILE: files.missing,
        ETHEREUM_FUEL_WALLET_PRIVATE_KEY_FILE: files.missing,
        // Unreachable on purpose: the service starts without a reachable
        // provider (its /health says so); it does not start without one set.
        ETHEREUM_RPC_PRIMARY_URL: 'http://127.0.0.1:9/rpc-key-in-path',
        ETHEREUM_RPC_BACKUP_URL: 'http://127.0.0.1:9/backup',
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

const CONFIGURED = 'Ethereum service configured';
const REFUSED = 'Ethereum service refused to start';

describe('Ethereum service startup — refuses what it must not run without', () => {
  it.each([
    ['no internal key', { INTERNAL_KEY: '' }, 'INTERNAL_KEY_MISSING'],
    ['a short internal key', { INTERNAL_KEY: 'test-short' }, 'INTERNAL_KEY_TOO_SHORT'],
    ['no seed secret', { ETHEREUM_HD_MNEMONIC_FILE: files.missing }, 'HD_WALLET_NOT_CONFIGURED'],
    ['no primary RPC', { ETHEREUM_RPC_PRIMARY_URL: '' }, 'RPC_NOT_CONFIGURED'],
    // T152 — a signer that cannot remember what it signed must not sign.
    [
      'a journal directory it cannot write',
      { ETHEREUM_TX_JOURNAL_DIR: join(files.seed, 'journal') },
      'TX_JOURNAL_UNAVAILABLE',
    ],
    [
      'one key for the hot and the fuel wallet',
      {
        ETHEREUM_HOT_WALLET_PRIVATE_KEY_FILE: files.hot,
        ETHEREUM_FUEL_WALLET_PRIVATE_KEY_FILE: files.hot,
      },
      'WALLET_KEYS_NOT_DISTINCT',
    ],
  ])(
    '%s → exits non-zero with %s',
    async (_label, env, code) => {
      const startup = await start(env, REFUSED);
      expect(startup.line, startup.output).toMatchObject({ code });
      expect(startup.output).not.toContain(CONFIGURED);
    },
    30_000,
  );
});

describe('Ethereum service startup — the configured values reach the running process', () => {
  it('derives the T143 index-0 vector from the seed secret and names its providers without their URLs', async () => {
    const startup = await start(
      {
        ETHEREUM_HOT_WALLET_PRIVATE_KEY_FILE: files.hot,
        ETHEREUM_FUEL_WALLET_PRIVATE_KEY_FILE: files.fuel,
        ETHEREUM_RPC_PRIMARY_NAME: 'alchemy',
        ETHEREUM_RPC_BACKUP_NAME: 'chainstack',
        ETHEREUM_RPC_BACKUP_MAX_BLOCK_RANGE: '100',
        ETHEREUM_NETWORK: 'sepolia',
      },
      CONFIGURED,
    );

    expect(startup.line, startup.output).toMatchObject({
      network: 'ETHEREUM',
      chainId: 11155111,
      depositIndex0: '0x9858EfFD232B4033E47d90003D41EC34EcaEda94',
      hotWalletAddress: keys.derive(30).address,
      fuelWalletAddress: keys.derive(31).address,
      rpcProviders: [
        { name: 'alchemy', host: '127.0.0.1:9', maxBlockRange: 10, maxAddressesPerQuery: 1000 },
        { name: 'chainstack', host: '127.0.0.1:9', maxBlockRange: 100, maxAddressesPerQuery: 1000 },
      ],
      finalizedStallSeconds: 1800,
    });
    expect(startup.output).not.toContain('rpc-key-in-path');
    expect(startup.output).not.toContain(MNEMONIC);
  }, 30_000);

  it('T151 — starts the payment monitor with the configured read-back and reconciliation budget', async () => {
    const startup = await start(
      {
        ETHEREUM_MONITOR_RESTART_LOOKBACK_BLOCKS: '3600',
        ETHEREUM_RECONCILIATION_CU_PER_SECOND: '150',
        ETHEREUM_RECONCILIATION_INTERVAL_MS: '1800000',
        ETHEREUM_USDT_CONTRACT: '0xdAC17F958D2ee523a2206206994597C13D831ec7',
      },
      'Ethereum payment monitor configured',
    );
    expect(startup.line, startup.output).toMatchObject({
      restartLookbackBlocks: 3600,
      reconciliationCuPerSecond: 150,
      reconciliationIntervalMs: 1_800_000,
      reconciliationMaxFinalizedAgeSeconds: 1500,
      // eth_getBalance 20 + USDT balanceOf 26 (USDC not configured on Sepolia).
      reconciliationCuPerDeposit: 46,
    });
  }, 30_000);

  it('T151 — says loudly that payments cannot be reported without the webhook secret', async () => {
    const startup = await start({ WEBHOOK_SECRET: '' }, 'WEBHOOK_SECRET is not set');
    expect(startup.line, startup.output).toMatchObject({ level: 50 });
  }, 30_000);
});

/**
 * T171a — in production the internal key and the webhook secret come only from
 * their Docker secret files (DEFERRED_BACKLOG `SharedSecretsInPlainEnv`); a
 * plain variable stops the service and nothing echoes a value.
 */
const shortWebhookFile = join(dir, 'short_webhook_secret');
writeFileSync(shortWebhookFile, 'test-short-webhook-secret');

describe('Ethereum service startup — shared secrets from Docker secret files (T171a)', () => {
  const production = {
    NODE_ENV: 'production',
    INTERNAL_KEY: '',
    INTERNAL_KEY_FILE: files.internalKey,
    WEBHOOK_SECRET_FILE: files.webhookSecret,
    // T171b — the RPC URLs are secret files in production too.
    ETHEREUM_RPC_PRIMARY_URL: '',
    ETHEREUM_RPC_BACKUP_URL: '',
    ETHEREUM_RPC_PRIMARY_URL_FILE: files.rpcPrimaryUrl,
    ETHEREUM_RPC_BACKUP_URL_FILE: files.rpcBackupUrl,
  };

  it('starts in production from the two files and logs their sources only', async () => {
    const startup = await start(production, CONFIGURED);
    expect(startup.line, startup.output).toMatchObject({
      internalKeySource: 'file',
      webhookSecretSource: 'file',
      webhookConfigured: true,
    });
    expect(startup.output).not.toContain(SHARED.internalKey);
    expect(startup.output).not.toContain(SHARED.webhookSecret);
  }, 30_000);

  it.each([
    [
      'the internal key as a plain variable',
      { INTERNAL_KEY: 'test-env-internal-key-'.padEnd(64, 'e') },
      'SHARED_SECRET_IN_ENV',
    ],
    [
      'the webhook secret as a plain variable',
      { WEBHOOK_SECRET: 'test-env-webhook-'.padEnd(64, 'e') },
      'SHARED_SECRET_IN_ENV',
    ],
    ['no internal key file', { INTERNAL_KEY_FILE: files.missing }, 'INTERNAL_KEY_MISSING'],
    [
      'a directory where the internal key file should be',
      { INTERNAL_KEY_FILE: dir },
      'SHARED_SECRET_UNREADABLE',
    ],
    [
      'a short webhook secret file',
      { WEBHOOK_SECRET_FILE: shortWebhookFile },
      'WEBHOOK_SECRET_TOO_SHORT',
    ],
  ])(
    '%s → exits non-zero with %s',
    async (_label, env, code) => {
      const startup = await start({ ...production, ...env }, REFUSED);
      expect(startup.line, startup.output).toMatchObject({ code });
      expect(startup.output).not.toContain(CONFIGURED);
      expect(startup.output).not.toContain(SHARED.internalKey);
      expect(startup.output).not.toContain('test-env-');
    },
    30_000,
  );
});

/**
 * T171b — in production the RPC URLs (they carry the provider's API key) come
 * only from their Docker secret files (DEFERRED_BACKLOG
 * `ThirdPartyCredentialsInPlainEnv`); a plain variable stops the service and
 * neither the URL nor its key reaches the output.
 */
describe('Ethereum service startup — RPC URLs from Docker secret files (T171b)', () => {
  const production = {
    NODE_ENV: 'production',
    INTERNAL_KEY: '',
    INTERNAL_KEY_FILE: files.internalKey,
    WEBHOOK_SECRET_FILE: files.webhookSecret,
    ETHEREUM_RPC_PRIMARY_URL: '',
    ETHEREUM_RPC_BACKUP_URL: '',
    ETHEREUM_RPC_PRIMARY_URL_FILE: files.rpcPrimaryUrl,
    ETHEREUM_RPC_BACKUP_URL_FILE: files.rpcBackupUrl,
  };

  it('starts in production from the files and names the providers without their URLs', async () => {
    const startup = await start(production, CONFIGURED);
    expect(startup.line, startup.output).toMatchObject({
      rpcPrimaryUrlSource: 'file',
      rpcBackupUrlSource: 'file',
    });
    expect(startup.output).not.toContain('test-rpc-');
  }, 30_000);

  it.each([
    [
      'the primary URL as a plain variable',
      { ETHEREUM_RPC_PRIMARY_URL: RPC.primary },
      'CREDENTIAL_IN_ENV',
    ],
    [
      'the backup URL as a plain variable',
      { ETHEREUM_RPC_BACKUP_URL: RPC.backup },
      'CREDENTIAL_IN_ENV',
    ],
    ['no primary URL file', { ETHEREUM_RPC_PRIMARY_URL_FILE: files.missing }, 'RPC_NOT_CONFIGURED'],
    [
      'a directory where the primary URL file should be',
      { ETHEREUM_RPC_PRIMARY_URL_FILE: dir },
      'CREDENTIAL_UNREADABLE',
    ],
  ])(
    '%s → exits non-zero with %s',
    async (_label, env, code) => {
      const startup = await start({ ...production, ...env }, REFUSED);
      expect(startup.line, startup.output).toMatchObject({ code });
      expect(startup.output).not.toContain(CONFIGURED);
      expect(startup.output).not.toContain('test-rpc-');
    },
    30_000,
  );
});
