import { resolve, dirname } from "path";
import { fileURLToPath } from "url";

/**
 * Absolute path of the repository root, resolved from this module's own
 * location (scripts/lib/ -> ../..).
 *
 * `fileURLToPath` rather than gpt-review.mjs's hand-rolled
 * `url.pathname.replace(/^\/([A-Z]:)/i, "$1")`: a raw pathname is
 * percent-encoded, so a checkout under "C:\my projects\" or any non-ASCII path
 * yields "my%20projects" and every write lands in a directory that does not
 * exist — silently, since mkdirSync happily creates it.
 */
export function repoRoot() {
  return resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
}

/** Resolve a repo-relative path to an absolute one. */
export function fromRepoRoot(...segments) {
  return resolve(repoRoot(), ...segments);
}
