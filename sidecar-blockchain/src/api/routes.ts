import { Router } from 'express';
import { healthCheck } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import { internalKeyAuth } from './middleware.js';
import { deriveAddressHandler } from './walletHandlers.js';
import {
  postCancelStartHandler,
  postCancelStopHandler,
  startMonitorHandler,
  stopMonitorHandler,
} from './monitorHandlers.js';
import {
  payoutHandler,
  refundHandler,
  sweepHandler,
  transferStatusHandler,
} from './transferHandlers.js';
import { WalletManager } from '../wallet/WalletManager.js';
import type { MonitorRegistry } from '../monitor/MonitorRegistry.js';
import type { PostCancelMonitorRegistry } from '../monitor/PostCancelMonitor.js';
import type { TransferService } from '../transfer/TransferService.js';
import type { RefundService } from '../transfer/RefundService.js';

export interface RouterDeps {
  walletManager: WalletManager;
  monitorRegistry: MonitorRegistry;
  postCancelMonitorRegistry: PostCancelMonitorRegistry;
  transferService: TransferService;
  refundService: RefundService;
}

export function createRouter(deps: RouterDeps): Router {
  const router = Router();

  // Health check — no auth required
  router.get('/health', healthCheck);

  // Prometheus metrics — no auth required (T16)
  router.get('/metrics', metricsHandler);

  // Authenticated API routes
  const apiRouter = Router();
  apiRouter.use(internalKeyAuth);

  // Wallet management — T70
  // POST /api/wallet/derive { index, transactionId? }
  //   → 200 { address, derivationPath, index }
  //   → 400 { error: 'INVALID_DERIVATION_INDEX' }
  //   → 503 { error: 'HD_WALLET_NOT_CONFIGURED' }
  apiRouter.post('/wallet/derive', deriveAddressHandler(deps.walletManager));

  // Payment monitoring — T71
  // POST /api/monitor/start { address, paymentAddressId, transactionId, expectedContract, expectedSymbol }
  //   → 200 { acknowledged: true, started: boolean, address }
  //   → 400 { error: 'INVALID_MONITOR_REQUEST' | 'UNSUPPORTED_SYMBOL' }
  apiRouter.post('/monitor/start', startMonitorHandler(deps.monitorRegistry));

  // POST /api/monitor/stop { address }
  //   → 200 { acknowledged: true, stopped: boolean, address }
  apiRouter.post('/monitor/stop', stopMonitorHandler(deps.monitorRegistry));

  // Post-cancel monitoring — T75 (08 §3.4 gecikmeli ödeme)
  // POST /api/monitor/post-cancel-start
  //   { address, paymentAddressId, transactionId, expectedContract, expectedSymbol,
  //     cancelledAt, initialState?, initialStateExpiresAt? }
  //   → 200 { acknowledged: true, started: boolean, state: 'POST_CANCEL_*' | 'STOPPED', address }
  //   → 400 { error: 'INVALID_POST_CANCEL_REQUEST' | 'UNSUPPORTED_SYMBOL' |
  //                  'INVALID_CANCELLED_AT' | 'INVALID_INITIAL_STATE' |
  //                  'INVALID_STATE_EXPIRES_AT' }
  apiRouter.post(
    '/monitor/post-cancel-start',
    postCancelStartHandler(deps.postCancelMonitorRegistry),
  );

  // POST /api/monitor/post-cancel-stop { address }
  //   → 200 { acknowledged: true, stopped: boolean, address }
  apiRouter.post(
    '/monitor/post-cancel-stop',
    postCancelStopHandler(deps.postCancelMonitorRegistry),
  );

  // Transfers — T73
  // POST /api/transfer/payout { blockchainTransactionId, toAddress, amount, token }
  //   → 200 { txHash }
  //   → 400 { error: 'INVALID_TRANSFER_REQUEST' | 'HOT_WALLET_NOT_CONFIGURED' | 'INVALID_TRANSFER_AMOUNT' | 'TOKEN_CONTRACT_NOT_CONFIGURED' }
  //   → 502 { error: 'TRANSFER_BROADCAST_REJECTED' | 'TRANSFER_BROADCAST_FAILED' }  (retryable)
  apiRouter.post('/transfer/payout', payoutHandler(deps.transferService));

  // POST /api/transfer/refund { blockchainTransactionId, depositIndex, depositAddress, toBuyerAddress, amount, token }
  //   → 200 { txHash }
  //   Same error shape as /payout (plus DEPOSIT_ADDRESS_MISMATCH 400).
  apiRouter.post('/transfer/refund', refundHandler(deps.refundService));

  // POST /api/transfer/sweep { blockchainTransactionId, depositIndex, depositAddress, toHotWalletAddress, amount, token }
  //   → 200 { txHash }
  apiRouter.post('/transfer/sweep', sweepHandler(deps.transferService));

  // GET /api/transfer/status/:txHash
  //   → 200 { txHash, blockNumber?, contractRet?, confirmations }
  //   → 502 { error: 'TRANSFER_STATUS_HTTP_ERROR' }
  apiRouter.get('/transfer/status/:txHash', transferStatusHandler(deps.transferService));

  // Balance check — T77
  apiRouter.get('/wallet/hot-wallet-balance', (_req, res) => {
    res.status(501).json({ error: 'Not implemented — see T77' });
  });

  router.use('/api', apiRouter);

  return router;
}
