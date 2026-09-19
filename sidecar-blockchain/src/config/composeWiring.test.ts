import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, it, expect } from 'vitest';

/**
 * The production wiring, read from the file production is started with.
 *
 * <para>
 * #325 fix round: STAKE_ACCOUNT_ADDRESS and STAKE_ACCOUNT_PERMISSION_ID were
 * added to the BACKEND service in docker-compose.yml, which never reads them,
 * and not to this sidecar. Deployed that way the signer never learns the stake
 * account: it stays in the pre-split arrangement, finds nothing to delegate in
 * the hot wallet, and burns every sweep and refund while the stake sits idle.
 * Nothing fails — every unit test builds the service with the value already in
 * hand, which is exactly the part this file does not take on trust.
 * </para>
 *
 * The other signer variables fail loudly when missing (no sweeper, fail-closed
 * ceilings, no consolidation); these two fail silently, so they are pinned here.
 */
const COMPOSE_FILE = resolve(__dirname, '../../../docker-compose.yml');

function serviceBlock(compose: string, service: string): string {
  const lines = compose.split(/\r?\n/);
  const start = lines.indexOf(`  ${service}:`);
  if (start < 0) throw new Error(`docker-compose.yml has no service ${service}`);
  // The next service, or a top-level key (volumes:, networks:) if it is last.
  const next = lines.findIndex(
    (line, i) => i > start && /^( {2})?[a-z0-9][a-z0-9_-]*:\s*$/.test(line),
  );
  return lines.slice(start, next < 0 ? undefined : next).join('\n');
}

describe('docker-compose — the blockchain sidecar receives the stake account', () => {
  const sidecar = serviceBlock(readFileSync(COMPOSE_FILE, 'utf8'), 'skinora-blockchain-sidecar');

  it.each(['STAKE_ACCOUNT_ADDRESS', 'STAKE_ACCOUNT_PERMISSION_ID'])(
    'passes %s from .env into skinora-blockchain-sidecar',
    (name) => {
      expect(sidecar).toMatch(new RegExp(`^ +- ${name}=\\$\\{${name}(:-[^}]*)?\\}$`, 'm'));
    },
  );
});
