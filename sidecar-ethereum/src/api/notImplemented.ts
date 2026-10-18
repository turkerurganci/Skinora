import type { Request, Response } from 'express';

/**
 * The per-network contract's endpoints this skeleton does not serve yet
 * (05 §3.3a "Uçlar"), and the task that brings each (11 §F8 P3).
 */
export const NOT_YET_IMPLEMENTED = [
  { method: 'post', path: '/wallet/balances', task: 'T151' },
  { method: 'post', path: '/monitor/start', task: 'T151' },
  { method: 'post', path: '/monitor/stop', task: 'T151' },
  { method: 'post', path: '/monitor/post-cancel-start', task: 'T151' },
  { method: 'post', path: '/monitor/post-cancel-stop', task: 'T151' },
  { method: 'post', path: '/transfer/payout', task: 'T152' },
  { method: 'post', path: '/transfer/refund', task: 'T152' },
  { method: 'post', path: '/transfer/sweep', task: 'T152' },
  { method: 'post', path: '/transfer/cold-wallet', task: 'T152' },
  { method: 'get', path: '/transfer/status/:txHash', task: 'T152' },
] as const;

/**
 * 501 `NOT_IMPLEMENTED`, with no side effect: nothing is read, signed, sent
 * or remembered. 501 is deliberately not 400 — the backend treats a 400 on a
 * transfer as a terminal rejection (`FAILED` + alert), and a 5xx as transient:
 * a backend pointed at the skeleton by mistake keeps its rows to retry rather
 * than failing them, and the outbox backs off (T150).
 */
export function notImplementedHandler(task: string) {
  return (req: Request, res: Response): void => {
    req.log.warn({ url: req.url, task }, 'Endpoint not implemented in the Ethereum service yet');
    res.status(501).json({
      error: 'NOT_IMPLEMENTED',
      message: `This endpoint of the Ethereum service arrives with ${task}; so far the service serves /api/wallet/derive and /api/transfer/estimate-fee.`,
      retryable: true,
    });
  };
}
