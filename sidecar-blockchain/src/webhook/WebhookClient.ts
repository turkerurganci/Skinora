import crypto from 'crypto';
import { config } from '../config/index.js';
import { logger } from '../logger.js';
import type { WebhookPayload } from './WebhookPayloads.js';

/**
 * Sends webhook callbacks to the .NET backend with HMAC-SHA256 signing.
 * Implements 05 §3.4 inbound security: signature + timestamp + nonce.
 */
export async function sendCallback(
  endpoint: string,
  payload: WebhookPayload,
  correlationId: string,
): Promise<void> {
  const timestamp = new Date().toISOString();
  const nonce = crypto.randomUUID();
  const body = JSON.stringify(payload);

  const signature = crypto
    .createHmac('sha256', config.webhookSecret)
    .update(`${timestamp}${nonce}${body}`)
    .digest('hex');

  const url = `${config.backendUrl}${endpoint}`;

  logger.debug({ url, event: payload.event, correlationId }, 'Sending webhook callback');

  const response = await fetch(url, {
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
    logger.error({ url, status: response.status, correlationId }, 'Webhook callback failed');
    throw new Error(`Webhook callback failed: ${response.status} ${response.statusText}`);
  }

  logger.info({ url, event: payload.event, correlationId }, 'Webhook callback sent');
}
