import { Router } from 'express';
import type { Request, Response } from 'express';
import { config } from '../config.js';
import { fakeTronAddress, fakeTxHash } from '../ids.js';

export const blockchainRouter = Router();

// Plausible solid-block height the fake reports for derived addresses and
// confirmed transfers. Constant — the backend only checks confirmations >= 20.
const BASE_BLOCK = 60_000_000;

interface DeriveBody {
  index?: number;
}

blockchainRouter.post('/api/wallet/derive', (req, res) => {
  const index = Number((req.body as DeriveBody)?.index ?? 0);
  res.json({
    address: fakeTronAddress(index),
    derivationPath: `m/44'/195'/0'/0/${index}`,
    index,
  });
});

interface BalancesBody {
  addresses?: string[];
}

blockchainRouter.post('/api/wallet/balances', (req, res) => {
  const addresses = Array.isArray((req.body as BalancesBody)?.addresses)
    ? (req.body as BalancesBody).addresses!
    : [];
  res.json({
    blockNumber: BASE_BLOCK,
    balances: addresses.map((address) => ({ address, tokens: {} })),
  });
});

blockchainRouter.post('/api/monitor/post-cancel-start', (_req, res) => {
  res.json({ status: 'ok' });
});

blockchainRouter.post('/api/monitor/post-cancel-stop', (_req, res) => {
  res.json({ status: 'ok' });
});

interface TransferBody {
  blockchainTransactionId?: string;
  coldTransferId?: string;
}

// payout / refund / sweep / cold-wallet all share { txHash } success shape.
function transfer(req: Request, res: Response): void {
  const body = (req.body ?? {}) as TransferBody;
  const id = body.blockchainTransactionId ?? body.coldTransferId ?? 'transfer';
  res.json({ txHash: fakeTxHash(String(id)) });
}

blockchainRouter.post('/api/transfer/payout', transfer);
blockchainRouter.post('/api/transfer/refund', transfer);
blockchainRouter.post('/api/transfer/sweep', transfer);
blockchainRouter.post('/api/transfer/cold-wallet', transfer);

blockchainRouter.get('/api/transfer/status/:txHash', (req, res) => {
  // Immediate finality so the confirmation job completes the payout on the
  // first poll (>= 20 confirmations + SUCCESS = confirmed).
  res.json({
    txHash: req.params.txHash,
    blockNumber: BASE_BLOCK,
    contractRet: 'SUCCESS',
    confirmations: config.transferConfirmations,
  });
});
