import { describe, expect, it } from 'vitest';
import { InternalKeyError } from './internalKey.js';
import { DEFAULT_INTERNAL_KEY_PATH, SharedSecretError, loadInternalKey } from './sharedSecrets.js';

/**
 * T171a — the Steam service's internal key comes from its Docker secret file
 * (DEFERRED_BACKLOG `SharedSecretsInPlainEnv`). The running process is
 * covered by startupWiring.test.ts.
 */
const KEY = 'test-internal-key-'.padEnd(64, 'k');

function reader(files: Record<string, string | null | Error>) {
  return (path: string): string | null => {
    const entry = files[path];
    if (entry instanceof Error) throw entry;
    return entry ?? null;
  };
}

function codeOf(fn: () => unknown): string | undefined {
  try {
    fn();
    return undefined;
  } catch (err) {
    if (err instanceof SharedSecretError || err instanceof InternalKeyError) return err.code;
    throw err;
  }
}

describe('loadInternalKey', () => {
  it('reads the compose mount point in production', () => {
    expect(DEFAULT_INTERNAL_KEY_PATH).toBe('/run/secrets/steam_sidecar_internal_key');
    expect(
      loadInternalKey({ NODE_ENV: 'production' }, reader({ [DEFAULT_INTERNAL_KEY_PATH]: KEY })),
    ).toEqual({ internalKey: KEY, source: 'file', path: DEFAULT_INTERNAL_KEY_PATH });
  });

  it('INTERNAL_KEY_FILE overrides the path', () => {
    expect(
      loadInternalKey(
        { NODE_ENV: 'production', INTERNAL_KEY_FILE: '/x/key' },
        reader({ '/x/key': KEY }),
      ).path,
    ).toBe('/x/key');
  });

  it.each(['production', '', 'Development', ' test', undefined])(
    'NODE_ENV=%j refuses INTERNAL_KEY as a plain variable (SHARED_SECRET_IN_ENV)',
    (nodeEnv) => {
      expect(
        codeOf(() => loadInternalKey({ NODE_ENV: nodeEnv, INTERNAL_KEY: KEY }, reader({}))),
      ).toBe('SHARED_SECRET_IN_ENV');
    },
  );

  it.each(['development', 'test'])('NODE_ENV=%s reads the variable', (nodeEnv) => {
    expect(loadInternalKey({ NODE_ENV: nodeEnv, INTERNAL_KEY: KEY }, reader({})).source).toBe(
      'env',
    );
  });

  it('file and variable together is ambiguous', () => {
    expect(
      codeOf(() =>
        loadInternalKey(
          { NODE_ENV: 'test', INTERNAL_KEY: KEY },
          reader({ [DEFAULT_INTERNAL_KEY_PATH]: KEY }),
        ),
      ),
    ).toBe('SHARED_SECRET_AMBIGUOUS');
  });

  it('an unreadable mount point, a missing and a short key stop the service', () => {
    expect(
      codeOf(() =>
        loadInternalKey(
          { NODE_ENV: 'production' },
          reader({ [DEFAULT_INTERNAL_KEY_PATH]: new Error('EISDIR') }),
        ),
      ),
    ).toBe('SHARED_SECRET_UNREADABLE');
    expect(codeOf(() => loadInternalKey({ NODE_ENV: 'production' }, reader({})))).toBe(
      'INTERNAL_KEY_MISSING',
    );
    expect(
      codeOf(() =>
        loadInternalKey(
          { NODE_ENV: 'production' },
          reader({ [DEFAULT_INTERNAL_KEY_PATH]: 'k'.repeat(31) }),
        ),
      ),
    ).toBe('INTERNAL_KEY_TOO_SHORT');
  });

  it('never puts the key in an error message', () => {
    for (const run of [
      () => loadInternalKey({ NODE_ENV: 'production', INTERNAL_KEY: KEY }, reader({})),
      () =>
        loadInternalKey(
          { NODE_ENV: 'test', INTERNAL_KEY: KEY },
          reader({ [DEFAULT_INTERNAL_KEY_PATH]: KEY }),
        ),
      () =>
        loadInternalKey(
          { NODE_ENV: 'production' },
          reader({ [DEFAULT_INTERNAL_KEY_PATH]: 'short-key' }),
        ),
    ]) {
      try {
        run();
        throw new Error('expected a refusal');
      } catch (err) {
        expect((err as Error).message).not.toContain(KEY);
        expect((err as Error).message).not.toContain('short-key');
      }
    }
  });
});
