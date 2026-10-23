import crypto from 'crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { configureWebhookSecret, sendCallback, type WebhookFetch } from './WebhookClient.js';
import type { AnyBlockchainWebhookPayload } from './WebhookPayloads.js';

/**
 * T171a — the webhook HMAC secret reaches WebhookClient only through
 * configureWebhookSecret (the entry point hands over the value it read from
 * the Docker secret file, config/sharedSecrets.ts); nothing reads it from the
 * environment any more.
 */
const SECRET = 'test-webhook-secret-'.padEnd(64, 'w');
const PAYLOAD = { event: 'test' } as unknown as AnyBlockchainWebhookPayload;

function capture() {
  const calls: Array<{ headers: Record<string, string>; body: string }> = [];
  const fetchFn = (async (
    _url: string,
    init: { headers: Record<string, string>; body: string },
  ) => {
    calls.push({ headers: init.headers, body: init.body });
    return new Response('{}', { status: 200 });
  }) as unknown as WebhookFetch;
  return { calls, fetchFn };
}

describe('WebhookClient — the secret comes from configureWebhookSecret (T171a)', () => {
  afterEach(() => configureWebhookSecret(''));

  it('refuses to send before a secret is configured', async () => {
    const { calls, fetchFn } = capture();
    await expect(
      sendCallback('/x', PAYLOAD, 'c', { baseUrl: 'http://b', fetchFn }),
    ).rejects.toThrow(/not configured/);
    expect(calls).toHaveLength(0);
  });

  it('signs timestamp + nonce + body with the configured secret', async () => {
    configureWebhookSecret(SECRET);
    const { calls, fetchFn } = capture();
    await sendCallback('/x', PAYLOAD, 'c', { baseUrl: 'http://b', fetchFn });

    const [call] = calls;
    const expected = crypto
      .createHmac('sha256', SECRET)
      .update(`${call.headers['X-Timestamp']}${call.headers['X-Nonce']}${call.body}`)
      .digest('hex');
    expect(call.headers['X-Signature']).toBe(expected);
  });

  it('the entry point hands the loaded secret over before anything can send', () => {
    // index.ts is the process (it listens on import); its wiring is pinned by text.
    const entry = readFileSync(resolve(__dirname, '../index.ts'), 'utf8');
    const handOver = entry.indexOf('configureWebhookSecret(sharedSecrets.webhookSecret);');
    expect(handOver).toBeGreaterThan(-1);
    expect(handOver).toBeLessThan(entry.indexOf('new MonitorRegistry('));
  });
});
