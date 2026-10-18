import { config } from './config.js';
import { logger } from './logger.js';
import { signWebhook } from './hmac.js';

/**
 * The backend refused a webhook. Carries its status and body so the control
 * surface can hand them to the test — a refusal is an OUTCOME a negative test
 * asserts on (T155: a webhook claiming the wrong network must come back 409
 * `WEBHOOK_NETWORK_MISMATCH`), not only an error to log.
 */
export class WebhookRejectedError extends Error {
  constructor(
    readonly path: string,
    readonly status: number,
    readonly responseBody: string,
  ) {
    super(`webhook ${path} failed: ${status} ${responseBody}`);
    this.name = 'WebhookRejectedError';
  }
}

/**
 * POST a signed inbound webhook to the backend
 * (`/api/v1/webhooks/blockchain/...`). The envelope shape matches the
 * backend's `*WebhookEnvelope<TData>` ({ event, network, timestamp, data }) —
 * callers pass the already-built envelope object and the secret of the
 * network that is speaking (T148: the secret that verifies decides which
 * network the caller IS).
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
    throw new WebhookRejectedError(path, response.status, text);
  }
  logger.info({ url, correlationId }, 'Webhook POST ok');
}
