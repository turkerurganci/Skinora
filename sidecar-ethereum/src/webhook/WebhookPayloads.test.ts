import crypto from 'node:crypto';
import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  BlockchainWebhookEvents,
  WEBHOOK_ENDPOINTS,
  buildWebhookEnvelope,
  type BlockchainWebhookEvent,
} from './WebhookPayloads.js';
import { WebhookDeliveryError, createWebhookSender } from './WebhookClient.js';

/**
 * T151 (05 §3.3a "Webhook yükü") — the Ethereum service's webhook bodies.
 * `contracts/webhook/*.json` are built here through the one function every
 * event is emitted with, and deserialized by the backend's
 * `BlockchainWebhookContractTests` into the controller's envelope types: a
 * rename on either side fails one of the two tests. The TRON service's
 * examples live in `sidecar-blockchain/contracts/webhook` — same events, same
 * fields, `network: "ETHEREUM"`.
 */
const CONTRACT_DIR = resolve(__dirname, '../../contracts/webhook');
const TRON_DIR = resolve(__dirname, '../../../sidecar-blockchain/contracts/webhook');

interface Example {
  event: BlockchainWebhookEvent;
  network: string;
  timestamp: string;
  data: Record<string, unknown>;
}

function examples(dir = CONTRACT_DIR): Array<[string, Example]> {
  return readdirSync(dir)
    .filter((f) => f.endsWith('.json'))
    .sort()
    .map((f) => [f, JSON.parse(readFileSync(resolve(dir, f), 'utf8')) as Example]);
}

describe('webhook envelope contract (contracts/webhook)', () => {
  it.each(examples())('%s is the envelope the service emits', (_file, example) => {
    const built = buildWebhookEnvelope(example.event, example.data, new Date(example.timestamp));
    expect(JSON.parse(JSON.stringify(built))).toEqual(example);
  });

  it('names ETHEREUM on every envelope', () => {
    for (const [, example] of examples()) expect(example.network).toBe('ETHEREUM');
  });

  it('has one example per event, and every TRON example has an Ethereum twin with the same fields', () => {
    expect(
      examples()
        .map(([, e]) => e.event)
        .sort(),
    ).toEqual(Object.values(BlockchainWebhookEvents).sort());
    const tron = new Map(examples(TRON_DIR));
    for (const [file, example] of examples()) {
      expect(Object.keys(example.data).sort(), file).toEqual(
        Object.keys(tron.get(file)!.data).sort(),
      );
    }
  });

  it('posts to the paths the backend controller serves', () => {
    expect(WEBHOOK_ENDPOINTS['payment.detected']).toBe(
      '/api/v1/webhooks/blockchain/payment-detected',
    );
    expect(WEBHOOK_ENDPOINTS['monitor.post_cancel_state_changed']).toBe(
      '/api/v1/webhooks/blockchain/post-cancel-monitor-state-changed',
    );
  });
});

describe('createWebhookSender — signed with this service’s own secret (05 §3.4)', () => {
  it('signs timestamp + nonce + body with HMAC-SHA256', async () => {
    const seen: Array<{ url: string; headers: Record<string, string>; body: string }> = [];
    const fixture = { hmacKey: ['test', 'hmac', 'key'].join('-') };
    const hmacKey = fixture.hmacKey;
    const send = createWebhookSender({
      baseUrl: 'http://backend',
      secret: fixture.hmacKey,
      fetchFn: (async (url: string, init: RequestInit) => {
        seen.push({
          url,
          headers: init.headers as Record<string, string>,
          body: String(init.body),
        });
        return new Response('{}', { status: 200 });
      }) as unknown as typeof fetch,
    });
    await send('payment.detected', { txHash: '0x1' }, 'cid');
    const [req] = seen;
    expect(req.url).toBe('http://backend/api/v1/webhooks/blockchain/payment-detected');
    const expected = crypto
      .createHmac('sha256', hmacKey)
      .update(`${req.headers['X-Timestamp']}${req.headers['X-Nonce']}${req.body}`)
      .digest('hex');
    expect(req.headers['X-Signature']).toBe(expected);
    expect(JSON.parse(req.body)).toMatchObject({ event: 'payment.detected', network: 'ETHEREUM' });
  });

  it.each([
    [503, true],
    [429, true],
    [0, true],
    [409, false],
    [400, false],
  ])('HTTP %i → retryable %s', (status, retryable) => {
    expect(new WebhookDeliveryError(status, 'payment.detected').retryable).toBe(retryable);
  });

  it('without a secret it sends nothing and fails retryably (nothing is lost)', async () => {
    let called = false;
    const send = createWebhookSender({
      baseUrl: 'http://backend',
      secret: '',
      fetchFn: (async () => {
        called = true;
        return new Response('{}');
      }) as unknown as typeof fetch,
    });
    await expect(send('payment.detected', {}, 'cid')).rejects.toMatchObject({ retryable: true });
    expect(called).toBe(false);
  });
});
