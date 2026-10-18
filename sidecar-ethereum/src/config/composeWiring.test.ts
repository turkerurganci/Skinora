import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { DEFAULT_SECRET_PATHS } from './index.js';

/**
 * docker-compose.yml, read the way production starts the stack (T150 —
 * 05 §3.3a, §3.5; 08 §3.7.2; K7). A unit test builds the service with the
 * values already in hand; this file checks that the values reach THIS
 * container, as secrets where they are secrets, and reach no other one.
 */
const COMPOSE = readFileSync(resolve(__dirname, '../../../docker-compose.yml'), 'utf8').replace(
  /\r\n/g,
  '\n',
);

function block(name: string, indent = '  '): string {
  const lines = COMPOSE.split('\n');
  const start = lines.indexOf(`${indent}${name}:`);
  if (start < 0) throw new Error(`docker-compose.yml has no ${name}`);
  // A service ends at the next service or top-level key; a top-level key
  // ends at the next top-level key only.
  const boundary = indent === '' ? /^[a-z0-9][a-z0-9_-]*:\s*$/ : /^( {2})?[a-z0-9][a-z0-9_-]*:\s*$/;
  const next = lines.findIndex((line, i) => i > start && boundary.test(line));
  return lines.slice(start, next < 0 ? undefined : next).join('\n');
}

const ethereum = block('skinora-ethereum-sidecar');
const SECRET_NAMES = [
  'ethereum_hd_mnemonic',
  'ethereum_hot_wallet_private_key',
  'ethereum_fuel_wallet_private_key',
];

describe('docker-compose — the Ethereum service (K7: own container, own seed)', () => {
  it('is built from its own directory and runs only under the "ethereum" profile', () => {
    expect(ethereum).toMatch(/^ {6}context: \.\/sidecar-ethereum$/m);
    expect(ethereum).toMatch(/^ {4}profiles: \["ethereum"\]$/m);
  });

  it.each([
    ['INTERNAL_KEY', 'ETHEREUM_SIDECAR_INTERNAL_KEY'],
    ['WEBHOOK_SECRET', 'ETHEREUM_WEBHOOK_SECRET'],
    ['ETHEREUM_RPC_PRIMARY_URL', 'ETHEREUM_RPC_PRIMARY_URL'],
    ['ETHEREUM_RPC_BACKUP_URL', 'ETHEREUM_RPC_BACKUP_URL'],
    ['ETHEREUM_RPC_PRIMARY_MAX_BLOCK_RANGE', 'ETHEREUM_RPC_PRIMARY_MAX_BLOCK_RANGE'],
    ['ETHEREUM_RPC_BACKUP_MAX_BLOCK_RANGE', 'ETHEREUM_RPC_BACKUP_MAX_BLOCK_RANGE'],
    ['ETHEREUM_FINALIZED_STALL_SECONDS', 'ETHEREUM_FINALIZED_STALL_SECONDS'],
    ['ETHEREUM_USDT_CONTRACT', 'ETHEREUM_USDT_CONTRACT'],
    ['ETHEREUM_HOT_WALLET_ADDRESS', 'ETHEREUM_HOT_WALLET_ADDRESS'],
    ['ETHEREUM_COLD_WALLET_ADDRESS', 'ETHEREUM_COLD_WALLET_ADDRESS'],
  ])('passes %s from .env (%s)', (inside, outside) => {
    expect(ethereum).toMatch(new RegExp(`^ +- ${inside}=\\$\\{${outside}(:-[^}]*)?\\}$`, 'm'));
  });

  it('mounts its seed and keys as Docker secrets at the paths the service reads', () => {
    for (const name of SECRET_NAMES) {
      expect(ethereum).toMatch(new RegExp(`^ {6}- ${name}$`, 'm'));
    }
    expect(Object.values(DEFAULT_SECRET_PATHS)).toEqual(
      SECRET_NAMES.map((name) => `/run/secrets/${name}`),
    );
  });

  it('declares each secret as a file under ./secrets (gitignored)', () => {
    const mounted = block('secrets', '');
    for (const name of SECRET_NAMES) {
      expect(mounted).toMatch(
        new RegExp(`^ {2}${name}:\\n {4}file: \\./secrets/${name}\\.txt$`, 'm'),
      );
    }
  });

  it('carries no signing material as a plain environment variable', () => {
    expect(ethereum).not.toMatch(/MNEMONIC=|PRIVATE_KEY=/);
  });

  it('receives nothing of the TRON service (seed, keys, TronGrid)', () => {
    for (const tron of [
      'HD_WALLET_MNEMONIC',
      'HOT_WALLET_PRIVATE_KEY',
      'TRON_API_KEY',
      '${INTERNAL_KEY}',
    ]) {
      expect(ethereum).not.toContain(tron);
    }
  });
});

describe('docker-compose — the Ethereum secrets reach no other container', () => {
  it.each([
    'skinora-backend',
    'skinora-blockchain-sidecar',
    'skinora-steam-sidecar',
    'skinora-frontend',
  ])('%s has none of them', (service) => {
    const other = block(service);
    for (const name of SECRET_NAMES) expect(other).not.toContain(name);
    expect(other).not.toMatch(/^ {4}secrets:/m);
  });

  it('the backend still points at no Ethereum service by default (fail-closed until T151–T154)', () => {
    expect(block('skinora-backend')).toMatch(
      /^ +- EthereumSidecar__BaseUrl=\$\{ETHEREUM_SIDECAR_BASE_URL:-\}$/m,
    );
  });
});
