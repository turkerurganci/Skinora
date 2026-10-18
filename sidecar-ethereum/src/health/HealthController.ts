import type { Request, Response } from 'express';
import { SERVICE_NETWORK } from '../network.js';
import { SERVICE_NAME } from '../logger.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';

interface HealthCheck {
  name: string;
  status: 'healthy' | 'degraded' | 'unhealthy';
  message?: string;
}

/**
 * `/health` of the Ethereum service (05 §3.3a "Sağlık ve dondurma",
 * 08 §3.7.4 "Dondurma", §3.7.8).
 *
 * <para>
 * Liveness only, like the TRON service (WP5): the answer is about whether this
 * service can do its job against the chain right now, not about configuration
 * completeness. It reads the {@link FinalityTracker}'s last snapshot and never
 * calls a provider itself — a tight probe loop cannot spend RPC quota.
 * </para>
 *
 * <list type="bullet">
 *   <item><b>unhealthy (503)</b> — no provider answered for three poll
 *     intervals, the finalized head is older than the stall threshold, or the
 *     first read has not finished. The backend's probe turns 503 into
 *     `BLOCKCHAIN_ETHEREUM` degraded: Ethereum's payment timeouts freeze and
 *     Ethereum closes for new transactions; TRON is untouched (T148).</item>
 *   <item><b>degraded (200)</b> — the primary provider is failing and the
 *     backup answers.</item>
 *   <item><b>healthy (200)</b>.</item>
 * </list>
 */
export function healthCheckFactory(deps: { finality: () => FinalityStatus; chainId: number }) {
  return function healthCheck(_req: Request, res: Response): void {
    const finality = deps.finality();
    const check = finalityCheck(finality);
    const statusCode = check.status === 'unhealthy' ? 503 : 200;
    res.status(statusCode).json({
      status: check.status,
      service: SERVICE_NAME,
      network: SERVICE_NETWORK,
      chainId: deps.chainId,
      uptime: process.uptime(),
      finalized:
        finality.snapshot === null
          ? null
          : {
              blockNumber: String(finality.snapshot.number),
              ageSeconds: finality.ageSeconds,
              provider: finality.snapshot.provider,
            },
      checks: [check],
    });
  };
}

function finalityCheck(finality: FinalityStatus): HealthCheck {
  switch (finality.state) {
    case 'ok':
      return {
        name: 'finality',
        status: 'healthy',
        message: `finalized block ${String(finality.snapshot?.number)}, ${finality.ageSeconds}s old`,
      };
    case 'backup':
      return {
        name: 'finality',
        status: 'degraded',
        message: `primary RPC provider failing — read by ${finality.snapshot?.provider}`,
      };
    case 'stalled':
      return {
        name: 'finality',
        status: 'unhealthy',
        message: `finalized block is ${finality.ageSeconds}s old — finality has stalled`,
      };
    case 'unreadable':
      return {
        name: 'finality',
        status: 'unhealthy',
        message: `no RPC provider answered: ${finality.lastError ?? 'unknown error'}`,
      };
    case 'starting':
    default:
      return { name: 'finality', status: 'unhealthy', message: 'first finalized read pending' };
  }
}
