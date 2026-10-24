import { describe, expect, it } from 'vitest';
import { ConfigError, DEFAULT_SECRET_PATHS, loadConfig, providerHost } from './index.js';
import { DEFAULT_SHARED_SECRET_PATHS } from './sharedSecrets.js';
import { DEFAULT_CREDENTIAL_PATHS } from './providerCredentials.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';

const MNEMONIC = `${'abandon '.repeat(11)}about`;
const KEY = 'k'.repeat(64);
const RPC_KEY = 'alchemyApiKeyThatMustNeverBeLogged';
const PRIMARY_URL = `https://eth-sepolia.g.alchemy.com/v2/${RPC_KEY}`;
const BACKUP_URL = 'https://ethereum-sepolia.core.chainstack.com/abcdef0123456789';
/** T171b — a production-shaped test reads the primary RPC URL from this file (ETHEREUM_RPC_PRIMARY_URL_FILE). */
const RPC_FILE = '/test/ethereum_rpc_primary_url';
const signer = new EthereumHdWallet(MNEMONIC).deriveSigner(20);

const baseEnv = {
  // T171a — the INTERNAL_KEY variable is read only under development|test;
  // production reads the Docker secret file (the "shared secrets" block below).
  NODE_ENV: 'test',
  INTERNAL_KEY: KEY,
  ETHEREUM_RPC_PRIMARY_URL: PRIMARY_URL,
  ETHEREUM_RPC_BACKUP_URL: BACKUP_URL,
};

/** Secret files as a map path → content; anything else does not exist. */
function secrets(files: Record<string, string | null> = {}) {
  const all: Record<string, string | null> = {
    [DEFAULT_SECRET_PATHS.hdMnemonic]: MNEMONIC,
    [RPC_FILE]: PRIMARY_URL,
    ...files,
  };
  return (path: string) => all[path] ?? null;
}

function errorOf(fn: () => unknown): ConfigError {
  try {
    fn();
  } catch (err) {
    if (err instanceof ConfigError) return err;
    throw err;
  }
  throw new Error('expected a ConfigError');
}

describe('loadConfig — refuses to start without what the service needs', () => {
  it('without INTERNAL_KEY', () => {
    expect(errorOf(() => loadConfig({ ...baseEnv, INTERNAL_KEY: '' }, secrets())).code).toBe(
      'INTERNAL_KEY_MISSING',
    );
    expect(errorOf(() => loadConfig({ ...baseEnv, INTERNAL_KEY: '   ' }, secrets())).code).toBe(
      'INTERNAL_KEY_MISSING',
    );
  });

  it('with an INTERNAL_KEY shorter than 32 characters', () => {
    expect(
      errorOf(() => loadConfig({ ...baseEnv, INTERNAL_KEY: 'x'.repeat(31) }, secrets())).code,
    ).toBe('INTERNAL_KEY_TOO_SHORT');
    expect(() => loadConfig({ ...baseEnv, INTERNAL_KEY: 'x'.repeat(32) }, secrets())).not.toThrow();
  });

  it('without its own seed secret (K7)', () => {
    expect(
      errorOf(() => loadConfig(baseEnv, secrets({ [DEFAULT_SECRET_PATHS.hdMnemonic]: null }))).code,
    ).toBe('HD_WALLET_NOT_CONFIGURED');
  });

  // T163 — a secret path that exists but is not a readable file (Docker made a
  // directory for a missing source file) is named, not reported as STARTUP_FAILED.
  it.each([
    ['seed', DEFAULT_SECRET_PATHS.hdMnemonic],
    ['hot wallet key', DEFAULT_SECRET_PATHS.hotWalletKey],
    ['fuel wallet key', DEFAULT_SECRET_PATHS.fuelWalletKey],
  ])('with an unreadable %s secret (SECRET_UNREADABLE, path named)', (_label, unreadable) => {
    const reader = (path: string) => {
      if (path === unreadable)
        throw new Error(`Secret file ${path} exists but cannot be read: EISDIR`);
      return secrets()(path);
    };
    const err = errorOf(() => loadConfig(baseEnv, reader));
    expect(err.code).toBe('SECRET_UNREADABLE');
    expect(err.message).toContain(unreadable);
    expect(err.message).toContain('DIRECTORY');
  });

  it('reads the seed from ETHEREUM_HD_MNEMONIC_FILE when set', () => {
    const config = loadConfig(
      { ...baseEnv, ETHEREUM_HD_MNEMONIC_FILE: '/elsewhere/seed' },
      secrets({ [DEFAULT_SECRET_PATHS.hdMnemonic]: null, '/elsewhere/seed': MNEMONIC }),
    );
    expect(config.hdMnemonic).toBe(MNEMONIC);
  });

  // T158 (c) — the seed and the signing keys come from the mounted secret
  // files only (K7, 05 §3.5). An env fallback (`docker inspect` shows env) is
  // the mutation this pins: every plausible variable name carries a valid
  // seed / key here, the files are absent, and nothing may be picked up.
  const ENV_LOOKALIKES = (secret: string) =>
    Object.fromEntries(
      [
        'ETHEREUM_HD_MNEMONIC',
        'ETHEREUM_MNEMONIC',
        'HD_WALLET_MNEMONIC',
        'MNEMONIC',
        'ETHEREUM_SEED',
        'ETHEREUM_HOT_WALLET_PRIVATE_KEY',
        'ETHEREUM_FUEL_WALLET_PRIVATE_KEY',
        'HOT_WALLET_PRIVATE_KEY',
        'FUEL_WALLET_PRIVATE_KEY',
      ].map((name) => [name, secret]),
    );

  it('T158 (c): never reads the seed from the environment — only from the secret file', () => {
    expect(
      errorOf(() =>
        loadConfig(
          { ...baseEnv, ...ENV_LOOKALIKES(MNEMONIC) },
          secrets({ [DEFAULT_SECRET_PATHS.hdMnemonic]: null }),
        ),
      ).code,
    ).toBe('HD_WALLET_NOT_CONFIGURED');
  });

  it('T158 (c): never reads a signing key from the environment — without the files both keys are absent', () => {
    const config = loadConfig({ ...baseEnv, ...ENV_LOOKALIKES(signer.privateKey) }, secrets());
    expect(config.hotWalletKey).toBeNull();
    expect(config.fuelWalletKey).toBeNull();
  });

  it('T158 (c): code level — the config reads no environment variable holding a seed or key, only *_FILE paths', async () => {
    const { readFileSync } = await import('node:fs');
    const { resolve } = await import('node:path');
    const source = readFileSync(resolve(__dirname, 'index.ts'), 'utf8');
    const envNames = [...source.matchAll(/value\(env, '([A-Z0-9_]+)'\)/g)].map((m) => m[1]);
    const direct = [...source.matchAll(/env\[['"]([A-Z0-9_]+)['"]\]|env\.([A-Z0-9_]+)/g)].map(
      (m) => m[1] ?? m[2],
    );
    const riskyName = /MNEMONIC|SEED|PRIVATE_KEY/;
    expect(envNames.filter((n) => riskyName.test(n) && !n.endsWith('_FILE'))).toEqual([]);
    expect(direct.filter((n) => riskyName.test(n))).toEqual([]);
    // The three *_FILE paths: read through the file reader, nothing else.
    expect(envNames).toEqual(
      expect.arrayContaining([
        'ETHEREUM_HD_MNEMONIC_FILE',
        'ETHEREUM_HOT_WALLET_PRIVATE_KEY_FILE',
        'ETHEREUM_FUEL_WALLET_PRIVATE_KEY_FILE',
      ]),
    );
  });

  it('without a primary RPC provider', () => {
    expect(
      errorOf(() => loadConfig({ ...baseEnv, ETHEREUM_RPC_PRIMARY_URL: '' }, secrets())).code,
    ).toBe('RPC_NOT_CONFIGURED');
  });

  it('on an unknown ETHEREUM_NETWORK', () => {
    expect(
      errorOf(() => loadConfig({ ...baseEnv, ETHEREUM_NETWORK: 'goerli' }, secrets())).code,
    ).toBe('INVALID_CONFIG');
  });

  it('on a malformed signing key, without echoing it', () => {
    const err = errorOf(() =>
      loadConfig(
        baseEnv,
        secrets({ [DEFAULT_SECRET_PATHS.hotWalletKey]: 'not-a-key-but-secret-text' }),
      ),
    );
    expect(err.code).toBe('INVALID_SIGNING_KEY');
    expect(err.message).not.toContain('not-a-key-but-secret-text');
  });

  it('on a malformed RPC URL, without echoing it', () => {
    const err = errorOf(() =>
      loadConfig({ ...baseEnv, ETHEREUM_RPC_BACKUP_URL: `not a url ${RPC_KEY}` }, secrets()),
    );
    expect(err.code).toBe('INVALID_CONFIG');
    expect(err.message).not.toContain(RPC_KEY);
  });

  it('on a mixed-case address with a wrong EIP-55 checksum', () => {
    // EIP-55 of index 0 is 0x9858EfFD…; flipping one letter breaks the checksum.
    expect(
      errorOf(() =>
        loadConfig(
          { ...baseEnv, ETHEREUM_HOT_WALLET_ADDRESS: '0x9858EfFD232B4033E47d90003D41EC34EcaEdA94' },
          secrets(),
        ),
      ).code,
    ).toBe('INVALID_CONFIG');
  });

  it('when the two providers share a metrics label', () => {
    expect(
      errorOf(() =>
        loadConfig(
          { ...baseEnv, ETHEREUM_RPC_PRIMARY_NAME: 'x', ETHEREUM_RPC_BACKUP_NAME: 'x' },
          secrets(),
        ),
      ).code,
    ).toBe('INVALID_CONFIG');
  });

  it.each(['abc', '0', '-5', '1.5'])('on a non-positive-integer limit %s', (raw) => {
    expect(
      errorOf(() =>
        loadConfig({ ...baseEnv, ETHEREUM_RPC_PRIMARY_MAX_BLOCK_RANGE: raw }, secrets()),
      ).code,
    ).toBe('INVALID_CONFIG');
  });
});

describe('loadConfig — values', () => {
  it('defaults to Sepolia, port 5300, and the T142 provider limits (Alchemy 10 / Chainstack 100 / 1,000 addresses)', () => {
    const config = loadConfig(baseEnv, secrets());
    expect(config.chain).toBe('sepolia');
    expect(config.chainId).toBe(11155111);
    expect(config.port).toBe(5300);
    expect(config.rpc.providers).toEqual([
      { name: 'primary', url: PRIMARY_URL, maxBlockRange: 10, maxAddressesPerQuery: 1000 },
      { name: 'backup', url: BACKUP_URL, maxBlockRange: 100, maxAddressesPerQuery: 1000 },
    ]);
    expect(config.finality).toEqual({ pollIntervalMs: 30_000, stallThresholdSeconds: 1800 });
  });

  it('mainnet is chain 1 and defaults to the mainnet USDT / USDC contracts (canonical lowercase)', () => {
    const config = loadConfig({ ...baseEnv, ETHEREUM_NETWORK: 'mainnet' }, secrets());
    expect(config.chainId).toBe(1);
    expect(config.tokenContracts).toEqual({
      USDT: '0xdac17f958d2ee523a2206206994597c13d831ec7',
      USDC: '0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48',
    });
  });

  it('a testnet has no token contract until one is configured', () => {
    expect(loadConfig(baseEnv, secrets()).tokenContracts).toEqual({ USDT: '', USDC: '' });
    expect(
      loadConfig(
        { ...baseEnv, ETHEREUM_USDC_CONTRACT: '0x1c7D4B196Cb0C7B01d743Fbc6116a902379C7238' },
        secrets(),
      ).tokenContracts.USDC,
    ).toBe('0x1c7d4b196cb0c7b01d743fbc6116a902379c7238');
  });

  it('a single provider is allowed (startup warns)', () => {
    const config = loadConfig({ ...baseEnv, ETHEREUM_RPC_BACKUP_URL: '' }, secrets());
    expect(config.rpc.providers.map((p) => p.name)).toEqual(['primary']);
  });

  it('reads the signing keys from their secret files, normalised to 0x + lowercase', () => {
    const config = loadConfig(
      baseEnv,
      secrets({
        [DEFAULT_SECRET_PATHS.hotWalletKey]: signer.privateKey.slice(2).toUpperCase(),
      }),
    );
    expect(config.hotWalletKey).toBe(signer.privateKey);
    expect(config.fuelWalletKey).toBeNull();
  });

  it('stores configured addresses in canonical lowercase', () => {
    const config = loadConfig(
      { ...baseEnv, ETHEREUM_HOT_WALLET_ADDRESS: signer.address },
      secrets(),
    );
    expect(config.hotWalletAddress).toBe(signer.address.toLowerCase());
  });
});

describe('loadConfig — sends (T152)', () => {
  it('leaves every send cap unset by default (fail closed) and defaults the journal, alarm and bump', () => {
    const config = loadConfig(baseEnv, secrets());
    expect(config.transfers).toEqual({
      journalDir: '/var/lib/skinora-ethereum/tx-journal',
      maxSingleTransferUnits: null,
      maxDailyOutflowUnits: null,
      maxDailyDepositRefundUnits: null,
      fuelMaxSingleWei: null,
      fuelMaxDailyWei: null,
      waitAlarmSeconds: 10_800,
      keeperIntervalMs: 30_000,
      bumpAfterSeconds: 300,
      journalAcknowledgedNonces: new Map(),
    });
  });

  it('reads operator nonce acknowledgements as canonical address → nonce (T152 düzeltme turu 2)', () => {
    const config = loadConfig(
      {
        ...baseEnv,
        ETHEREUM_JOURNAL_ACKNOWLEDGED_NONCES: `${signer.address}:12, 0x${'2'.repeat(40)}:0`,
      },
      secrets(),
    );
    expect([...config.transfers.journalAcknowledgedNonces]).toEqual([
      [signer.address.toLowerCase(), 12],
      ['0x' + '2'.repeat(40), 0],
    ]);
  });

  it.each([
    'abc',
    '0x1234:1',
    `0x${'2'.repeat(40)}`,
    `0x${'2'.repeat(40)}:-1`,
    `0x${'2'.repeat(40)}:1.5`,
    `0x${'2'.repeat(40)}:1,0x${'2'.repeat(40)}:2`,
  ])('refuses to start on a malformed acknowledgement %j', (bad) => {
    expect(
      errorOf(() =>
        loadConfig({ ...baseEnv, ETHEREUM_JOURNAL_ACKNOWLEDGED_NONCES: bad }, secrets()),
      ).code,
    ).toBe('INVALID_CONFIG');
  });

  it('parses the caps in token units (6 decimals) and wei (18 decimals)', () => {
    const config = loadConfig(
      {
        ...baseEnv,
        ETHEREUM_MAX_SINGLE_TRANSFER_USDT: '5000',
        ETHEREUM_MAX_DAILY_OUTFLOW_USDT: '20000.5',
        ETHEREUM_FUEL_MAX_SINGLE_ETH: '0.001',
        ETHEREUM_FUEL_MAX_DAILY_ETH: '0.05',
        ETHEREUM_TX_JOURNAL_DIR: '/data/journal',
      },
      secrets(),
    );
    expect(config.transfers.maxSingleTransferUnits).toBe(5_000_000_000n);
    expect(config.transfers.maxDailyOutflowUnits).toBe(20_000_500_000n);
    expect(config.transfers.fuelMaxSingleWei).toBe(1_000_000_000_000_000n);
    expect(config.transfers.fuelMaxDailyWei).toBe(50_000_000_000_000_000n);
    expect(config.transfers.journalDir).toBe('/data/journal');
  });

  it.each(['0', '-1', '1e3', '1.0000001', 'abc'])(
    'refuses to start on a malformed cap %j rather than run without it',
    (bad) => {
      expect(
        errorOf(() => loadConfig({ ...baseEnv, ETHEREUM_MAX_SINGLE_TRANSFER_USDT: bad }, secrets()))
          .code,
      ).toBe('INVALID_CONFIG');
    },
  );

  it('T172b — reads the deposit refund cap in token units; malformed refuses to start, empty leaves it unset', () => {
    expect(
      loadConfig({ ...baseEnv, ETHEREUM_DEPOSIT_REFUND_DAILY_LIMIT_USDT: '100' }, secrets())
        .transfers.maxDailyDepositRefundUnits,
    ).toBe(100_000_000n);
    expect(
      loadConfig({ ...baseEnv, ETHEREUM_DEPOSIT_REFUND_DAILY_LIMIT_USDT: '' }, secrets()).transfers
        .maxDailyDepositRefundUnits,
    ).toBeNull();
    for (const bad of ['0', '-1', '1e2', '100.0000001', 'abc']) {
      expect(
        errorOf(() =>
          loadConfig({ ...baseEnv, ETHEREUM_DEPOSIT_REFUND_DAILY_LIMIT_USDT: bad }, secrets()),
        ).code,
      ).toBe('INVALID_CONFIG');
    }
  });
});

describe('loadConfig — platform wallet ETH floors (T153)', () => {
  it('defaults both floors to 0.06 ETH (T142) and the reading to every 5 minutes', () => {
    expect(loadConfig(baseEnv, secrets()).walletBalances).toEqual({
      fuelFloorWei: 60_000_000_000_000_000n,
      hotFloorWei: 60_000_000_000_000_000n,
      intervalMs: 300_000,
    });
  });

  it('reads the floors in ETH (18 decimals)', () => {
    const config = loadConfig(
      {
        ...baseEnv,
        ETHEREUM_FUEL_WALLET_MIN_ETH: '0.1',
        ETHEREUM_HOT_WALLET_MIN_ETH: '0.000000000000000001',
        ETHEREUM_WALLET_BALANCE_INTERVAL_MS: '60000',
      },
      secrets(),
    );
    expect(config.walletBalances).toEqual({
      fuelFloorWei: 100_000_000_000_000_000n,
      hotFloorWei: 1n,
      intervalMs: 60_000,
    });
  });

  it.each([
    ['ETHEREUM_FUEL_WALLET_MIN_ETH', '0'],
    ['ETHEREUM_FUEL_WALLET_MIN_ETH', '-0.06'],
    ['ETHEREUM_HOT_WALLET_MIN_ETH', '0.0000000000000000001'],
    ['ETHEREUM_HOT_WALLET_MIN_ETH', 'abc'],
    ['ETHEREUM_WALLET_BALANCE_INTERVAL_MS', '0'],
  ])('refuses to start on %s=%j', (name, bad) => {
    expect(errorOf(() => loadConfig({ ...baseEnv, [name]: bad }, secrets())).code).toBe(
      'INVALID_CONFIG',
    );
  });
});

describe('providerHost', () => {
  it('shows the host only — the path carries the API key', () => {
    expect(providerHost(PRIMARY_URL)).toBe('eth-sepolia.g.alchemy.com');
    expect(providerHost(PRIMARY_URL)).not.toContain(RPC_KEY);
  });
});

/**
 * T171a — the internal key and the webhook secret come from Docker secret
 * files, the same files the backend reads (DEFERRED_BACKLOG
 * `SharedSecretsInPlainEnv`). The running process: startupWiring.test.ts.
 */
describe('loadConfig — shared secrets (T171a)', () => {
  const WEBHOOK = 'w'.repeat(64);
  const production = {
    NODE_ENV: 'production',
    // T171b — the RPC URL is a secret file in production too.
    ETHEREUM_RPC_PRIMARY_URL_FILE: RPC_FILE,
  };
  const mounted = () =>
    secrets({
      [DEFAULT_SHARED_SECRET_PATHS.internalKey]: KEY,
      [DEFAULT_SHARED_SECRET_PATHS.webhookSecret]: WEBHOOK,
    });

  it('reads both from the compose mount points in production', () => {
    expect(DEFAULT_SHARED_SECRET_PATHS).toEqual({
      internalKey: '/run/secrets/ethereum_sidecar_internal_key',
      webhookSecret: '/run/secrets/ethereum_webhook_secret',
    });
    const config = loadConfig(production, mounted());
    expect(config.internalKey).toBe(KEY);
    expect(config.webhookHmacKey).toBe(WEBHOOK);
    expect(config.sharedSecretSources).toEqual({ internalKey: 'file', webhookSecret: 'file' });
  });

  it.each([
    ['production', 'INTERNAL_KEY'],
    ['production', 'WEBHOOK_SECRET'],
    ['', 'INTERNAL_KEY'],
    ['Development', 'WEBHOOK_SECRET'],
  ])('NODE_ENV=%j refuses %s as a plain variable (SHARED_SECRET_IN_ENV)', (nodeEnv, variable) => {
    const error = errorOf(() =>
      loadConfig({ ...production, NODE_ENV: nodeEnv, [variable]: 'v'.repeat(64) }, mounted()),
    );
    expect(error.code).toBe('SHARED_SECRET_IN_ENV');
    expect(error.message).not.toContain('v'.repeat(32));
  });

  it('development reads the variables; file and variable together is ambiguous', () => {
    const config = loadConfig(
      { ...baseEnv, NODE_ENV: 'development', WEBHOOK_SECRET: WEBHOOK },
      secrets(),
    );
    expect(config.sharedSecretSources).toEqual({ internalKey: 'env', webhookSecret: 'env' });
    expect(errorOf(() => loadConfig({ ...baseEnv, NODE_ENV: 'development' }, mounted())).code).toBe(
      'SHARED_SECRET_AMBIGUOUS',
    );
  });

  it('a mount point that is not a readable file stops the service (SHARED_SECRET_UNREADABLE)', () => {
    const read = (path: string) => {
      if (path === DEFAULT_SHARED_SECRET_PATHS.internalKey) throw new Error('EISDIR');
      return mounted()(path);
    };
    expect(errorOf(() => loadConfig(production, read)).code).toBe('SHARED_SECRET_UNREADABLE');
  });

  it('no internal key file → INTERNAL_KEY_MISSING; a short one → INTERNAL_KEY_TOO_SHORT', () => {
    expect(errorOf(() => loadConfig(production, secrets())).code).toBe('INTERNAL_KEY_MISSING');
    expect(
      errorOf(() =>
        loadConfig(
          production,
          secrets({ [DEFAULT_SHARED_SECRET_PATHS.internalKey]: 'k'.repeat(31) }),
        ),
      ).code,
    ).toBe('INTERNAL_KEY_TOO_SHORT');
  });

  it('the webhook secret may be absent (T151: acceptsPayments=false) but not short', () => {
    const none = loadConfig(
      production,
      secrets({ [DEFAULT_SHARED_SECRET_PATHS.internalKey]: KEY }),
    );
    expect(none.webhookHmacKey).toBe('');
    expect(none.sharedSecretSources.webhookSecret).toBe('none');
    const short = errorOf(() =>
      loadConfig(
        production,
        secrets({
          [DEFAULT_SHARED_SECRET_PATHS.internalKey]: KEY,
          [DEFAULT_SHARED_SECRET_PATHS.webhookSecret]: 'short-webhook-secret',
        }),
      ),
    );
    expect(short.code).toBe('WEBHOOK_SECRET_TOO_SHORT');
    expect(short.message).not.toContain('short-webhook-secret');
  });
});

/**
 * T171b — the RPC provider URLs carry the provider's API key: Docker secret
 * files (DEFERRED_BACKLOG `ThirdPartyCredentialsInPlainEnv`). The running
 * process: startupWiring.test.ts.
 */
describe('loadConfig — RPC URLs from secret files (T171b)', () => {
  const production = { NODE_ENV: 'production' };
  const mounted = (files: Record<string, string | null> = {}) =>
    secrets({
      [DEFAULT_SHARED_SECRET_PATHS.internalKey]: KEY,
      [DEFAULT_CREDENTIAL_PATHS.rpcPrimaryUrl]: PRIMARY_URL,
      [DEFAULT_CREDENTIAL_PATHS.rpcBackupUrl]: BACKUP_URL,
      ...files,
    });

  it('reads both URLs from the compose mount points in production', () => {
    expect(DEFAULT_CREDENTIAL_PATHS).toEqual({
      rpcPrimaryUrl: '/run/secrets/ethereum_rpc_primary_url',
      rpcBackupUrl: '/run/secrets/ethereum_rpc_backup_url',
    });
    const config = loadConfig(production, mounted());
    expect(config.rpc.providers.map((p) => p.url)).toEqual([PRIMARY_URL, BACKUP_URL]);
    expect(config.credentialSources).toEqual({ rpcPrimaryUrl: 'file', rpcBackupUrl: 'file' });
  });

  it('no primary file → RPC_NOT_CONFIGURED; no backup file → one provider', () => {
    const missing = errorOf(() =>
      loadConfig(production, mounted({ [DEFAULT_CREDENTIAL_PATHS.rpcPrimaryUrl]: null })),
    );
    expect(missing.code).toBe('RPC_NOT_CONFIGURED');
    const single = loadConfig(
      production,
      mounted({ [DEFAULT_CREDENTIAL_PATHS.rpcBackupUrl]: null }),
    );
    expect(single.rpc.providers).toHaveLength(1);
    expect(single.credentialSources.rpcBackupUrl).toBe('none');
  });

  it.each([
    ['production', 'ETHEREUM_RPC_PRIMARY_URL'],
    ['production', 'ETHEREUM_RPC_BACKUP_URL'],
    ['', 'ETHEREUM_RPC_PRIMARY_URL'],
    ['Development', 'ETHEREUM_RPC_BACKUP_URL'],
  ])('NODE_ENV=%j refuses %s as a plain variable (CREDENTIAL_IN_ENV)', (nodeEnv, variable) => {
    const error = errorOf(() =>
      loadConfig({ ...production, NODE_ENV: nodeEnv, [variable]: PRIMARY_URL }, mounted()),
    );
    expect(error.code).toBe('CREDENTIAL_IN_ENV');
    expect(error.message).not.toContain(RPC_KEY);
  });

  it('development reads the variables; file and variable together is ambiguous', () => {
    const config = loadConfig(baseEnv, secrets());
    expect(config.credentialSources).toEqual({ rpcPrimaryUrl: 'env', rpcBackupUrl: 'env' });
    const error = errorOf(() =>
      loadConfig(baseEnv, secrets({ [DEFAULT_CREDENTIAL_PATHS.rpcPrimaryUrl]: PRIMARY_URL })),
    );
    expect(error.code).toBe('CREDENTIAL_AMBIGUOUS');
    expect(error.message).not.toContain(RPC_KEY);
  });

  it('a mount point that is not a readable file stops the service (CREDENTIAL_UNREADABLE)', () => {
    const read = (path: string) => {
      if (path === DEFAULT_CREDENTIAL_PATHS.rpcBackupUrl) throw new Error('EISDIR');
      return mounted()(path);
    };
    expect(errorOf(() => loadConfig(production, read)).code).toBe('CREDENTIAL_UNREADABLE');
  });

  it('a file URL that is not a URL is refused without echoing it', () => {
    const error = errorOf(() =>
      loadConfig(
        production,
        mounted({ [DEFAULT_CREDENTIAL_PATHS.rpcPrimaryUrl]: `not a url ${RPC_KEY}` }),
      ),
    );
    expect(error.code).toBe('INVALID_CONFIG');
    expect(error.message).not.toContain(RPC_KEY);
  });
});
