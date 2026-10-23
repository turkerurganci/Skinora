import { Router } from 'express';
import { healthCheckFactory } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import { internalKeyAuth, networkGuard } from './middleware.js';
import { deriveAddressHandler, walletBalancesHandler } from './walletHandlers.js';
import {
  postCancelStartHandler,
  postCancelStopHandler,
  startMonitorHandler,
  stopMonitorHandler,
} from './monitorHandlers.js';
import {
  coldWalletTransferHandler,
  payoutHandler,
  refundHandler,
  sweepHandler,
  transferStatusHandler,
} from './transferHandlers.js';
import { estimateFeeHandler, estimateSweepFeeHandler } from './feeHandlers.js';
import { verifyHandler } from './verifyHandler.js';
import { WalletManager } from '../wallet/WalletManager.js';
import type { MonitorRegistry } from '../monitor/MonitorRegistry.js';
import type { PostCancelMonitorRegistry } from '../monitor/PostCancelMonitor.js';
import type { TransferService } from '../transfer/TransferService.js';
import type { RefundService } from '../transfer/RefundService.js';
import type { FeeEstimationService } from '../fee/FeeEstimationService.js';
import type { TransferVerifier } from '../transfer/TransferVerifier.js';

export interface RouterDeps {
  /**
   * T161 — the X-Internal-Key every /api route requires (05 §3.4). Required:
   * the router cannot be built without one (fail closed, config/internalKey.ts).
   */
  internalKey: string;
  walletManager: WalletManager;
  monitorRegistry: MonitorRegistry;
  postCancelMonitorRegistry: PostCancelMonitorRegistry;
  transferService: TransferService;
  refundService: RefundService;
  feeEstimationService: FeeEstimationService;
  /** T172a — `POST /api/transfer/verify`; absent = the endpoint answers 501. */
  transferVerifier?: TransferVerifier;
}

export function createRouter(deps: RouterDeps): Router {
  const router = Router();

  // Health check — no auth required
  router.get('/health', healthCheckFactory());

  // Prometheus metrics — no auth required (T16)
  router.get('/metrics', metricsHandler);

  // Authenticated API routes
  const apiRouter = Router();
  apiRouter.use(internalKeyAuth(deps.internalKey));
  // T148 (05 §3.3a) — every POST body names its network; one naming another
  // network than this service's is refused with 400 NETWORK_MISMATCH.
  apiRouter.use(networkGuard);

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
  //   → 503 { error: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED' | 'DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED'
  //          | 'DEPOSIT_REFUND_LEDGER_UNAVAILABLE', deferred: true }  (T172b — waits, nothing signed)
  apiRouter.post('/transfer/refund', refundHandler(deps.refundService));

  // POST /api/transfer/sweep { blockchainTransactionId, depositIndex, depositAddress, toHotWalletAddress, amount, token }
  //   → 200 { txHash }
  apiRouter.post('/transfer/sweep', sweepHandler(deps.transferService));

  // Hot wallet → cold wallet operational transfer — T77 (05 §3.3 hot wallet limit
  // alert flow). Backend orchestrates and persists the ColdWalletTransfer
  // ledger row using the returned txHash; the sidecar is signing-only.
  // POST /api/transfer/cold-wallet { coldTransferId, toColdAddress, amount, token }
  //   → 200 { txHash }
  //   → 400 { error: 'INVALID_TRANSFER_REQUEST' | 'HOT_WALLET_NOT_CONFIGURED' |
  //                  'INVALID_TRANSFER_AMOUNT' | 'TOKEN_CONTRACT_NOT_CONFIGURED' }
  //   → 502 { error: 'TRANSFER_BROADCAST_REJECTED' | 'TRANSFER_BROADCAST_FAILED' }
  apiRouter.post('/transfer/cold-wallet', coldWalletTransferHandler(deps.transferService));

  // Pre-send fee estimate — Prova-GasFeeChargedIsFixedGuess (2026-09-02)
  // POST /api/transfer/estimate-fee { fromAddress?, toAddress, amount, token }
  //   → 200 { feeUsdt, energyRequired, energyAvailable, energyShortfall,
  //           bandwidthRequired, bandwidthAvailable, burnSun, trxPriceUsdt, priceSource }
  //   → 400 { error: 'INVALID_ESTIMATE_REQUEST' | 'TOKEN_CONTRACT_NOT_CONFIGURED' | 'HOT_WALLET_NOT_CONFIGURED' }
  //   → 502 { error: 'FEE_ESTIMATE_*' | 'TRX_PRICE_UNAVAILABLE' }  (backend falls back to the static setting)
  apiRouter.post('/transfer/estimate-fee', estimateFeeHandler(deps.feeEstimationService));

  // T149 — the buyer's TRON network fee line basis (11 §F8 K8, 08 §3.1a)
  // POST /api/transfer/estimate-sweep-fee { network?, token }
  //   → 200 { sweepCostSun, activationSun, burnTopUpSun, bandwidthSun,
  //           energyRequired, trxPriceUsdt, priceSource, costUsdt }
  //   → 400 { error: 'INVALID_ESTIMATE_REQUEST' | 'TOKEN_CONTRACT_NOT_CONFIGURED' | 'HOT_WALLET_NOT_CONFIGURED' }
  //   → 502 { error: 'FEE_ESTIMATE_*' | 'TRX_PRICE_UNAVAILABLE' }  (backend writes the fallback line)
  apiRouter.post(
    '/transfer/estimate-sweep-fee',
    estimateSweepFeeHandler(deps.feeEstimationService),
  );

  // GET /api/transfer/status/:txHash
  //   → 200 { txHash, blockNumber?, contractRet?, confirmations }
  //   → 502 { error: 'TRANSFER_STATUS_HTTP_ERROR' }
  apiRouter.get('/transfer/status/:txHash', transferStatusHandler(deps.transferService));

  // T172a — the closure tool's chain check (same contract as the Ethereum
  // service's T157 endpoint; read-only, signs nothing)
  // POST /api/transfer/verify { network, txHash, token, from?, to, amount }
  //   → 200 { outcome: MATCH|NOT_FOUND|REVERTED|NO_MATCHING_TRANSFER, matches, blockNumber, matchingTransfers }
  //   → 400 { error: 'INVALID_VERIFY_REQUEST' }
  //   → 502 { error: 'TRANSFER_VERIFY_*' }  (the backend reads it as "not verified")
  const verifier = deps.transferVerifier;
  apiRouter.post(
    '/transfer/verify',
    verifier
      ? verifyHandler(verifier)
      : (_req, res) => {
          res
            .status(501)
            .json({ error: 'NOT_IMPLEMENTED', message: 'Transfer verification is not wired.' });
        },
  );

  // Reconciliation snapshot — T76 (05 §3.3)
  // POST /api/wallet/balances { addresses: string[] }
  //   → 200 { blockNumber, balances: [{ address, tokens: { TRX, USDT, USDC } }] }
  //   → 400 { error: 'INVALID_BALANCES_REQUEST' }
  //   → 502 { error: 'BALANCE_SNAPSHOT_FAILED' }
  apiRouter.post('/wallet/balances', walletBalancesHandler());

  router.use('/api', apiRouter);

  return router;
}
