import type { UsdtState } from '../token/UsdtStatus.js';

/**
 * The `acceptsPayments` capability flag of `/health` (T151 kod düzeyi kapı —
 * 11 §F8 T151, T150 doğrulamasından).
 *
 * <para>
 * The backend opens Ethereum for new transactions only while this service
 * says it can see a payment: its probe treats a `/health` body without
 * `acceptsPayments: true` as unhealthy and keeps Ethereum out of
 * `supportedNetworks`. Before T151 the skeleton answered `monitor/start` with
 * 501 — a backend pointed at it would have shown deposit addresses nobody
 * watched and the money would have stayed there unseen.
 * </para>
 *
 * The flag is true only when all of these hold:
 * <list type="bullet">
 *   <item>the payment monitor is running;</item>
 *   <item>the webhook secret is configured (a payment seen but not
 *     reportable is not accepted);</item>
 *   <item>the USDT contract is configured and its fee, pause and
 *     deprecation switches read off at the finalized block (08 §3.7.1).</item>
 * </list>
 */

export interface PaymentReadiness {
  readonly acceptsPayments: boolean;
  /** Why not — empty when accepting. */
  readonly reasons: readonly string[];
  readonly usdt: UsdtState;
}

export function paymentReadiness(input: {
  monitorRunning: boolean;
  webhookConfigured: boolean;
  usdt: UsdtState;
}): PaymentReadiness {
  const reasons: string[] = [];
  if (!input.monitorRunning) reasons.push('MONITOR_NOT_RUNNING');
  if (!input.webhookConfigured) reasons.push('WEBHOOK_SECRET_MISSING');
  if (input.usdt !== 'ok') reasons.push(`USDT_${input.usdt.toUpperCase()}`);
  return { acceptsPayments: reasons.length === 0, reasons, usdt: input.usdt };
}

/** Readiness of a service whose monitor is not wired (the T150 skeleton shape). */
export const NOT_ACCEPTING: PaymentReadiness = {
  acceptsPayments: false,
  reasons: ['MONITOR_NOT_RUNNING'],
  usdt: 'unknown',
};
