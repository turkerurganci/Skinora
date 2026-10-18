import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  BlockchainWebhookEvents,
  buildWebhookEnvelope,
  type BlockchainWebhookEvent,
} from './WebhookPayloads.js';

/**
 * T148 (05 §3.3a "Webhook yükü") — every webhook envelope names its network.
 *
 * The examples in `contracts/webhook/` are the wire bodies both sides bind to:
 * this test builds each one through the same function both registries use to
 * emit callbacks (`buildWebhookEnvelope`) and expects the example byte for
 * byte (as JSON), and the backend's `BlockchainWebhookContractTests`
 * deserializes the same files and runs them through the controller's network
 * checks. Neither side can drop or rename `network` — or any other envelope
 * field — without the other side's test failing.
 */
const CONTRACT_DIR = resolve(__dirname, '../../contracts/webhook');

interface Example {
  event: BlockchainWebhookEvent;
  network: string;
  timestamp: string;
  data: Record<string, unknown>;
}

function examples(): Array<[string, Example]> {
  return readdirSync(CONTRACT_DIR)
    .filter((f) => f.endsWith('.json'))
    .sort()
    .map((f) => [f, JSON.parse(readFileSync(resolve(CONTRACT_DIR, f), 'utf8')) as Example]);
}

describe('webhook envelope contract (contracts/webhook)', () => {
  it.each(examples())('%s is the envelope the service emits', (_file, example) => {
    const built = buildWebhookEnvelope(example.event, example.data, new Date(example.timestamp));

    expect(JSON.parse(JSON.stringify(built))).toEqual(example);
  });

  it('names this service’s network on every envelope', () => {
    for (const [, example] of examples()) {
      expect(example.network).toBe('TRON');
      expect(buildWebhookEnvelope(example.event, {}, new Date()).network).toBe('TRON');
    }
  });

  it('has one example per webhook event', () => {
    const covered = examples()
      .map(([, e]) => e.event)
      .sort();

    expect(covered).toEqual(Object.values(BlockchainWebhookEvents).sort());
  });
});
