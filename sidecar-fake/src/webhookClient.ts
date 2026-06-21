import { config } from './config.js';
import { logger } from './logger.js';
import { signWebhook } from './hmac.js';

/**
 * POST a signed inbound webhook to the backend
 * (`/api/v1/webhooks/{steam,blockchain}/...`). The envelope shape matches the
 * backend's `*WebhookEnvelope<TData>` ({ event, timestamp, data }) — callers
 * pass the already-built envelope object.
 */
export async function postWebhook(
  path: string,
  secret: string,
  envelope: unknown,
  correlationId: string,
): Promise<void> {
  const body = JSON.stringify(envelope);
  const signed = signWebhook(secret, body);
  const url = `${config.backendUrl}${path}`;

  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Signature': signed.signature,
      'X-Timestamp': signed.timestamp,
      'X-Nonce': signed.nonce,
      'X-Correlation-Id': correlationId,
    },
    body,
  });

  if (!response.ok) {
    const text = await response.text().catch(() => '');
    logger.error(
      { url, status: response.status, body: text, correlationId },
      'Webhook POST failed',
    );
    throw new Error(`webhook ${path} failed: ${response.status} ${text}`);
  }
  logger.info({ url, correlationId }, 'Webhook POST ok');
}
