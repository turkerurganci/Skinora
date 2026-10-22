import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, it, expect } from 'vitest';
import { DEFAULT_SIGNING_SECRET_PATHS } from './signingSecrets.js';

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

/**
 * T139-ActiveMonitorQuotaAlarm — two more silent ones: the sidecar has defaults
 * for both, so a value set in .env that never reaches the container is simply
 * ignored (a paid plan's budget would keep the alert firing on the free tier's
 * figure; a tuned holding cadence would never apply).
 */
describe('docker-compose — the blockchain sidecar receives the polling cadence and the quota budget', () => {
  const sidecar = serviceBlock(readFileSync(COMPOSE_FILE, 'utf8'), 'skinora-blockchain-sidecar');

  it.each(['PAYMENT_HOLDING_POLLING_INTERVAL_MS', 'TRONGRID_DAILY_REQUEST_BUDGET'])(
    'passes %s from .env into skinora-blockchain-sidecar',
    (name) => {
      expect(sidecar).toMatch(new RegExp(`^ +- ${name}=\\$\\{${name}(:-[^}]*)?\\}$`, 'm'));
    },
  );
});

describe('docker-compose — the blockchain sidecar receives the stake account', () => {
  const sidecar = serviceBlock(readFileSync(COMPOSE_FILE, 'utf8'), 'skinora-blockchain-sidecar');

  // T149 — the dormant stake switch travels the same way; it must default to
  // false in compose too, so an .env without it keeps the stake off.
  it.each(['STAKE_ACCOUNT_ADDRESS', 'STAKE_ACCOUNT_PERMISSION_ID', 'ENERGY_STAKING_ENABLED'])(
    'passes %s from .env into skinora-blockchain-sidecar',
    (name) => {
      expect(sidecar).toMatch(new RegExp(`^ +- ${name}=\\$\\{${name}(:-[^}]*)?\\}$`, 'm'));
    },
  );
});

describe('docker-compose — the dormant stake switch defaults to off', () => {
  const sidecar = serviceBlock(readFileSync(COMPOSE_FILE, 'utf8'), 'skinora-blockchain-sidecar');

  it('passes ENERGY_STAKING_ENABLED with a false default', () => {
    expect(sidecar).toMatch(/^ +- ENERGY_STAKING_ENABLED=\$\{ENERGY_STAKING_ENABLED:-false\}$/m);
  });
});

/**
 * T163 — the TRON seed and hot wallet key are Docker secrets, mounted into this
 * service alone at the paths it reads (DEFERRED_BACKLOG `SigningSecretsInPlainEnv`).
 * A plain variable here would put them back where `docker inspect` shows them;
 * the running service would refuse to start on it (signingSecretsStartup.test.ts).
 */
describe('docker-compose — the TRON signing secrets are Docker secrets, for this service only', () => {
  const compose = readFileSync(COMPOSE_FILE, 'utf8').replace(/\r\n/g, '\n');
  const sidecar = serviceBlock(compose, 'skinora-blockchain-sidecar');
  const TRON_SIGNING_FILES = ['tron_hd_mnemonic', 'tron_hot_wallet_private_key'];

  function topLevel(name: string): string {
    const lines = compose.split('\n');
    const start = lines.indexOf(`${name}:`);
    if (start < 0) throw new Error(`docker-compose.yml has no top-level ${name}`);
    const next = lines.findIndex((line, i) => i > start && /^[a-z0-9][a-z0-9_-]*:\s*$/.test(line));
    return lines.slice(start, next < 0 ? undefined : next).join('\n');
  }

  it('carries no signing material as a plain environment variable', () => {
    expect(sidecar).not.toMatch(/HD_WALLET_MNEMONIC[=]|HOT_WALLET_PRIVATE_KEY[=]/);
    expect(sidecar).not.toMatch(/MNEMONIC=\$|PRIVATE_KEY=\$/);
  });

  it('mounts both secrets at the paths the service reads', () => {
    for (const name of TRON_SIGNING_FILES)
      expect(sidecar).toMatch(new RegExp(`^ {6}- ${name}$`, 'm'));
    expect(Object.values(DEFAULT_SIGNING_SECRET_PATHS)).toEqual(
      TRON_SIGNING_FILES.map((name) => `/run/secrets/${name}`),
    );
  });

  it('declares each as a file under ./secrets (gitignored)', () => {
    const declared = topLevel('secrets');
    for (const name of TRON_SIGNING_FILES) {
      expect(declared).toMatch(
        new RegExp(`^ {2}${name}:\\n {4}file: \\./secrets/${name}\\.txt$`, 'm'),
      );
    }
  });

  it.each([
    'skinora-backend',
    'skinora-steam-sidecar',
    'skinora-frontend',
    'skinora-ethereum-sidecar',
  ])('%s receives neither secret', (service) => {
    const other = serviceBlock(compose, service);
    for (const name of TRON_SIGNING_FILES) expect(other).not.toContain(name);
    expect(other).not.toMatch(/HD_WALLET_MNEMONIC[=]|HOT_WALLET_PRIVATE_KEY[=]/);
  });
});
