import { readSecretFile } from './secretFile.js';
import { requireInternalKey } from './internalKey.js';

/**
 * T171a — the Steam service's one shared secret, its internal key
 * (X-Internal-Key, 05 §3.4), comes from a Docker secret file — the SAME file
 * compose mounts into the backend (DEFERRED_BACKLOG `SharedSecretsInPlainEnv`;
 * the TRON service's config/sharedSecrets.ts, DEPLOY_RUNBOOK §L.9).
 *
 * <para>
 * `INTERNAL_KEY_FILE` overrides the path. The `INTERNAL_KEY` variable is read
 * only when `NODE_ENV` is exactly `development` or `test`; anywhere else a
 * non-empty variable stops the service (`SHARED_SECRET_IN_ENV`). File and
 * variable together: `SHARED_SECRET_AMBIGUOUS`. A path that exists but is not
 * a readable file: `SHARED_SECRET_UNREADABLE`. Missing / short keep their T161
 * codes (`INTERNAL_KEY_MISSING` / `INTERNAL_KEY_TOO_SHORT`).
 * </para>
 *
 * <para><b>Never echoed.</b> Messages carry names, paths and sources only.</para>
 */

export const DEFAULT_INTERNAL_KEY_PATH = '/run/secrets/steam_sidecar_internal_key';

/** NODE_ENV values under which the plain-variable path is open. Exact match only. */
export const ENV_SECRET_MODES: ReadonlySet<string> = new Set(['development', 'test']);

export function envSecretsAllowed(nodeEnv: string | undefined): boolean {
  return nodeEnv !== undefined && ENV_SECRET_MODES.has(nodeEnv);
}

export type SharedSecretErrorCode =
  | 'SHARED_SECRET_IN_ENV'
  | 'SHARED_SECRET_AMBIGUOUS'
  | 'SHARED_SECRET_UNREADABLE';

export class SharedSecretError extends Error {
  constructor(
    readonly code: SharedSecretErrorCode,
    message: string,
  ) {
    super(message);
    this.name = 'SharedSecretError';
  }
}

export type SecretSource = 'file' | 'env';

export interface LoadedInternalKey {
  readonly internalKey: string;
  readonly source: SecretSource;
  readonly path: string;
}

type Env = Readonly<Record<string, string | undefined>>;
type MountedFileReader = (path: string) => string | null;

/** The internal key, validated; throws SharedSecretError or InternalKeyError. */
export function loadInternalKey(
  env: Env,
  readFile: MountedFileReader = readSecretFile,
): LoadedInternalKey {
  const path = (env.INTERNAL_KEY_FILE ?? '').trim() || DEFAULT_INTERNAL_KEY_PATH;
  const fromEnv = (env.INTERNAL_KEY ?? '').trim();

  if (fromEnv !== '' && !envSecretsAllowed(env.NODE_ENV)) {
    throw new SharedSecretError(
      'SHARED_SECRET_IN_ENV',
      `INTERNAL_KEY is set as an environment variable. Outside development ` +
        `(NODE_ENV=development|test) the internal key is read only from its Docker ` +
        `secret file (${path}) — remove the variable (DEPLOY_RUNBOOK §L.9).`,
    );
  }

  let fromFile: string | null;
  try {
    fromFile = readFile(path);
  } catch {
    throw new SharedSecretError(
      'SHARED_SECRET_UNREADABLE',
      `The internal key secret ${path} exists but cannot be read as a file. A missing ` +
        `source file makes Docker create a DIRECTORY at the mount point — create the ` +
        `file (secrets/README.md) and remove the directory.`,
    );
  }

  if (fromFile !== null && fromEnv !== '') {
    throw new SharedSecretError(
      'SHARED_SECRET_AMBIGUOUS',
      `The internal key is provided twice (${path} and INTERNAL_KEY) — keep one.`,
    );
  }

  const internalKey = requireInternalKey(fromFile ?? fromEnv);
  return { internalKey, source: fromFile !== null ? 'file' : 'env', path };
}
