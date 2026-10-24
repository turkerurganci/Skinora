import { envSecretsAllowed, type SharedSecretSource } from './sharedSecrets.js';

/**
 * T171b — the Ethereum service's third-party credentials: the JSON-RPC
 * provider URLs, which carry the provider's API key in their path
 * (08 §3.7.1; DEFERRED_BACKLOG `ThirdPartyCredentialsInPlainEnv`, owner
 * decision 2026-10-24; the T171a pattern of config/sharedSecrets.ts,
 * 05 §3.5, DEPLOY_RUNBOOK §L.9).
 *
 * <para>
 * Both URLs arrive as Docker secret files (`ethereum_rpc_primary_url`,
 * `ethereum_rpc_backup_url`; `*_FILE` overrides the path). The rules on the
 * value itself stay in config/index.ts (the primary is required —
 * `RPC_NOT_CONFIGURED`; http(s) only; the URL is never echoed).
 * </para>
 *
 * <para>
 * <b>The variable path is development-only.</b> `ETHEREUM_RPC_PRIMARY_URL` /
 * `ETHEREUM_RPC_BACKUP_URL` are read only when `NODE_ENV` is exactly
 * `development` or `test`; anywhere else a non-empty variable stops the
 * service (`CREDENTIAL_IN_ENV`). File and variable together:
 * `CREDENTIAL_AMBIGUOUS`. A path that exists but is not a readable file
 * (Docker's directory for a missing source): `CREDENTIAL_UNREADABLE`.
 * </para>
 *
 * <para><b>Never echoed.</b> Messages carry names, paths and sources only.</para>
 */

export const DEFAULT_CREDENTIAL_PATHS = {
  rpcPrimaryUrl: '/run/secrets/ethereum_rpc_primary_url',
  rpcBackupUrl: '/run/secrets/ethereum_rpc_backup_url',
} as const;

export interface CredentialSpec {
  readonly label: string;
  readonly variable: string;
  readonly fileVariable: string;
  readonly defaultPath: string;
}

export const RPC_PRIMARY_URL_SPEC: CredentialSpec = {
  label: 'The primary RPC URL',
  variable: 'ETHEREUM_RPC_PRIMARY_URL',
  fileVariable: 'ETHEREUM_RPC_PRIMARY_URL_FILE',
  defaultPath: DEFAULT_CREDENTIAL_PATHS.rpcPrimaryUrl,
};

export const RPC_BACKUP_URL_SPEC: CredentialSpec = {
  label: 'The backup RPC URL',
  variable: 'ETHEREUM_RPC_BACKUP_URL',
  fileVariable: 'ETHEREUM_RPC_BACKUP_URL_FILE',
  defaultPath: DEFAULT_CREDENTIAL_PATHS.rpcBackupUrl,
};

export interface ResolvedCredential {
  readonly value: string;
  readonly source: SharedSecretSource;
  readonly path: string;
}

type Env = Readonly<Record<string, string | undefined>>;
type MountedFileReader = (path: string) => string | null;

/**
 * File first, the variable only in development. `fail(code, message)` builds
 * the caller's error type (ConfigError) so the entry point reports the code.
 */
export function resolveCredential(
  spec: CredentialSpec,
  env: Env,
  readFile: MountedFileReader,
  fail: (code: string, message: string) => Error,
): ResolvedCredential {
  const path = (env[spec.fileVariable] ?? '').trim() || spec.defaultPath;
  const fromEnv = (env[spec.variable] ?? '').trim();

  if (fromEnv !== '' && !envSecretsAllowed(env.NODE_ENV)) {
    throw fail(
      'CREDENTIAL_IN_ENV',
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
      'CREDENTIAL_UNREADABLE',
      `${spec.label} secret ${path} exists but cannot be read as a file. A missing ` +
        `source file makes Docker create a DIRECTORY at the mount point — create the ` +
        `file (secrets/README.md) and remove the directory.`,
    );
  }

  if (fromFile !== null && fromEnv !== '') {
    throw fail(
      'CREDENTIAL_AMBIGUOUS',
      `${spec.label} is provided twice (${path} and ${spec.variable}) — keep one.`,
    );
  }
  if (fromFile !== null) return { value: fromFile, source: 'file', path };
  if (fromEnv !== '') return { value: fromEnv, source: 'env', path };
  return { value: '', source: 'none', path };
}
