import { describe, expect, it } from 'vitest';
import { ConfigError, DEFAULT_SECRET_PATHS, loadConfig, providerHost } from './index.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';

const MNEMONIC = `${'abandon '.repeat(11)}about`;
const KEY = 'k'.repeat(64);
const RPC_KEY = 'alchemyApiKeyThatMustNeverBeLogged';
const PRIMARY_URL = `https://eth-sepolia.g.alchemy.com/v2/${RPC_KEY}`;
const BACKUP_URL = 'https://ethereum-sepolia.core.chainstack.com/abcdef0123456789';
const signer = new EthereumHdWallet(MNEMONIC).deriveSigner(20);

const baseEnv = {
  INTERNAL_KEY: KEY,
  ETHEREUM_RPC_PRIMARY_URL: PRIMARY_URL,
  ETHEREUM_RPC_BACKUP_URL: BACKUP_URL,
};

/** Secret files as a map path → content; anything else does not exist. */
function secrets(files: Record<string, string | null> = {}) {
  const all: Record<string, string | null> = {
    [DEFAULT_SECRET_PATHS.hdMnemonic]: MNEMONIC,
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

  it('reads the seed from ETHEREUM_HD_MNEMONIC_FILE when set', () => {
    const config = loadConfig(
      { ...baseEnv, ETHEREUM_HD_MNEMONIC_FILE: '/elsewhere/seed' },
      secrets({ [DEFAULT_SECRET_PATHS.hdMnemonic]: null, '/elsewhere/seed': MNEMONIC }),
    );
    expect(config.hdMnemonic).toBe(MNEMONIC);
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
});

describe('providerHost', () => {
  it('shows the host only — the path carries the API key', () => {
    expect(providerHost(PRIMARY_URL)).toBe('eth-sepolia.g.alchemy.com');
    expect(providerHost(PRIMARY_URL)).not.toContain(RPC_KEY);
  });
});
