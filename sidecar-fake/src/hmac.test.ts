import { describe, it, expect } from 'vitest';
import crypto from 'crypto';
import { signWebhook } from './hmac.js';

describe('signWebhook', () => {
  const secret = 'e2e-test-secret-minimum-32-characters-long!';
  const body = JSON.stringify({ event: 'payment.detected', data: { amount: '10.000000' } });

  it('produces a lowercase 64-hex HMAC-SHA256 over timestamp+nonce+body', () => {
    const signed = signWebhook(secret, body);

    expect(signed.signature).toMatch(/^[0-9a-f]{64}$/);

    const expected = crypto
      .createHmac('sha256', secret)
      .update(`${signed.timestamp}${signed.nonce}${signed.body}`)
      .digest('hex');
    expect(signed.signature).toBe(expected);
  });

  it('returns the body unchanged and a fresh nonce each call', () => {
    const a = signWebhook(secret, body);
    const b = signWebhook(secret, body);
    expect(a.body).toBe(body);
    expect(a.nonce).not.toBe(b.nonce);
  });

  it('throws when the secret is empty', () => {
    expect(() => signWebhook('', body)).toThrow();
  });
});
