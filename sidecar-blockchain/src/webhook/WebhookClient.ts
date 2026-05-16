import crypto from 'crypto';
import { config } from '../config/index.js';
import { logger } from '../logger.js';
import type { AnyBlockchainWebhookPayload } from './WebhookPayloads.js';

export type WebhookFetch = typeof fetch;

export interface WebhookClientDeps {
  baseUrl?: string;
  secret?: string;
  fetchFn?: WebhookFetch;
}

/**
 * Send a signed webhook callback to the .NET backend. Mirrors the Steam
 * sidecar's wire contract (05 §3.4, 09 §11.3): HMAC-SHA256 over
 * `timestamp + nonce + body` with the secret loaded from `WEBHOOK_SECRET`.
 *
 * <para>
 * Caller is responsible for retry on transient failure. The active monitor
 * treats a 4xx as terminal (mis-routed payload) and a 5xx / network error
 * as retryable — see `MonitorRegistry.deliverWebhook`.
 * </para>
 */
export async function sendCallback(
  endpoint: string,
  payload: AnyBlockchainWebhookPayload,
  correlationId: string,
  deps: WebhookClientDeps = {},
): Promise<void> {
  const baseUrl = deps.baseUrl ?? config.backendUrl;
  const secret = deps.secret ?? config.webhookSecret;
  const fetchFn = deps.fetchFn ?? fetch;

  if (!secret) {
    throw new Error('WEBHOOK_SECRET is not configured — refusing to send webhook');
  }

  const timestamp = new Date().toISOString();
  const nonce = crypto.randomUUID();
  const body = JSON.stringify(payload);

  const signature = crypto
    .createHmac('sha256', secret)
    .update(`${timestamp}${nonce}${body}`)
    .digest('hex');

  const url = `${baseUrl}${endpoint}`;

  logger.debug({ url, event: payload.event, correlationId }, 'Sending webhook callback');

  const response = await fetchFn(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Signature': signature,
      'X-Timestamp': timestamp,
      'X-Nonce': nonce,
      'X-Correlation-Id': correlationId,
    },
    body,
  });

  if (!response.ok) {
    logger.error(
      { url, status: response.status, event: payload.event, correlationId },
      'Webhook callback failed',
    );
    throw new WebhookDeliveryError(response.status, response.statusText, payload.event);
  }

  logger.info({ url, event: payload.event, correlationId }, 'Webhook callback sent');
}

export class WebhookDeliveryError extends Error {
  constructor(
    public readonly status: number,
    public readonly statusText: string,
    public readonly event: string,
  ) {
    super(`Webhook delivery failed (${status} ${statusText}) for event ${event}`);
    this.name = 'WebhookDeliveryError';
  }

  get retryable(): boolean {
    return this.status >= 500 || this.status === 408 || this.status === 429;
  }
}
