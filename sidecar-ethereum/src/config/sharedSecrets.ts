/**
 * T171a — the Ethereum service's shared secrets (DEFERRED_BACKLOG
 * `SharedSecretsInPlainEnv`; the TRON service's config/sharedSecrets.ts and
 * signingSecrets.ts pattern, 05 §3.5, DEPLOY_RUNBOOK §L.9).
 *
 * <para>
 * Two secrets this service shares with the backend: its internal key
 * (X-Internal-Key, 05 §3.4) and the HMAC secret of its webhooks (05 §3.3a).
 * Both arrive as Docker secret files — the SAME files compose mounts into the
 * backend. `*_FILE` overrides the path.
 * </para>
 *
 * <para>
 * <b>The variable path is development-only.</b> `INTERNAL_KEY` and
 * `WEBHOOK_SECRET` are read only when `NODE_ENV` is exactly `development` or
 * `test`; anywhere else a non-empty variable stops the service
 * (`SHARED_SECRET_IN_ENV`). File and variable together:
 * `SHARED_SECRET_AMBIGUOUS`. A path that exists but is not a readable file:
 * `SHARED_SECRET_UNREADABLE`. The length rules stay in config/index.ts.
 * </para>
 *
 * <para><b>Never echoed.</b> Messages carry names, paths and sources only.</para>
 */

export const DEFAULT_SHARED_SECRET_PATHS = {
  internalKey: '/run/secrets/ethereum_sidecar_internal_key',
  webhookSecret: '/run/secrets/ethereum_webhook_secret',
} as const;

/** NODE_ENV values under which the plain-variable path is open. Exact match only. */
export const ENV_SECRET_MODES: ReadonlySet<string> = new Set(['development', 'test']);

export function envSecretsAllowed(nodeEnv: string | undefined): boolean {
  return nodeEnv !== undefined && ENV_SECRET_MODES.has(nodeEnv);
}

export type SharedSecretSource = 'file' | 'env' | 'none';

export interface ResolvedSharedSecret {
  readonly value: string;
  readonly source: SharedSecretSource;
  readonly path: string;
}

export interface SharedSecretSpec {
  readonly label: string;
  readonly variable: string;
  readonly fileVariable: string;
  readonly defaultPath: string;
}

export const INTERNAL_KEY_SPEC: SharedSecretSpec = {
  label: 'The internal key',
  variable: 'INTERNAL_KEY',
  fileVariable: 'INTERNAL_KEY_FILE',
  defaultPath: DEFAULT_SHARED_SECRET_PATHS.internalKey,
};

export const WEBHOOK_SECRET_SPEC: SharedSecretSpec = {
  label: 'The webhook secret',
  variable: 'WEBHOOK_SECRET',
  fileVariable: 'WEBHOOK_SECRET_FILE',
  defaultPath: DEFAULT_SHARED_SECRET_PATHS.webhookSecret,
};

type Env = Readonly<Record<string, string | undefined>>;
type MountedFileReader = (path: string) => string | null;

/**
 * File first, the variable only in development. `fail(code, message)` builds
 * the caller's error type (ConfigError) so the entry point reports the code.
 */
export function resolveSharedSecret(
  spec: SharedSecretSpec,
  env: Env,
  readFile: MountedFileReader,
  fail: (code: string, message: string) => Error,
): ResolvedSharedSecret {
  const path = (env[spec.fileVariable] ?? '').trim() || spec.defaultPath;
  const fromEnv = (env[spec.variable] ?? '').trim();

  if (fromEnv !== '' && !envSecretsAllowed(env.NODE_ENV)) {
    throw fail(
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
    throw fail(
      'SHARED_SECRET_UNREADABLE',
      `${spec.label} secret ${path} exists but cannot be read as a file. A missing ` +
        `source file makes Docker create a DIRECTORY at the mount point — create the ` +
        `file (secrets/README.md) and remove the directory.`,
    );
  }

  if (fromFile !== null && fromEnv !== '') {
    throw fail(
      'SHARED_SECRET_AMBIGUOUS',
      `${spec.label} is provided twice (${path} and ${spec.variable}) — keep one.`,
    );
  }
  if (fromFile !== null) return { value: fromFile, source: 'file', path };
  if (fromEnv !== '') return { value: fromEnv, source: 'env', path };
  return { value: '', source: 'none', path };
}
