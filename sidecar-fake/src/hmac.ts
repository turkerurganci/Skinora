import crypto from 'crypto';

export interface SignedWebhook {
  body: string;
  timestamp: string;
  nonce: string;
  signature: string;
}

/**
 * Sign a webhook body exactly as the real sidecars do
 * (sidecar-steam / sidecar-blockchain WebhookClient.ts, 05 §3.4): HMAC-SHA256 over
 * `timestamp + nonce + body` with the per-sidecar shared secret. The backend's
 * `WebhookSignatureMiddleware` recomputes the same string and constant-time
 * compares the lowercase hex digest.
 *
 * @param secret  steam OR blockchain shared secret (must match the route prefix)
 * @param body    the exact JSON string that will be sent as the request body
 */
export function signWebhook(secret: string, body: string): SignedWebhook {
  if (!secret) {
    throw new Error('webhook secret is not configured — refusing to sign');
  }
  const timestamp = new Date().toISOString();
  const nonce = crypto.randomUUID();
  const signature = crypto
    .createHmac('sha256', secret)
    .update(`${timestamp}${nonce}${body}`)
    .digest('hex');
  return { body, timestamp, nonce, signature };
}
