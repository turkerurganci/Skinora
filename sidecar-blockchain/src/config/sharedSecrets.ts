import { readSecretFile } from './secretFile.js';
import { envSecretsAllowed, type SecretSource } from './signingSecrets.js';
import { MIN_INTERNAL_KEY_LENGTH, requireInternalKey } from './internalKey.js';

/**
 * T171a — the TRON service's shared secrets (DEFERRED_BACKLOG
 * `SharedSecretsInPlainEnv`; the T163 pattern of config/signingSecrets.ts,
 * 05 §3.5, DEPLOY_RUNBOOK §L.9).
 *
 * <para>
 * Two secrets this service shares with the backend: its internal key
 * (X-Internal-Key, 05 §3.4) and the HMAC secret of its webhooks (05 §3.3a).
 * Both arrive as Docker secret files — the SAME files compose mounts into the
 * backend, so the two ends cannot drift. `*_FILE` overrides the path (tests,
 * a non-compose host).
 * </para>
 *
 * <para>
 * <b>The variable path is development-only.</b> `INTERNAL_KEY` and
 * `WEBHOOK_SECRET` are read only when `NODE_ENV` is exactly `development` or
 * `test`; anywhere else a non-empty variable stops the service
 * (`SHARED_SECRET_IN_ENV`). File and variable together:
 * `SHARED_SECRET_AMBIGUOUS`. A path that exists but is not a readable file
 * (Docker's directory for a missing source): `SHARED_SECRET_UNREADABLE`.
 * Both secrets are required and at least 32 characters (internal key:
 * `INTERNAL_KEY_MISSING` / `INTERNAL_KEY_TOO_SHORT`, T161; webhook secret:
 * `WEBHOOK_SECRET_MISSING` / `WEBHOOK_SECRET_TOO_SHORT`) — without the webhook
 * secret no payment can be reported, so the service does not pretend to run.
 * </para>
 *
 * <para><b>Never echoed.</b> Messages carry names, paths and sources only.</para>
 */

export const DEFAULT_SHARED_SECRET_PATHS = {
  internalKey: '/run/secrets/blockchain_sidecar_internal_key',
  webhookSecret: '/run/secrets/blockchain_webhook_secret',
} as const;

export const MIN_WEBHOOK_SECRET_LENGTH = MIN_INTERNAL_KEY_LENGTH;

export type SharedSecretErrorCode =
  | 'SHARED_SECRET_IN_ENV'
  | 'SHARED_SECRET_AMBIGUOUS'
  | 'SHARED_SECRET_UNREADABLE'
  | 'WEBHOOK_SECRET_MISSING'
  | 'WEBHOOK_SECRET_TOO_SHORT';

export class SharedSecretError extends Error {
  constructor(
    readonly code: SharedSecretErrorCode,
    message: string,
  ) {
    super(message);
    this.name = 'SharedSecretError';
  }
}

export interface SharedSecrets {
  readonly internalKey: string;
  readonly webhookSecret: string;
  readonly sources: { readonly internalKey: SecretSource; readonly webhookSecret: SecretSource };
  readonly paths: { readonly internalKey: string; readonly webhookSecret: string };
}

type Env = Readonly<Record<string, string | undefined>>;
type MountedFileReader = (path: string) => string | null;

interface SharedSecretSpec {
  readonly label: string;
  readonly variable: string;
  readonly fileVariable: string;
  readonly defaultPath: string;
}

const INTERNAL_KEY: SharedSecretSpec = {
  label: 'The internal key',
  variable: 'INTERNAL_KEY',
  fileVariable: 'INTERNAL_KEY_FILE',
  defaultPath: DEFAULT_SHARED_SECRET_PATHS.internalKey,
};

const WEBHOOK_SECRET: SharedSecretSpec = {
  label: 'The webhook secret',
  variable: 'WEBHOOK_SECRET',
  fileVariable: 'WEBHOOK_SECRET_FILE',
  defaultPath: DEFAULT_SHARED_SECRET_PATHS.webhookSecret,
};

function resolveShared(
  spec: SharedSecretSpec,
  env: Env,
  readFile: MountedFileReader,
): { value: string; source: SecretSource; path: string } {
  const path = (env[spec.fileVariable] ?? '').trim() || spec.defaultPath;
  const fromEnv = (env[spec.variable] ?? '').trim();

  if (fromEnv !== '' && !envSecretsAllowed(env.NODE_ENV)) {
    throw new SharedSecretError(
      'SHARED_SECRET_IN_ENV',
      `${spec.variable} is set as an environment variable. Outside development ` +
        `(NODE_ENV=development|test) ${spec.label.toLowerCase()} is read only from its ` +
        `Docker secret file (${path}) — remove the variable (DEPLOY_RUNBOOK §L.9).`,
    );
  }

  let fromFile: string | null;
  try {
    fromFile = readFile(path);
  } catch {
    throw new SharedSecretError(
      'SHARED_SECRET_UNREADABLE',
      `${spec.label} secret ${path} exists but cannot be read as a file. A missing ` +
        `source file makes Docker create a DIRECTORY at the mount point — create the ` +
        `file (secrets/README.md) and remove the directory.`,
    );
  }

  if (fromFile !== null && fromEnv !== '') {
    throw new SharedSecretError(
      'SHARED_SECRET_AMBIGUOUS',
      `${spec.label} is provided twice (${path} and ${spec.variable}) — keep one.`,
    );
  }
  if (fromFile !== null) return { value: fromFile, source: 'file', path };
  if (fromEnv !== '') return { value: fromEnv, source: 'env', path };
  return { value: '', source: 'none', path };
}

/**
 * Reads and validates both shared secrets. Throws a {@link SharedSecretError}
 * or an `InternalKeyError` the entry point turns into a non-zero exit.
 */
export function loadSharedSecrets(
  env: Env,
  readFile: MountedFileReader = readSecretFile,
): SharedSecrets {
  const internal = resolveShared(INTERNAL_KEY, env, readFile);
  const webhook = resolveShared(WEBHOOK_SECRET, env, readFile);

  const internalKey = requireInternalKey(internal.value);
  if (webhook.value === '') {
    throw new SharedSecretError(
      'WEBHOOK_SECRET_MISSING',
      `The webhook secret is not set (${webhook.path}) — the service does not start ` +
        `without it: no payment could be reported to the backend (05 §3.3a).`,
    );
  }
  if (webhook.value.length < MIN_WEBHOOK_SECRET_LENGTH) {
    throw new SharedSecretError(
      'WEBHOOK_SECRET_TOO_SHORT',
      `The webhook secret must be at least ${MIN_WEBHOOK_SECRET_LENGTH} characters (openssl rand -hex 32).`,
    );
  }

  return {
    internalKey,
    webhookSecret: webhook.value,
    sources: { internalKey: internal.source, webhookSecret: webhook.source },
    paths: { internalKey: internal.path, webhookSecret: webhook.path },
  };
}
