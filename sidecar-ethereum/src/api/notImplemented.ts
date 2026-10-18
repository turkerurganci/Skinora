import type { Request, Response } from 'express';

/**
 * The per-network contract's endpoints this skeleton does not serve yet
 * (05 §3.3a "Uçlar"), and the task that brings each (11 §F8 P3). T151's
 * monitor and balance endpoints are served by routes.ts (`PAYMENT_ENDPOINTS`).
 */
export const NOT_YET_IMPLEMENTED: readonly {
  readonly method: 'get' | 'post';
  readonly path: string;
  readonly task: string;
}[] = [
  // Empty since T151 + T152 + T154 joined on main: every endpoint of the
  // contract is served. The 501 handler below still answers T151's monitor
  // endpoints when routes.ts gets no `payments` deps (the skeleton shape).
];

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
      message: `This endpoint of the Ethereum service arrives with ${task}; it is not served yet.`,
      retryable: true,
    });
  };
}
