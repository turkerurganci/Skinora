import { Router } from 'express';
import type { NextFunction, Request, Response } from 'express';
import { getFees, recordCall, startMonitor, stopMonitor } from '../blockchainState.js';
import { weiToUsdtCentsCeil } from '../feeMath.js';
import { type NetworkProfile, type StablecoinSymbol, symbolForContract } from '../networks.js';

/**
 * The backend → blockchain-service surface for ONE network (T155). The TRON
 * router answers `BlockchainSidecar__BaseUrl` (port 5200), the Ethereum router
 * `EthereumSidecar__BaseUrl` (port 5300); the paths are the same on both
 * (05 §3.3a — one contract, one service per network), the formats are not.
 *
 * What the fake checks — and why. It signs nothing and moves nothing, so the
 * only way it can be WRONG in a way a test notices is by accepting a call the
 * real service would refuse. Each check below is one the real service makes and
 * one a cross-network mix-up in the backend would trip:
 *
 *   • `network` in every POST body names this network (T148 iv / T150 iv) —
 *     400 `NETWORK_MISMATCH`, which the backend treats as a permanent refusal;
 *   • every address in the body has this network's shape — a TRON refund
 *     address sent through the Ethereum service (or the reverse) is 400
 *     `INVALID_ADDRESS`, so the transfer row never reaches CONFIRMED;
 *   • `monitor/start` names this network's contract for the symbol — arming an
 *     Ethereum deposit with the TRON USDT contract would classify every real
 *     payment as a wrong token (StablecoinContractRegistry remarks);
 *   • `transfer/status` takes only this network's hash format.
 *
 * Every call is written to the journal (blockchainState.ts) with the status it
 * was answered with, so a test can assert which service the backend used.
 */
export function createBlockchainRouter(profile: NetworkProfile): Router {
  const router = Router();
  const surface = ['/api/wallet', '/api/monitor', '/api/transfer'];

  router.use(surface, journalCalls(profile));
  router.use(surface, networkGuard(profile));

  router.post('/api/wallet/derive', (req, res) => {
    const raw = (req.body as { index?: unknown })?.index;
    // Ethereum is strict like the real service (walletHandlers.ts); TRON keeps
    // the fake's original lenient read (an absent index derives 0).
    const index = profile.network === 'ETHEREUM' ? raw : Number(raw ?? 0);
    if (typeof index !== 'number' || !Number.isInteger(index) || index < 0) {
      res.status(400).json({
        error: 'INVALID_DERIVATION_INDEX',
        message: 'Field "index" must be a non-negative integer.',
      });
      return;
    }
    res.json({
      address: profile.depositAddress(index),
      derivationPath: profile.derivationPath(index),
      index,
      // T158 (h) — the real Ethereum service names the finalized head the
      // address was derived at; the backend persists it as the deposit's
      // start block and refuses an Ethereum derive answer without it.
      ...(profile.network === 'ETHEREUM' ? { watchFromBlock: String(profile.blockNumber) } : {}),
    });
  });

  router.post('/api/wallet/balances', (req, res) => {
    const addresses = (req.body as { addresses?: unknown })?.addresses;
    const list = Array.isArray(addresses) ? addresses.map(String) : [];
    const foreign = list.find((a) => !profile.isAddress(a));
    if (foreign !== undefined) {
      invalidAddress(res, profile, 'addresses', foreign);
      return;
    }
    res.json({
      blockNumber: profile.blockNumber,
      balances: list.map((address) => ({ address, tokens: {} })),
    });
  });

  router.post('/api/monitor/start', (req, res) => {
    const body = (req.body ?? {}) as Record<string, unknown>;
    const address = str(body.address);
    const paymentAddressId = str(body.paymentAddressId);
    const transactionId = str(body.transactionId);
    const expectedContract = str(body.expectedContract);
    const expectedSymbol = str(body.expectedSymbol);
    if (!address || !paymentAddressId || !transactionId || !expectedContract || !expectedSymbol) {
      res.status(400).json({
        error: 'INVALID_MONITOR_REQUEST',
        message:
          'Required fields: address, paymentAddressId, transactionId, expectedContract, expectedSymbol.',
      });
      return;
    }
    if (!profile.isAddress(address)) {
      invalidAddress(res, profile, 'address', address);
      return;
    }
    if (expectedSymbol !== 'USDT' && expectedSymbol !== 'USDC') {
      res.status(400).json({
        error: 'UNSUPPORTED_SYMBOL',
        message: 'expectedSymbol must be one of: USDT, USDC',
      });
      return;
    }
    if (symbolForContract(profile, expectedContract) !== (expectedSymbol as StablecoinSymbol)) {
      res.status(400).json({
        error: 'EXPECTED_CONTRACT_MISMATCH',
        message: `${expectedContract} is not the ${profile.network} ${expectedSymbol} contract.`,
      });
      return;
    }
    const cadence = body.cadence === undefined ? 'PAYMENT' : body.cadence;
    if (cadence !== 'PAYMENT' && cadence !== 'HOLDING') {
      res.status(400).json({
        error: 'UNSUPPORTED_CADENCE',
        message: 'cadence must be one of: PAYMENT, HOLDING',
      });
      return;
    }
    const started = startMonitor({
      network: profile.network,
      address,
      paymentAddressId,
      transactionId,
      expectedContract,
      expectedSymbol,
      cadence,
    });
    res.json({ acknowledged: true, started, cadence, address });
  });

  router.post('/api/monitor/stop', (req, res) => {
    const address = str((req.body as { address?: unknown })?.address);
    if (!address) {
      res.status(400).json({ error: 'INVALID_MONITOR_REQUEST', message: 'address is required.' });
      return;
    }
    const stopped = stopMonitor(profile.network, address);
    res.json({ acknowledged: true, stopped, address });
  });

  // Post-cancel watching (03 §5.4) is driven by the late-payment lever, not by
  // a scan, so the fake only checks the address and acknowledges.
  for (const path of ['/api/monitor/post-cancel-start', '/api/monitor/post-cancel-stop']) {
    router.post(path, (req, res) => {
      const address = str((req.body as { address?: unknown })?.address);
      if (address && !profile.isAddress(address)) {
        invalidAddress(res, profile, 'address', address);
        return;
      }
      res.json({ status: 'ok' });
    });
  }

  // payout / refund / sweep / cold-wallet share the { txHash } success shape.
  // Each names its destination in its own field (HttpBlockchainTransferClient
  // PayoutBody / RefundBody / SweepBody, HttpBlockchainSidecarClient
  // ColdTransferRequestBody); a deposit-sourced transfer also names the deposit.
  const transfers: Array<[string, string[]]> = [
    ['/api/transfer/payout', ['toAddress']],
    ['/api/transfer/refund', ['depositAddress', 'toBuyerAddress']],
    ['/api/transfer/sweep', ['depositAddress', 'toHotWalletAddress']],
    ['/api/transfer/cold-wallet', ['toColdAddress']],
  ];
  for (const [path, defaultFields] of transfers) {
    router.post(path, (req, res) => {
      const body = (req.body ?? {}) as Record<string, unknown>;
      // T158 (f) — an Ethereum refund after the sweep leaves the hot wallet:
      // `source: "HOT_WALLET"`, no deposit fields (the real service refuses
      // either deposit field on that body, and TRON has no such source).
      let addressFields = defaultFields;
      if (path === '/api/transfer/refund' && body.source !== undefined) {
        const hot =
          profile.network === 'ETHEREUM' &&
          body.source === 'HOT_WALLET' &&
          body.depositIndex === undefined &&
          body.depositAddress === undefined;
        if (!hot && !(profile.network === 'ETHEREUM' && body.source === 'DEPOSIT')) {
          res.status(400).json({
            error: 'INVALID_TRANSFER_REQUEST',
            message: `source ${String(body.source)} is not accepted with this body on ${profile.network}.`,
          });
          return;
        }
        if (hot) addressFields = ['toBuyerAddress'];
      }
      for (const field of addressFields) {
        const value = str(body[field]);
        if (!value || !profile.isAddress(value)) {
          invalidAddress(res, profile, field, value);
          return;
        }
      }
      const id = str(body.blockchainTransactionId) ?? str(body.coldTransferId) ?? 'transfer';
      res.json({ txHash: profile.txHash(id) });
    });
  }

  router.get('/api/transfer/status/:txHash', (req, res) => {
    const { txHash } = req.params;
    if (!profile.isTxHash(txHash)) {
      res.status(400).json({
        error: 'INVALID_TX_HASH',
        message: `${txHash} is not a ${profile.network} transaction hash.`,
      });
      return;
    }
    // Immediate finality so the confirmation job settles the transfer on its
    // first poll — in this network's own terms (TRON: >= 20 blocks; Ethereum:
    // `finalized: true`; its block count is informational).
    res.json(profile.finalTransferStatus(txHash));
  });

  router.post('/api/transfer/estimate-fee', (req, res) => {
    const body = (req.body ?? {}) as Record<string, unknown>;
    const toAddress = str(body.toAddress);
    const fromAddress = body.fromAddress === undefined ? undefined : str(body.fromAddress);
    if (!toAddress || !profile.isAddress(toAddress)) {
      invalidAddress(res, profile, 'toAddress', toAddress);
      return;
    }
    if (fromAddress !== undefined && (!fromAddress || !profile.isAddress(fromAddress))) {
      invalidAddress(res, profile, 'fromAddress', fromAddress);
      return;
    }
    if (body.token !== 'USDT' && body.token !== 'USDC') {
      res
        .status(400)
        .json({ error: 'INVALID_ESTIMATE_REQUEST', message: 'token must be USDT or USDC.' });
      return;
    }
    const fees = getFees(profile.network);
    if (!fees.available) {
      estimateUnavailable(res);
      return;
    }
    if (profile.network === 'ETHEREUM') {
      // T154 shape (sidecar-ethereum/contracts/estimate-fee/response.json):
      // every integer a decimal string; feeUsdt is exactly the rounded-up
      // product the backend re-derives (WeiAmount.TryUsdtCentsCeil).
      const fromDeposit = fromAddress !== undefined;
      res.json({
        network: 'ETHEREUM',
        feeUsdt: weiToUsdtCentsCeil(BigInt(fees.ethFeeWei), fees.ethPriceUsdt),
        feeWei: fees.ethFeeWei,
        sender: fromDeposit ? 'DEPOSIT' : 'HOT_WALLET',
        blockNumber: String(profile.blockNumber),
        gasLimit: '46201',
        fuelGasLimit: fromDeposit ? '21000' : '0',
        baseFeePerGasWei: '167000000',
        maxPriorityFeePerGasWei: '50000000',
        maxFeePerGasWei: '384000000',
        ethPriceUsdt: fees.ethPriceUsdt,
        priceSource: 'fake',
        priceAgeSeconds: 0,
      });
      return;
    }
    const burnSun = Math.round((Number(fees.tronFeeUsdt) / fees.tronTrxPriceUsdt) * 1e6);
    res.json({
      feeUsdt: fees.tronFeeUsdt,
      energyRequired: 64_285,
      energyAvailable: 0,
      energyShortfall: 64_285,
      bandwidthRequired: 345,
      bandwidthAvailable: 0,
      burnSun,
      trxPriceUsdt: fees.tronTrxPriceUsdt,
      priceSource: 'fake',
    });
  });

  router.post('/api/transfer/estimate-sweep-fee', (req, res) => {
    if (profile.network === 'ETHEREUM') {
      // Ethereum has no buyer-paid fee line (K8: the platform carries
      // Ethereum's fee), and the real service has no such endpoint — it
      // answers 501 like the T150 skeleton's unimplemented routes.
      res.status(501).json({
        error: 'NOT_IMPLEMENTED',
        message: 'POST /api/transfer/estimate-sweep-fee is a TRON endpoint.',
      });
      return;
    }
    const token = (req.body as { token?: unknown })?.token;
    if (token !== 'USDT' && token !== 'USDC') {
      res
        .status(400)
        .json({ error: 'INVALID_ESTIMATE_REQUEST', message: 'token must be USDT or USDC.' });
      return;
    }
    const fees = getFees('TRON');
    if (!fees.available) {
      estimateUnavailable(res);
      return;
    }
    // The 08 §3.1a breakdown: activation + burn top-up + bandwidth = total.
    const activationSun = 1_100_000;
    const bandwidthSun = 270_000;
    const burnTopUpSun = Math.max(fees.tronSweepCostSun - activationSun - bandwidthSun, 0);
    res.json({
      sweepCostSun: fees.tronSweepCostSun,
      activationSun,
      burnTopUpSun,
      bandwidthSun,
      energyRequired: 64_285,
      trxPriceUsdt: fees.tronTrxPriceUsdt,
      priceSource: 'fake',
      costUsdt: ((fees.tronSweepCostSun / 1e6) * fees.tronTrxPriceUsdt).toFixed(6),
    });
  });

  return router;
}

function str(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function invalidAddress(
  res: Response,
  profile: NetworkProfile,
  field: string,
  value: string | undefined,
): void {
  res.status(400).json({
    error: 'INVALID_ADDRESS',
    message: `${field} ${JSON.stringify(value ?? null)} is not a ${profile.network} address.`,
  });
}

function estimateUnavailable(res: Response): void {
  res.status(502).json({
    error: 'FEE_ESTIMATE_UNAVAILABLE',
    message: 'Fee estimate switched off on the fake (POST /__e2e/blockchain/fees).',
  });
}

/**
 * 400 `NETWORK_MISMATCH` for a POST that names another network — or, on
 * Ethereum, none (sidecar-ethereum api/middleware.ts `networkGuard`; the TRON
 * service lets an absent field through, sidecar-blockchain networkGuard).
 */
function networkGuard(profile: NetworkProfile) {
  return (req: Request, res: Response, next: NextFunction): void => {
    if (req.method !== 'POST') {
      next();
      return;
    }
    const claimed = (req.body as { network?: unknown } | undefined)?.network;
    const ok =
      claimed === profile.network || (claimed === undefined && !profile.networkFieldRequired);
    if (!ok) {
      res.status(400).json({
        error: 'NETWORK_MISMATCH',
        message: `This is the ${profile.network} service; the request names ${JSON.stringify(claimed ?? null)}.`,
      });
      return;
    }
    next();
  };
}

/** Journal every call on this surface with the status it was answered with. */
function journalCalls(profile: NetworkProfile) {
  return (req: Request, res: Response, next: NextFunction): void => {
    const path = req.originalUrl.split('?')[0];
    res.on('finish', () => {
      recordCall({
        network: profile.network,
        method: req.method,
        path,
        body: req.body ?? null,
        status: res.statusCode,
      });
    });
    next();
  };
}
