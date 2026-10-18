import { readFileSync } from 'node:fs';

/**
 * Reads one Docker secret (05 §3.5, 08 §3.7.2): the file compose mounts under
 * `/run/secrets/<name>`. Signing material reaches this service only this way —
 * never as a plain environment variable, which `docker inspect` would show.
 *
 * Returns the trimmed content, or `null` when the file does not exist or is
 * empty (the secret is not provisioned). Any other read failure — a directory,
 * a permission error — throws: a secret that exists but cannot be read is a
 * broken deployment, not an absent one.
 */
export function readSecretFile(path: string): string | null {
  let raw: string;
  try {
    raw = readFileSync(path, 'utf8');
  } catch (err) {
    if ((err as NodeJS.ErrnoException).code === 'ENOENT') return null;
    throw new Error(`Secret file ${path} exists but cannot be read: ${(err as Error).message}`);
  }
  const value = raw.trim();
  return value.length > 0 ? value : null;
}
