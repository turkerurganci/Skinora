import { readSecretFile } from './secretFile.js';
import { envSecretsAllowed } from './sharedSecrets.js';

/**
 * T171b — the Steam service's third-party credentials (DEFERRED_BACKLOG
 * `ThirdPartyCredentialsInPlainEnv`, owner decision 2026-10-24; the T171a
 * pattern of config/sharedSecrets.ts, 05 §3.5, DEPLOY_RUNBOOK §L.9).
 *
 * <para>
 * <b>The Steam Web API key</b> is the Docker secret file `steam_api_key` —
 * the SAME file compose mounts into the backend (OpenID profile). Optional, as
 * before: without it the trade-hold probe answers 503 and the service runs.
 * `STEAM_API_KEY_FILE` overrides the path.
 * </para>
 *
 * <para>
 * <b>The Redis password</b> is the Docker secret file `redis_password` — the
 * file the Redis container itself reads (scripts/redis/redis-entrypoint.sh).
 * `REDIS_URL` carries the address WITHOUT a password
 * (`redis://skinora-redis:6379`); the password from the file is put into it
 * here, so the full URL exists only inside this process. An empty `REDIS_URL`
 * keeps the in-memory cache (no Redis, no password needed).
 * `REDIS_PASSWORD_FILE` overrides the path.
 * </para>
 *
 * <para>
 * <b>The variable path is development-only.</b> `STEAM_API_KEY`, and a
 * password inside `REDIS_URL`, are read only when `NODE_ENV` is exactly
 * `development` or `test`; anywhere else they stop the service
 * (`CREDENTIAL_IN_ENV`). File and variable together: `CREDENTIAL_AMBIGUOUS`.
 * A path that exists but is not a readable file (Docker's directory for a
 * missing source): `CREDENTIAL_UNREADABLE`. A Redis password outside the
 * backend's character set, or a `REDIS_URL` that is not a redis URL:
 * `CREDENTIAL_INVALID`.
 * </para>
 *
 * <para><b>Never echoed.</b> Messages carry names, paths and sources only —
 * never a key, a password or the URL.</para>
 */

export const DEFAULT_CREDENTIAL_PATHS = {
  steamApiKey: '/run/secrets/steam_api_key',
  redisPassword: '/run/secrets/redis_password',
} as const;

/** Same set as the backend's ThirdPartyCredentialFiles.RedisPasswordCharacters. */
export const REDIS_PASSWORD_CHARACTERS = /^[A-Za-z0-9._~+=-]+$/;

export type CredentialErrorCode =
  | 'CREDENTIAL_IN_ENV'
  | 'CREDENTIAL_AMBIGUOUS'
  | 'CREDENTIAL_UNREADABLE'
  | 'CREDENTIAL_INVALID';

export class CredentialError extends Error {
  constructor(
    readonly code: CredentialErrorCode,
    message: string,
  ) {
    super(message);
    this.name = 'CredentialError';
  }
}

export type CredentialSource = 'file' | 'env' | 'none';

export interface ProviderCredentials {
  readonly steamApiKey: string;
  /** '' = no Redis (in-memory cache). Carries the password; never log it. */
  readonly redisUrl: string;
  readonly sources: {
    readonly steamApiKey: CredentialSource;
    readonly redisPassword: CredentialSource;
  };
}

type Env = Readonly<Record<string, string | undefined>>;
type MountedFileReader = (path: string) => string | null;

function readMounted(readFile: MountedFileReader, path: string, label: string): string | null {
  try {
    return readFile(path);
  } catch {
    throw new CredentialError(
      'CREDENTIAL_UNREADABLE',
      `${label} secret ${path} exists but cannot be read as a file. A missing source file ` +
        `makes Docker create a DIRECTORY at the mount point — create the file ` +
        `(secrets/README.md) and remove the directory.`,
    );
  }
}

function loadSteamApiKey(
  env: Env,
  readFile: MountedFileReader,
): { value: string; source: CredentialSource } {
  const path = (env.STEAM_API_KEY_FILE ?? '').trim() || DEFAULT_CREDENTIAL_PATHS.steamApiKey;
  const fromEnv = (env.STEAM_API_KEY ?? '').trim();
  if (fromEnv !== '' && !envSecretsAllowed(env.NODE_ENV)) {
    throw new CredentialError(
      'CREDENTIAL_IN_ENV',
      `STEAM_API_KEY is set as an environment variable. Outside development ` +
        `(NODE_ENV=development|test) the Steam Web API key is read only from its Docker ` +
        `secret file (${path}) — remove the variable (DEPLOY_RUNBOOK §L.9).`,
    );
  }
  const fromFile = readMounted(readFile, path, 'The Steam Web API key');
  if (fromFile !== null && fromEnv !== '') {
    throw new CredentialError(
      'CREDENTIAL_AMBIGUOUS',
      `The Steam Web API key is provided twice (${path} and STEAM_API_KEY) — keep one.`,
    );
  }
  if (fromFile !== null) return { value: fromFile, source: 'file' };
  if (fromEnv !== '') return { value: fromEnv, source: 'env' };
  return { value: '', source: 'none' };
}

function loadRedisUrl(
  env: Env,
  readFile: MountedFileReader,
): { value: string; source: CredentialSource } {
  const raw = (env.REDIS_URL ?? '').trim();
  const path = (env.REDIS_PASSWORD_FILE ?? '').trim() || DEFAULT_CREDENTIAL_PATHS.redisPassword;
  // Read (and so check) the mount even without a URL: a directory there is a
  // broken deployment either way.
  const fromFile = readMounted(readFile, path, 'The Redis password');
  if (raw === '') return { value: '', source: 'none' };

  let url: URL;
  try {
    url = new URL(raw);
  } catch {
    // The URL itself is not echoed: it may carry a password.
    throw new CredentialError('CREDENTIAL_INVALID', 'REDIS_URL is not a URL (not shown).');
  }
  if (url.protocol !== 'redis:' && url.protocol !== 'rediss:') {
    throw new CredentialError(
      'CREDENTIAL_INVALID',
      'REDIS_URL must be a redis:// or rediss:// URL.',
    );
  }
  const inUrl = url.password !== '';
  if (inUrl && !envSecretsAllowed(env.NODE_ENV)) {
    throw new CredentialError(
      'CREDENTIAL_IN_ENV',
      `REDIS_URL carries a password. Outside development (NODE_ENV=development|test) the ` +
        `Redis password is read only from its Docker secret file (${path}) — remove it ` +
        `from the URL (DEPLOY_RUNBOOK §L.9).`,
    );
  }
  if (inUrl && fromFile !== null) {
    throw new CredentialError(
      'CREDENTIAL_AMBIGUOUS',
      `The Redis password is provided twice (${path} and inside REDIS_URL) — keep one.`,
    );
  }
  if (inUrl) return { value: raw, source: 'env' };
  if (fromFile === null) return { value: raw, source: 'none' };
  if (!REDIS_PASSWORD_CHARACTERS.test(fromFile)) {
    throw new CredentialError(
      'CREDENTIAL_INVALID',
      `The Redis password (${path}) may use only A-Z a-z 0-9 and . _ ~ + = - (openssl rand -hex 32).`,
    );
  }
  url.password = fromFile;
  return { value: url.toString(), source: 'file' };
}

/** Reads both credentials. Throws a {@link CredentialError} the entry point turns into a non-zero exit. */
export function loadProviderCredentials(
  env: Env,
  readFile: MountedFileReader = readSecretFile,
): ProviderCredentials {
  const steam = loadSteamApiKey(env, readFile);
  const redis = loadRedisUrl(env, readFile);
  return {
    steamApiKey: steam.value,
    redisUrl: redis.value,
    sources: { steamApiKey: steam.source, redisPassword: redis.source },
  };
}
