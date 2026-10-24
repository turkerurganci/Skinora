import { describe, expect, it } from 'vitest';
import { InternalKeyError } from './internalKey.js';
import {
  DEFAULT_SHARED_SECRET_PATHS,
  SharedSecretError,
  loadSharedSecrets,
} from './sharedSecrets.js';

/**
 * T171a — the TRON service's internal key and webhook secret come from Docker
 * secret files (DEFERRED_BACKLOG `SharedSecretsInPlainEnv`). The running
 * process is covered by signingSecretsStartup.test.ts.
 */
const KEY = 'test-internal-key-'.padEnd(64, 'k');
const WEBHOOK = 'test-webhook-secret-'.padEnd(64, 'w');

type Files = Record<string, string | null | Error>;

/** A fake mount table: path → content, null (absent) or an Error (unreadable). */
function reader(files: Files) {
  return (path: string): string | null => {
    const entry = files[path];
    if (entry instanceof Error) throw entry;
    return entry ?? null;
  };
}

const MOUNTED: Files = {
  [DEFAULT_SHARED_SECRET_PATHS.internalKey]: KEY,
  [DEFAULT_SHARED_SECRET_PATHS.webhookSecret]: WEBHOOK,
};

function codeOf(fn: () => unknown): string | undefined {
  try {
    fn();
    return undefined;
  } catch (err) {
    if (err instanceof SharedSecretError || err instanceof InternalKeyError) return err.code;
    throw err;
  }
}

describe('loadSharedSecrets', () => {
  it('reads both secrets from the compose mount points in production', () => {
    const shared = loadSharedSecrets({ NODE_ENV: 'production' }, reader(MOUNTED));
    expect(shared).toMatchObject({
      internalKey: KEY,
      webhookSecret: WEBHOOK,
      sources: { internalKey: 'file', webhookSecret: 'file' },
    });
  });

  it('pins the mount points to the compose secret names the backend reads too', () => {
    expect(DEFAULT_SHARED_SECRET_PATHS).toEqual({
      internalKey: '/run/secrets/blockchain_sidecar_internal_key',
      webhookSecret: '/run/secrets/blockchain_webhook_secret',
    });
  });

  it('*_FILE overrides the path', () => {
    const shared = loadSharedSecrets(
      { NODE_ENV: 'production', INTERNAL_KEY_FILE: '/x/key', WEBHOOK_SECRET_FILE: '/x/hook' },
      reader({ '/x/key': KEY, '/x/hook': WEBHOOK }),
    );
    expect(shared.paths).toEqual({ internalKey: '/x/key', webhookSecret: '/x/hook' });
  });

  it.each([
    ['production', 'INTERNAL_KEY'],
    ['production', 'WEBHOOK_SECRET'],
    [undefined, 'INTERNAL_KEY'],
    ['', 'WEBHOOK_SECRET'],
    ['Development', 'INTERNAL_KEY'],
    [' development', 'WEBHOOK_SECRET'],
  ])('NODE_ENV=%s refuses %s as a plain variable (SHARED_SECRET_IN_ENV)', (nodeEnv, variable) => {
    const value = variable === 'INTERNAL_KEY' ? KEY : WEBHOOK;
    expect(
      codeOf(() => loadSharedSecrets({ NODE_ENV: nodeEnv, [variable]: value }, reader({}))),
    ).toBe('SHARED_SECRET_IN_ENV');
  });

  it.each(['development', 'test'])('NODE_ENV=%s reads the variables', (nodeEnv) => {
    const shared = loadSharedSecrets(
      { NODE_ENV: nodeEnv, INTERNAL_KEY: KEY, WEBHOOK_SECRET: WEBHOOK },
      reader({}),
    );
    expect(shared.sources).toEqual({ internalKey: 'env', webhookSecret: 'env' });
  });

  it('file and variable together is ambiguous', () => {
    expect(
      codeOf(() =>
        loadSharedSecrets({ NODE_ENV: 'development', INTERNAL_KEY: KEY }, reader(MOUNTED)),
      ),
    ).toBe('SHARED_SECRET_AMBIGUOUS');
  });

  it('a mount point that is not a readable file stops the service (SHARED_SECRET_UNREADABLE)', () => {
    const files = { ...MOUNTED, [DEFAULT_SHARED_SECRET_PATHS.webhookSecret]: new Error('EISDIR') };
    expect(codeOf(() => loadSharedSecrets({ NODE_ENV: 'production' }, reader(files)))).toBe(
      'SHARED_SECRET_UNREADABLE',
    );
  });

  it('no internal key → INTERNAL_KEY_MISSING; short → INTERNAL_KEY_TOO_SHORT', () => {
    const none = { [DEFAULT_SHARED_SECRET_PATHS.webhookSecret]: WEBHOOK };
    expect(codeOf(() => loadSharedSecrets({ NODE_ENV: 'production' }, reader(none)))).toBe(
      'INTERNAL_KEY_MISSING',
    );
    const short = { ...MOUNTED, [DEFAULT_SHARED_SECRET_PATHS.internalKey]: 'k'.repeat(31) };
    expect(codeOf(() => loadSharedSecrets({ NODE_ENV: 'production' }, reader(short)))).toBe(
      'INTERNAL_KEY_TOO_SHORT',
    );
  });

  it('no webhook secret → WEBHOOK_SECRET_MISSING; short → WEBHOOK_SECRET_TOO_SHORT (also in development)', () => {
    const none = { [DEFAULT_SHARED_SECRET_PATHS.internalKey]: KEY };
    expect(codeOf(() => loadSharedSecrets({ NODE_ENV: 'production' }, reader(none)))).toBe(
      'WEBHOOK_SECRET_MISSING',
    );
    expect(
      codeOf(() =>
        loadSharedSecrets(
          { NODE_ENV: 'development', INTERNAL_KEY: KEY, WEBHOOK_SECRET: 'w'.repeat(31) },
          reader({}),
        ),
      ),
    ).toBe('WEBHOOK_SECRET_TOO_SHORT');
    expect(
      loadSharedSecrets(
        { NODE_ENV: 'production' },
        reader({ ...MOUNTED, [DEFAULT_SHARED_SECRET_PATHS.webhookSecret]: 'w'.repeat(32) }),
      ).webhookSecret,
    ).toHaveLength(32);
  });

  it('never puts a secret in an error message', () => {
    const cases: Array<() => unknown> = [
      () => loadSharedSecrets({ NODE_ENV: 'production', INTERNAL_KEY: KEY }, reader({})),
      () => loadSharedSecrets({ NODE_ENV: 'production', WEBHOOK_SECRET: WEBHOOK }, reader(MOUNTED)),
      () =>
        loadSharedSecrets({ NODE_ENV: 'development', WEBHOOK_SECRET: WEBHOOK }, reader(MOUNTED)),
      () =>
        loadSharedSecrets(
          { NODE_ENV: 'production' },
          reader({ ...MOUNTED, [DEFAULT_SHARED_SECRET_PATHS.webhookSecret]: 'short-webhook' }),
        ),
    ];
    for (const run of cases) {
      try {
        run();
        throw new Error('expected a refusal');
      } catch (err) {
        const message = (err as Error).message;
        expect(message).not.toContain(KEY);
        expect(message).not.toContain(WEBHOOK);
        expect(message).not.toContain('short-webhook');
      }
    }
  });
});
