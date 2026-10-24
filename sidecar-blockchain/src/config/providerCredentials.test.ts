import { describe, expect, it } from 'vitest';
import {
  CredentialError,
  DEFAULT_CREDENTIAL_PATHS,
  loadProviderCredentials,
} from './providerCredentials.js';

/**
 * T171b — the TronGrid API keys come from Docker secret files
 * (DEFERRED_BACKLOG `ThirdPartyCredentialsInPlainEnv`). The running process
 * is covered by signingSecretsStartup.test.ts.
 */
const KEY = 'test-trongrid-key-'.padEnd(36, 't');
const KEY_2 = 'test-trongrid-second-'.padEnd(36, 's');

type Files = Record<string, string | null | Error>;

function reader(files: Files) {
  return (path: string): string | null => {
    const entry = files[path];
    if (entry instanceof Error) throw entry;
    return entry ?? null;
  };
}

const MOUNTED: Files = {
  [DEFAULT_CREDENTIAL_PATHS.tronApiKey]: KEY,
  [DEFAULT_CREDENTIAL_PATHS.tronApiKeySecondary]: KEY_2,
};

function refusal(fn: () => unknown): CredentialError {
  try {
    fn();
  } catch (err) {
    expect(err).toBeInstanceOf(CredentialError);
    return err as CredentialError;
  }
  throw new Error('expected a refusal');
}

describe('loadProviderCredentials (T171b)', () => {
  it('reads both keys from their files in production', () => {
    const loaded = loadProviderCredentials({ NODE_ENV: 'production' }, reader(MOUNTED));
    expect(loaded).toEqual({
      tronApiKey: KEY,
      tronApiKeySecondary: KEY_2,
      sources: { tronApiKey: 'file', tronApiKeySecondary: 'file' },
    });
  });

  it('honours the *_FILE path overrides', () => {
    const loaded = loadProviderCredentials(
      { NODE_ENV: 'production', TRON_API_KEY_FILE: '/x/a', TRON_API_KEY_SECONDARY_FILE: '/x/b' },
      reader({ '/x/a': KEY, '/x/b': KEY_2 }),
    );
    expect(loaded.tronApiKey).toBe(KEY);
    expect(loaded.tronApiKeySecondary).toBe(KEY_2);
  });

  it('keeps both keys optional — no file is an empty key, not a refusal', () => {
    const loaded = loadProviderCredentials({ NODE_ENV: 'production' }, reader({}));
    expect(loaded).toEqual({
      tronApiKey: '',
      tronApiKeySecondary: '',
      sources: { tronApiKey: 'none', tronApiKeySecondary: 'none' },
    });
  });

  it.each(['production', 'staging', '', undefined, 'Development', 'development '])(
    'refuses a plain TRON_API_KEY when NODE_ENV is %j',
    (nodeEnv) => {
      const err = refusal(() =>
        loadProviderCredentials({ NODE_ENV: nodeEnv, TRON_API_KEY: KEY }, reader({})),
      );
      expect(err.code).toBe('CREDENTIAL_IN_ENV');
      expect(err.message).not.toContain(KEY);
    },
  );

  it('refuses a plain TRON_API_KEY_SECONDARY in production even with the file there', () => {
    const err = refusal(() =>
      loadProviderCredentials(
        { NODE_ENV: 'production', TRON_API_KEY_SECONDARY: KEY_2 },
        reader(MOUNTED),
      ),
    );
    expect(err.code).toBe('CREDENTIAL_IN_ENV');
    expect(err.message).not.toContain(KEY_2);
  });

  it.each(['development', 'test'])('reads the variables when NODE_ENV is exactly %s', (nodeEnv) => {
    const loaded = loadProviderCredentials(
      { NODE_ENV: nodeEnv, TRON_API_KEY: KEY, TRON_API_KEY_SECONDARY: KEY_2 },
      reader({}),
    );
    expect(loaded.tronApiKey).toBe(KEY);
    expect(loaded.sources).toEqual({ tronApiKey: 'env', tronApiKeySecondary: 'env' });
  });

  it('file and variable together are ambiguous, even in development', () => {
    const err = refusal(() =>
      loadProviderCredentials({ NODE_ENV: 'development', TRON_API_KEY: KEY }, reader(MOUNTED)),
    );
    expect(err.code).toBe('CREDENTIAL_AMBIGUOUS');
    expect(err.message).not.toContain(KEY);
  });

  it('a mount that is not a readable file (Docker directory) stops the service', () => {
    const err = refusal(() =>
      loadProviderCredentials(
        { NODE_ENV: 'production' },
        reader({ [DEFAULT_CREDENTIAL_PATHS.tronApiKey]: new Error('EISDIR') }),
      ),
    );
    expect(err.code).toBe('CREDENTIAL_UNREADABLE');
    expect(err.message).toContain(DEFAULT_CREDENTIAL_PATHS.tronApiKey);
  });
});
