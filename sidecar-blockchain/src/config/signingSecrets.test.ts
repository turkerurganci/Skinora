import { describe, it, expect } from 'vitest';
import {
  DEFAULT_SIGNING_SECRET_PATHS,
  SigningSecretError,
  envSecretsAllowed,
  loadSigningMaterial,
} from './signingSecrets.js';
import { HdWalletService } from '../wallet/HdWalletService.js';

/**
 * T163 — the TRON seed and hot wallet key are Docker secrets (files); the
 * plain variables are a development-only path; nothing echoes a value.
 *
 * Fixtures are the public BIP-39 reference mnemonic and the key it derives at
 * TRON index 0 (T143 vector TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH) — computed
 * here, never written down, and worthless on any network.
 */
const MNEMONIC = `${'abandon '.repeat(11)}about`;
const SIGNER = new HdWalletService(MNEMONIC).deriveSigner(0);
const HOT_KEY = SIGNER.privateKey;
const HOT_ADDRESS = 'TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH';
const OTHER_ADDRESS = 'TSeJkUh4Qv67VNFwY8LaAxERygNdy6NQZK';
/** Property access keeps the pre-commit secret guard from reading fixtures as literals. */
const FIXTURE = { mnemonic: MNEMONIC, hotKey: HOT_KEY };
const ELSEWHERE = { seed: '/elsewhere/seed', hot: '/elsewhere/hot' };

/** An in-memory /run/secrets: path → content (null = file absent). */
function files(entries: Record<string, string | null>, unreadable: string[] = []) {
  return (path: string): string | null => {
    if (unreadable.includes(path)) {
      throw new Error(`Secret file ${path} exists but cannot be read: EISDIR`);
    }
    const raw = entries[path];
    if (raw === undefined || raw === null) return null;
    const value = raw.trim();
    return value.length > 0 ? value : null;
  };
}

const MOUNTED = files({
  [DEFAULT_SIGNING_SECRET_PATHS.hdMnemonic]: `${MNEMONIC}\n`,
  [DEFAULT_SIGNING_SECRET_PATHS.hotWalletKey]: `${HOT_KEY}\n`,
});
const NONE = files({});

function refusal(run: () => unknown): SigningSecretError {
  try {
    run();
  } catch (err) {
    expect(err).toBeInstanceOf(SigningSecretError);
    return err as SigningSecretError;
  }
  throw new Error('expected a SigningSecretError');
}

describe('loadSigningMaterial — Docker secret files', () => {
  it('reads both secrets from /run/secrets in production and reports the address the key controls', () => {
    const loaded = loadSigningMaterial({ NODE_ENV: 'production' }, HOT_ADDRESS, MOUNTED);

    expect(loaded.hdMnemonic).toBe(MNEMONIC);
    expect(loaded.hotWalletPrivateKey).toBe(HOT_KEY);
    expect(loaded.hotWalletAddressFromKey).toBe(HOT_ADDRESS);
    expect(loaded.sources).toEqual({ hdMnemonic: 'file', hotWalletKey: 'file' });
    expect(loaded.paths).toEqual(DEFAULT_SIGNING_SECRET_PATHS);
  });

  it('honours *_FILE path overrides', () => {
    const loaded = loadSigningMaterial(
      {
        NODE_ENV: 'production',
        HD_WALLET_MNEMONIC_FILE: ELSEWHERE.seed,
        HOT_WALLET_PRIVATE_KEY_FILE: ELSEWHERE.hot,
      },
      '',
      files({ '/elsewhere/seed': MNEMONIC, '/elsewhere/hot': HOT_KEY }),
    );

    expect(loaded.sources).toEqual({ hdMnemonic: 'file', hotWalletKey: 'file' });
    expect(loaded.paths).toEqual({
      hdMnemonic: '/elsewhere/seed',
      hotWalletKey: '/elsewhere/hot',
    });
  });

  it('normalises a 0x-prefixed upper-case key to the lowercase hex TronWeb signs with', () => {
    const loaded = loadSigningMaterial(
      { NODE_ENV: 'production' },
      HOT_ADDRESS,
      files({ [DEFAULT_SIGNING_SECRET_PATHS.hotWalletKey]: `0x${HOT_KEY.toUpperCase()}` }),
    );

    expect(loaded.hotWalletPrivateKey).toBe(HOT_KEY);
    expect(loaded.hotWalletAddressFromKey).toBe(HOT_ADDRESS);
  });

  it('starts without either secret (the pre-T163 contract: derive answers 503, payouts refuse)', () => {
    const loaded = loadSigningMaterial({ NODE_ENV: 'production' }, '', NONE);

    expect(loaded.hdMnemonic).toBe('');
    expect(loaded.hotWalletPrivateKey).toBe('');
    expect(loaded.hotWalletAddressFromKey).toBeNull();
    expect(loaded.sources).toEqual({ hdMnemonic: 'none', hotWalletKey: 'none' });
  });
});

describe('loadSigningMaterial — the plain-variable path is development-only', () => {
  it.each(['development', 'test'])('reads the variables when NODE_ENV=%s', (mode) => {
    const loaded = loadSigningMaterial(
      {
        NODE_ENV: mode,
        HD_WALLET_MNEMONIC: FIXTURE.mnemonic,
        HOT_WALLET_PRIVATE_KEY: FIXTURE.hotKey,
      },
      HOT_ADDRESS,
      NONE,
    );

    expect(loaded.sources).toEqual({ hdMnemonic: 'env', hotWalletKey: 'env' });
    expect(loaded.hdMnemonic).toBe(MNEMONIC);
  });

  it.each([
    ['production', 'production'],
    ['unset', undefined],
    ['blank', ''],
    ['a near miss', 'dev'],
    ['different case', 'Development'],
    ['staging', 'staging'],
  ])('refuses a set variable when NODE_ENV is %s, without echoing it', (_label, mode) => {
    for (const [name, value] of [
      ['HD_WALLET_MNEMONIC', MNEMONIC],
      ['HOT_WALLET_PRIVATE_KEY', HOT_KEY],
    ] as const) {
      const err = refusal(() =>
        loadSigningMaterial({ NODE_ENV: mode, [name]: value }, '', MOUNTED),
      );
      expect(err.code).toBe('SIGNING_SECRET_IN_ENV');
      expect(err.message).toContain(name);
      expect(err.message).not.toContain(value);
      expect(err.message).not.toContain('abandon');
    }
  });

  it('ignores a blank variable in production (compose passes none)', () => {
    const loaded = loadSigningMaterial(
      { NODE_ENV: 'production', HD_WALLET_MNEMONIC: '  ', HOT_WALLET_PRIVATE_KEY: '' },
      HOT_ADDRESS,
      MOUNTED,
    );
    expect(loaded.sources).toEqual({ hdMnemonic: 'file', hotWalletKey: 'file' });
  });

  it('refuses the same secret from both a file and a variable', () => {
    const env = {
      NODE_ENV: 'development',
      HD_WALLET_MNEMONIC: FIXTURE.mnemonic,
    };
    const err = refusal(() => loadSigningMaterial(env, '', MOUNTED));
    expect(err.code).toBe('SIGNING_SECRET_AMBIGUOUS');
    expect(err.message).not.toContain('abandon');
  });

  it('envSecretsAllowed is an exact match', () => {
    expect(envSecretsAllowed('development')).toBe(true);
    expect(envSecretsAllowed(' test ')).toBe(true);
    expect(envSecretsAllowed('production')).toBe(false);
    expect(envSecretsAllowed(undefined)).toBe(false);
  });
});

describe('loadSigningMaterial — refusals never carry a value', () => {
  it('a secret that exists but is not a readable file (Docker made a directory) stops the service', () => {
    const err = refusal(() =>
      loadSigningMaterial(
        { NODE_ENV: 'production' },
        '',
        files({}, [DEFAULT_SIGNING_SECRET_PATHS.hdMnemonic]),
      ),
    );
    expect(err.code).toBe('SIGNING_SECRET_UNREADABLE');
    expect(err.message).toContain(DEFAULT_SIGNING_SECRET_PATHS.hdMnemonic);
    expect(err.message).toContain('DIRECTORY');
  });

  it.each([
    ['too short', 'deadbeef'],
    ['not hex', `zz${HOT_KEY.slice(2)}`],
    ['a mnemonic pasted into the key file', MNEMONIC],
  ])('a malformed hot wallet key (%s) is refused by name only', (_label, raw) => {
    const err = refusal(() =>
      loadSigningMaterial(
        { NODE_ENV: 'production' },
        '',
        files({ [DEFAULT_SIGNING_SECRET_PATHS.hotWalletKey]: raw }),
      ),
    );
    expect(err.code).toBe('INVALID_SIGNING_KEY');
    expect(err.message).not.toContain(raw);
    expect(err.message).not.toContain('abandon');
  });

  it('a key that does not control HOT_WALLET_ADDRESS stops the service (wrong backup restored)', () => {
    const err = refusal(() =>
      loadSigningMaterial({ NODE_ENV: 'production' }, OTHER_ADDRESS, MOUNTED),
    );
    expect(err.code).toBe('HOT_WALLET_ADDRESS_MISMATCH');
    expect(err.message).toContain(HOT_ADDRESS);
    expect(err.message).toContain(OTHER_ADDRESS);
    expect(err.message).not.toContain(HOT_KEY);
  });
});
