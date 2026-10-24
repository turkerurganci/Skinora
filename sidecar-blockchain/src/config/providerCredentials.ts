import { readSecretFile } from './secretFile.js';
import { envSecretsAllowed, type SecretSource } from './signingSecrets.js';

/**
 * T171b — the TRON service's third-party credentials: the TronGrid API keys
 * (DEFERRED_BACKLOG `ThirdPartyCredentialsInPlainEnv`, owner decision
 * 2026-10-24; the T171a pattern of config/sharedSecrets.ts, 05 §3.5,
 * DEPLOY_RUNBOOK §L.9).
 *
 * <para>
 * Both keys arrive as Docker secret files (`tron_api_key`,
 * `tron_api_key_secondary`; `*_FILE` overrides the path). Both stay OPTIONAL,
 * as before: without the primary key TronGrid rate-limits the monitor (429),
 * without the secondary there is no key failover (WP10) — the service runs.
 * </para>
 *
 * <para>
 * <b>The variable path is development-only.</b> `TRON_API_KEY` /
 * `TRON_API_KEY_SECONDARY` are read only when `NODE_ENV` is exactly
 * `development` or `test`; anywhere else a non-empty variable stops the
 * service (`CREDENTIAL_IN_ENV`). File and variable together:
 * `CREDENTIAL_AMBIGUOUS`. A path that exists but is not a readable file
 * (Docker's directory for a missing source): `CREDENTIAL_UNREADABLE`.
 * </para>
 *
 * <para><b>Never echoed.</b> Messages carry names, paths and sources only.</para>
 */

export const DEFAULT_CREDENTIAL_PATHS = {
  tronApiKey: '/run/secrets/tron_api_key',
  tronApiKeySecondary: '/run/secrets/tron_api_key_secondary',
} as const;

export type CredentialErrorCode =
  | 'CREDENTIAL_IN_ENV'
  | 'CREDENTIAL_AMBIGUOUS'
  | 'CREDENTIAL_UNREADABLE';

export class CredentialError extends Error {
  constructor(
    readonly code: CredentialErrorCode,
    message: string,
  ) {
    super(message);
    this.name = 'CredentialError';
  }
}

export interface ProviderCredentials {
  readonly tronApiKey: string;
  readonly tronApiKeySecondary: string;
  readonly sources: {
    readonly tronApiKey: SecretSource;
    readonly tronApiKeySecondary: SecretSource;
  };
}

type Env = Readonly<Record<string, string | undefined>>;
type MountedFileReader = (path: string) => string | null;

interface CredentialSpec {
  readonly label: string;
  readonly variable: string;
  readonly fileVariable: string;
  readonly defaultPath: string;
}

const TRON_API_KEY: CredentialSpec = {
  label: 'The TronGrid API key',
  variable: 'TRON_API_KEY',
  fileVariable: 'TRON_API_KEY_FILE',
  defaultPath: DEFAULT_CREDENTIAL_PATHS.tronApiKey,
};

const TRON_API_KEY_SECONDARY: CredentialSpec = {
  label: 'The secondary TronGrid API key',
  variable: 'TRON_API_KEY_SECONDARY',
  fileVariable: 'TRON_API_KEY_SECONDARY_FILE',
  defaultPath: DEFAULT_CREDENTIAL_PATHS.tronApiKeySecondary,
};

function resolveCredential(
  spec: CredentialSpec,
  env: Env,
  readFile: MountedFileReader,
): { value: string; source: SecretSource } {
  const path = (env[spec.fileVariable] ?? '').trim() || spec.defaultPath;
  const fromEnv = (env[spec.variable] ?? '').trim();

  if (fromEnv !== '' && !envSecretsAllowed(env.NODE_ENV)) {
    throw new CredentialError(
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
    throw new CredentialError(
      'CREDENTIAL_UNREADABLE',
      `${spec.label} secret ${path} exists but cannot be read as a file. A missing ` +
        `source file makes Docker create a DIRECTORY at the mount point — create the ` +
        `file (secrets/README.md) and remove the directory.`,
    );
  }

  if (fromFile !== null && fromEnv !== '') {
    throw new CredentialError(
      'CREDENTIAL_AMBIGUOUS',
      `${spec.label} is provided twice (${path} and ${spec.variable}) — keep one.`,
    );
  }
  if (fromFile !== null) return { value: fromFile, source: 'file' };
  if (fromEnv !== '') return { value: fromEnv, source: 'env' };
  return { value: '', source: 'none' };
}

/** Reads both TronGrid keys. Throws a {@link CredentialError} the entry point turns into a non-zero exit. */
export function loadProviderCredentials(
  env: Env,
  readFile: MountedFileReader = readSecretFile,
): ProviderCredentials {
  const primary = resolveCredential(TRON_API_KEY, env, readFile);
  const secondary = resolveCredential(TRON_API_KEY_SECONDARY, env, readFile);
  return {
    tronApiKey: primary.value,
    tronApiKeySecondary: secondary.value,
    sources: { tronApiKey: primary.source, tronApiKeySecondary: secondary.source },
  };
}
