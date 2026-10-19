import { describe, expect, it } from 'vitest';
import client from 'prom-client';
import { WebhookDeliveryError, createWebhookSender } from './WebhookClient.js';
import { BlockchainWebhookEvents } from './WebhookPayloads.js';

/**
 * T158 (j) — the backend refusing the webhook signature (401) is counted
 * (`skinora_ethereum_webhook_rejected_total{status="401"}`, the source of the
 * `ethereum-webhook-signature-refused` alarm) and kept retryable. The
 * scanner tests use their own sender; this one drives the real client.
 */

async function rejected(status: string): Promise<number> {
  const metric = await client.register
    .getSingleMetric('skinora_ethereum_webhook_rejected_total')
    ?.get();
  return (metric?.values ?? [])
    .filter((v) => v.labels.status === status)
    .reduce((s, v) => s + v.value, 0);
}

function senderAnswering(status: number) {
  const calls: string[] = [];
  const send = createWebhookSender({
    baseUrl: 'http://backend.test',
    secret: 'test-secret',
    fetchFn: (async (url: string) => {
      calls.push(url);
      return new Response('{}', { status });
    }) as unknown as typeof fetch,
  });
  return { send, calls };
}

describe('WebhookClient — backend refusals (T158 j)', () => {
  it('a 401 answer increments the rejected counter for 401 and throws a retryable error', async () => {
    const before = await rejected('401');
    const { send, calls } = senderAnswering(401);
    const err = await send(BlockchainWebhookEvents.PaymentDetected, { a: 1 }, 'corr-1').catch(
      (e: unknown) => e,
    );
    expect(calls).toHaveLength(1);
    expect(err).toBeInstanceOf(WebhookDeliveryError);
    expect((err as WebhookDeliveryError).status).toBe(401);
    expect((err as WebhookDeliveryError).retryable).toBe(true);
    expect(await rejected('401')).toBe(before + 1);
  });

  it('a 409 is counted under its own status and is final', async () => {
    const before401 = await rejected('401');
    const before409 = await rejected('409');
    const { send } = senderAnswering(409);
    const err = await send(BlockchainWebhookEvents.PaymentDetected, { a: 1 }, 'corr-2').catch(
      (e: unknown) => e,
    );
    expect((err as WebhookDeliveryError).retryable).toBe(false);
    expect(await rejected('409')).toBe(before409 + 1);
    expect(await rejected('401')).toBe(before401);
  });

  it('control: a 200 answer counts nothing', async () => {
    const before = await rejected('401');
    const { send } = senderAnswering(200);
    await send(BlockchainWebhookEvents.PaymentDetected, { a: 1 }, 'corr-3');
    expect(await rejected('401')).toBe(before);
    expect(await rejected('200')).toBe(0);
  });
});
