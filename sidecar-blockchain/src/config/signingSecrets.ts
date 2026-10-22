import TronWeb from 'tronweb';
import { readSecretFile } from './secretFile.js';

/**
 * T163 — the TRON service's signing material (DEFERRED_BACKLOG
 * `SigningSecretsInPlainEnv`, owner decision 2026-09-17: Docker secret +
 * offline backup; 05 §3.3, §3.5).
 *
 * <para>
 * <b>Files, not variables.</b> The HD seed and the hot wallet key arrive as
 * Docker secrets — files compose mounts under `/run/secrets/` — the same way
 * the Ethereum service receives its own (T150). An environment variable is
 * visible to `docker inspect`, `docker compose config` and every child
 * process; a mounted file is not. `*_FILE` overrides the path (tests, a
 * non-compose host).
 * </para>
 *
 * <para>
 * <b>The variable path is development-only.</b> `HD_WALLET_MNEMONIC` and
 * `HOT_WALLET_PRIVATE_KEY` are still read when `NODE_ENV` is exactly
 * `development` or `test` (a sidecar run with `npm run dev`, the test suite).
 * Anywhere else — `production`, an unset `NODE_ENV`, a typo — a non-empty
 * variable stops the service (`SIGNING_SECRET_IN_ENV`) instead of being used
 * or quietly ignored: somebody put a seed where `docker inspect` shows it, and
 * that has to be removed, not worked around. compose passes neither variable.
 * </para>
 *
 * <para>
 * <b>Never echoed.</b> No error message, log line or return value of this
 * module carries a secret's content — only its name, path and source.
 * </para>
 */

export const DEFAULT_SIGNING_SECRET_PATHS = {
  hdMnemonic: '/run/secrets/tron_hd_mnemonic',
  hotWalletKey: '/run/secrets/tron_hot_wallet_private_key',
} as const;

/** NODE_ENV values under which the plain-variable path is open. Exact match only. */
export const ENV_SECRET_MODES: ReadonlySet<string> = new Set(['development', 'test']);

export type SigningSecretErrorCode =
  | 'SIGNING_SECRET_IN_ENV'
  | 'SIGNING_SECRET_AMBIGUOUS'
  | 'SIGNING_SECRET_UNREADABLE'
  | 'INVALID_SIGNING_KEY'
  | 'HOT_WALLET_ADDRESS_MISMATCH';

export class SigningSecretError extends Error {
  constructor(
    readonly code: SigningSecretErrorCode,
    message: string,
  ) {
    super(message);
    this.name = 'SigningSecretError';
  }
}

/** Where a secret came from — logged at startup, never the value. */
export type SecretSource = 'file' | 'env' | 'none';

export interface SigningMaterial {
  /** BIP-39 phrase of the TRON deposit seed, '' when not provisioned. */
  readonly hdMnemonic: string;
  /** Hot wallet key as 64 lowercase hex (no 0x), '' when not provisioned. */
  readonly hotWalletPrivateKey: string;
  /** TRON address the hot wallet key controls, null when there is no key. */
  readonly hotWalletAddressFromKey: string | null;
  readonly sources: { readonly hdMnemonic: SecretSource; readonly hotWalletKey: SecretSource };
  readonly paths: { readonly hdMnemonic: string; readonly hotWalletKey: string };
}

type Env = Readonly<Record<string, string | undefined>>;
type MountedFileReader = (path: string) => string | null;

interface SecretSpec {
  readonly label: string;
  readonly variable: string;
  readonly fileVariable: string;
  readonly defaultPath: string;
}

const HD_MNEMONIC: SecretSpec = {
  label: 'The TRON seed',
  variable: 'HD_WALLET_MNEMONIC',
  fileVariable: 'HD_WALLET_MNEMONIC_FILE',
  defaultPath: DEFAULT_SIGNING_SECRET_PATHS.hdMnemonic,
};

const HOT_WALLET_KEY: SecretSpec = {
  label: 'The TRON hot wallet key',
  variable: 'HOT_WALLET_PRIVATE_KEY',
  fileVariable: 'HOT_WALLET_PRIVATE_KEY_FILE',
  defaultPath: DEFAULT_SIGNING_SECRET_PATHS.hotWalletKey,
};

/** Exact match, no trimming: ' development' or 'development\n' keeps the env path closed. */
export function envSecretsAllowed(nodeEnv: string | undefined): boolean {
  return nodeEnv !== undefined && ENV_SECRET_MODES.has(nodeEnv);
}

function resolveOne(
  spec: SecretSpec,
  env: Env,
  readFile: MountedFileReader,
): { value: string; source: SecretSource; path: string } {
  const path = (env[spec.fileVariable] ?? '').trim() || spec.defaultPath;
  const fromEnv = (env[spec.variable] ?? '').trim();

  if (fromEnv !== '' && !envSecretsAllowed(env.NODE_ENV)) {
    throw new SigningSecretError(
      'SIGNING_SECRET_IN_ENV',
      `${spec.variable} is set as an environment variable. Outside development ` +
        `(NODE_ENV=development|test) ${spec.label.toLowerCase()} is read only from its ` +
        `Docker secret file (${path}) — remove the variable (DEPLOY_RUNBOOK §L).`,
    );
  }

  let fromFile: string | null;
  try {
    fromFile = readFile(path);
  } catch {
    // readSecretFile names the path and the OS reason; the reason never
    // carries content, but the wording is ours so nothing else can slip in.
    throw new SigningSecretError(
      'SIGNING_SECRET_UNREADABLE',
      `${spec.label} secret ${path} exists but cannot be read as a file. A missing ` +
        `source file makes Docker create a DIRECTORY at the mount point — create the ` +
        `file (secrets/README.md) and remove the directory.`,
    );
  }

  if (fromFile !== null && fromEnv !== '') {
    throw new SigningSecretError(
      'SIGNING_SECRET_AMBIGUOUS',
      `${spec.label} is provided twice (${path} and ${spec.variable}) — keep one.`,
    );
  }
  if (fromFile !== null) return { value: fromFile, source: 'file', path };
  if (fromEnv !== '') return { value: fromEnv, source: 'env', path };
  return { value: '', source: 'none', path };
}

/** 64 hex, optional 0x → lowercase hex without 0x (the form TronWeb signs with). */
function normalizeKey(raw: string): string {
  if (raw === '') return '';
  const hex = raw.startsWith('0x') || raw.startsWith('0X') ? raw.slice(2) : raw;
  if (!/^[0-9a-fA-F]{64}$/.test(hex)) {
    // The value is never echoed — only that it is malformed.
    throw new SigningSecretError(
      'INVALID_SIGNING_KEY',
      `${HOT_WALLET_KEY.label} is not a 32-byte hex private key.`,
    );
  }
  return hex.toLowerCase();
}

/**
 * Reads, validates and returns the two signing secrets. Throws a
 * {@link SigningSecretError} the entry point turns into a non-zero exit.
 *
 * `hotWalletAddress` is HOT_WALLET_ADDRESS (the pinned sweep destination the
 * backend reads too). When both it and the key are present they must name the
 * same account (`HOT_WALLET_ADDRESS_MISMATCH`, the Ethereum service's rule):
 * a key restored from the wrong backup, or an address edited without its key,
 * stops the service at startup instead of failing at the first payout.
 */
export function loadSigningMaterial(
  env: Env,
  hotWalletAddress: string,
  readFile: MountedFileReader = readSecretFile,
): SigningMaterial {
  const mnemonic = resolveOne(HD_MNEMONIC, env, readFile);
  const key = resolveOne(HOT_WALLET_KEY, env, readFile);

  const hot = { key: normalizeKey(key.value) };
  const hotWalletAddressFromKey = hot.key === '' ? null : TronWeb.address.fromPrivateKey(hot.key);
  if (hot.key !== '' && typeof hotWalletAddressFromKey !== 'string') {
    throw new SigningSecretError(
      'INVALID_SIGNING_KEY',
      `${HOT_WALLET_KEY.label} does not map to a TRON address.`,
    );
  }

  const pinned = hotWalletAddress.trim();
  if (hotWalletAddressFromKey && pinned !== '' && pinned !== hotWalletAddressFromKey) {
    throw new SigningSecretError(
      'HOT_WALLET_ADDRESS_MISMATCH',
      `${HOT_WALLET_KEY.label} controls ${hotWalletAddressFromKey}, not HOT_WALLET_ADDRESS ` +
        `(${pinned}) — restore the matching key or correct the address.`,
    );
  }

  return {
    hdMnemonic: mnemonic.value,
    hotWalletPrivateKey: hot.key,
    hotWalletAddressFromKey: hotWalletAddressFromKey || null,
    sources: { hdMnemonic: mnemonic.source, hotWalletKey: key.source },
    paths: { hdMnemonic: mnemonic.path, hotWalletKey: key.path },
  };
}
