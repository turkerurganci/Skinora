import crypto from 'node:crypto';
import { logger } from '../logger.js';
import { webhookRejectedTotal } from '../metrics.js';
import {
  WEBHOOK_ENDPOINTS,
  buildWebhookEnvelope,
  type BlockchainWebhookEvent,
} from './WebhookPayloads.js';

/**
 * Signed webhook delivery to the backend (T151 — 05 §3.4, 09 §11.3): the
 * TRON service's wire contract — HMAC-SHA256 over `timestamp + nonce + body`
 * with this service's own secret (`Webhook:EthereumSharedSecret` on the
 * backend, T148: one secret per network, never shared).
 */

export class WebhookDeliveryError extends Error {
  constructor(
    public readonly status: number,
    public readonly event: string,
  ) {
    super(`Webhook delivery failed (${status}) for event ${event}`);
    this.name = 'WebhookDeliveryError';
  }

  /**
   * 5xx, 408, 429, 401 and transport failures (status 0) are retried by
   * re-scanning; any other 4xx is the backend refusing the payload for good
   * (e.g. 409 `WEBHOOK_NETWORK_MISMATCH`) — re-sending cannot change that.
   *
   * T158 (j): 401 is the backend refusing the SIGNATURE — the two sides hold
   * different webhook secrets (a rotation done on one side, a typo). That is
   * not about the payment and is fixed by the operator; treating it as final
   * would drop every payment event sent until then, for good. The range is
   * read again until the secrets agree; the monitor's scan-failure alarm
   * (`ethereum-monitor-scan-failing`) and `skinora_ethereum_webhook_rejected_total{status="401"}`
   * show it meanwhile.
   */
  get retryable(): boolean {
    return (
      this.status === 0 ||
      this.status >= 500 ||
      this.status === 401 ||
      this.status === 408 ||
      this.status === 429
    );
  }
}

export interface WebhookSenderDeps {
  baseUrl: string;
  secret: string;
  fetchFn?: typeof fetch;
  clock?: () => Date;
}

export type SendWebhook = <TData>(
  event: BlockchainWebhookEvent,
  data: TData,
  correlationId: string,
) => Promise<void>;

export function createWebhookSender(deps: WebhookSenderDeps): SendWebhook {
  const fetchFn = deps.fetchFn ?? fetch;
  const clock = deps.clock ?? (() => new Date());
  return async (event, data, correlationId) => {
    if (!deps.secret) {
      // Retryable on purpose: nothing is lost, the scan stays on the range
      // until the secret is configured (acceptsPayments is false meanwhile).
      throw new WebhookDeliveryError(0, event);
    }
    const body = JSON.stringify(buildWebhookEnvelope(event, data, clock()));
    const timestamp = clock().toISOString();
    const nonce = crypto.randomUUID();
    const signature = crypto
      .createHmac('sha256', deps.secret)
      .update(`${timestamp}${nonce}${body}`)
      .digest('hex');

    let response: Response;
    try {
      response = await fetchFn(`${deps.baseUrl}${WEBHOOK_ENDPOINTS[event]}`, {
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
    } catch (err) {
      logger.warn(
        { event, correlationId, reason: (err as Error).message },
        'Webhook transport failure',
      );
      throw new WebhookDeliveryError(0, event);
    }
    if (!response.ok) {
      webhookRejectedTotal.inc({ status: String(response.status) });
      if (response.status === 401) {
        logger.error(
          { event, status: response.status, correlationId },
          'WEBHOOK SIGNATURE REFUSED (401) — the backend does not accept the webhook secret of this service (WEBHOOK_SECRET vs ETHEREUM_WEBHOOK_SECRET); the event is kept and sent again until the secrets agree',
        );
      } else {
        logger.error({ event, status: response.status, correlationId }, 'Webhook callback failed');
      }
      throw new WebhookDeliveryError(response.status, event);
    }
    logger.info({ event, correlationId }, 'Webhook callback sent');
  };
}
