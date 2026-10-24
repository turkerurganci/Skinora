import { describe, expect, it } from 'vitest';
import {
  CredentialError,
  DEFAULT_CREDENTIAL_PATHS,
  loadProviderCredentials,
} from './providerCredentials.js';

/**
 * T171b — the Steam Web API key and the Redis password come from Docker
 * secret files (DEFERRED_BACKLOG `ThirdPartyCredentialsInPlainEnv`). The
 * running process is covered by startupWiring.test.ts.
 */
const STEAM_KEY = 'TESTSTEAMKEY'.padEnd(32, '7');
const REDIS_PASSWORD = 'test-redis-password-'.padEnd(40, 'r');

type Files = Record<string, string | null | Error>;

function reader(files: Files) {
  return (path: string): string | null => {
    const entry = files[path];
    if (entry instanceof Error) throw entry;
    return entry ?? null;
  };
}

const MOUNTED: Files = {
  [DEFAULT_CREDENTIAL_PATHS.steamApiKey]: STEAM_KEY,
  [DEFAULT_CREDENTIAL_PATHS.redisPassword]: REDIS_PASSWORD,
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
  it('reads the key and puts the file password into a Redis URL that carries none', () => {
    const loaded = loadProviderCredentials(
      { NODE_ENV: 'production', REDIS_URL: 'redis://skinora-redis:6379' },
      reader(MOUNTED),
    );
    expect(loaded.steamApiKey).toBe(STEAM_KEY);
    const url = new URL(loaded.redisUrl);
    expect(url.password).toBe(REDIS_PASSWORD);
    expect(url.hostname).toBe('skinora-redis');
    expect(url.port).toBe('6379');
    expect(loaded.sources).toEqual({ steamApiKey: 'file', redisPassword: 'file' });
  });

  it('keeps the in-memory cache when REDIS_URL is empty (no password needed)', () => {
    const loaded = loadProviderCredentials({ NODE_ENV: 'production' }, reader(MOUNTED));
    expect(loaded.redisUrl).toBe('');
    expect(loaded.sources.redisPassword).toBe('none');
  });

  it('keeps the Steam key optional — no file is an empty key', () => {
    const loaded = loadProviderCredentials({ NODE_ENV: 'production' }, reader({}));
    expect(loaded.steamApiKey).toBe('');
    expect(loaded.sources.steamApiKey).toBe('none');
  });

  it.each(['production', 'staging', '', undefined, 'Development'])(
    'refuses a plain STEAM_API_KEY when NODE_ENV is %j',
    (nodeEnv) => {
      const err = refusal(() =>
        loadProviderCredentials({ NODE_ENV: nodeEnv, STEAM_API_KEY: STEAM_KEY }, reader({})),
      );
      expect(err.code).toBe('CREDENTIAL_IN_ENV');
      expect(err.message).not.toContain(STEAM_KEY);
    },
  );

  it('refuses a REDIS_URL that carries a password outside development, without echoing it', () => {
    const err = refusal(() =>
      loadProviderCredentials(
        { NODE_ENV: 'production', REDIS_URL: `redis://:${REDIS_PASSWORD}@skinora-redis:6379` },
        reader({}),
      ),
    );
    expect(err.code).toBe('CREDENTIAL_IN_ENV');
    expect(err.message).not.toContain(REDIS_PASSWORD);
  });

  it('accepts a password inside REDIS_URL in development (no file)', () => {
    const url = `redis://:${REDIS_PASSWORD}@localhost:6379`;
    const loaded = loadProviderCredentials({ NODE_ENV: 'development', REDIS_URL: url }, reader({}));
    expect(loaded.redisUrl).toBe(url);
    expect(loaded.sources.redisPassword).toBe('env');
  });

  it('file and URL password together are ambiguous', () => {
    const err = refusal(() =>
      loadProviderCredentials(
        { NODE_ENV: 'development', REDIS_URL: 'redis://:other@localhost:6379' },
        reader(MOUNTED),
      ),
    );
    expect(err.code).toBe('CREDENTIAL_AMBIGUOUS');
  });

  it('file and STEAM_API_KEY together are ambiguous', () => {
    const err = refusal(() =>
      loadProviderCredentials({ NODE_ENV: 'test', STEAM_API_KEY: 'x' }, reader(MOUNTED)),
    );
    expect(err.code).toBe('CREDENTIAL_AMBIGUOUS');
  });

  it.each([
    ['steamApiKey', DEFAULT_CREDENTIAL_PATHS.steamApiKey],
    ['redisPassword', DEFAULT_CREDENTIAL_PATHS.redisPassword],
  ])('a mount that is not a readable file (%s) stops the service', (_name, path) => {
    const err = refusal(() =>
      loadProviderCredentials(
        { NODE_ENV: 'production', REDIS_URL: 'redis://skinora-redis:6379' },
        reader({ ...MOUNTED, [path]: new Error('EISDIR') }),
      ),
    );
    expect(err.code).toBe('CREDENTIAL_UNREADABLE');
  });

  it.each(['has,comma', 'has space', 'has/slash', 'has@at', 'has:colon'])(
    'refuses a Redis password outside the shared character set (%j)',
    (password) => {
      const err = refusal(() =>
        loadProviderCredentials(
          { NODE_ENV: 'production', REDIS_URL: 'redis://skinora-redis:6379' },
          reader({ ...MOUNTED, [DEFAULT_CREDENTIAL_PATHS.redisPassword]: password }),
        ),
      );
      expect(err.code).toBe('CREDENTIAL_INVALID');
      expect(err.message).not.toContain(password);
    },
  );

  it('refuses a REDIS_URL that is not a redis URL, without echoing it', () => {
    const err = refusal(() =>
      loadProviderCredentials(
        { NODE_ENV: 'production', REDIS_URL: 'http://secret-looking-host:6379' },
        reader(MOUNTED),
      ),
    );
    expect(err.code).toBe('CREDENTIAL_INVALID');
    expect(err.message).not.toContain('secret-looking-host');
  });
});
