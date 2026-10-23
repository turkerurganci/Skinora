/**
 * T161 — the internal key (05 §3.4, X-Internal-Key) is required to start.
 *
 * Before T161 an empty INTERNAL_KEY made the auth middleware skip the check
 * ("dev mode"), so a container started without it answered every /api route to
 * anyone who could reach its port. That is fail-open on the one control that
 * stands between the network and the service. The service now refuses to start
 * instead, with the same codes and the same length floor as the Ethereum
 * service (sidecar-ethereum/src/config/index.ts): the runbook's
 * `openssl rand -hex 32` is 64 characters.
 */
export const MIN_INTERNAL_KEY_LENGTH = 32;

export type InternalKeyErrorCode = 'INTERNAL_KEY_MISSING' | 'INTERNAL_KEY_TOO_SHORT';

export class InternalKeyError extends Error {
  constructor(
    readonly code: InternalKeyErrorCode,
    message: string,
  ) {
    super(message);
    this.name = 'InternalKeyError';
  }
}

/** The trimmed key, or an {@link InternalKeyError} the entry point turns into a non-zero exit. */
export function requireInternalKey(raw: string | undefined): string {
  const key = (raw ?? '').trim();
  if (key === '') {
    throw new InternalKeyError(
      'INTERNAL_KEY_MISSING',
      'The internal key is not set (Docker secret file /run/secrets/steam_sidecar_internal_key; ' +
        'INTERNAL_KEY only in development) — the service does not start without it (05 §3.4).',
    );
  }
  if (key.length < MIN_INTERNAL_KEY_LENGTH) {
    throw new InternalKeyError(
      'INTERNAL_KEY_TOO_SHORT',
      `The internal key must be at least ${MIN_INTERNAL_KEY_LENGTH} characters (openssl rand -hex 32).`,
    );
  }
  return key;
}
