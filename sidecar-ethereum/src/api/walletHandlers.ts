import type { Request, Response } from 'express';
import { SidecarError } from '../errors/SidecarError.js';
import { InvalidDerivationIndexError } from '../wallet/hdRoot.js';
import type { EthereumHdWallet } from '../wallet/HdWalletService.js';

/**
 * `POST /api/wallet/derive { network, index, transactionId? }` →
 * `200 { address, derivationPath, index }` (05 §3.3a — same contract as the
 * TRON service). The backend owns index allocation (`(Network, HdWalletIndex)`
 * unique, 06 §3.7); this is a pure derivation. `address` is EIP-55; the
 * backend stores it lowercase (06 §1.4).
 */
export function deriveAddressHandler(wallet: EthereumHdWallet) {
  return (req: Request, res: Response): void => {
    const body = (req.body ?? {}) as { index?: unknown; transactionId?: unknown };
    const index = body.index;
    const transactionId = typeof body.transactionId === 'string' ? body.transactionId : undefined;

    if (typeof index !== 'number' || !Number.isInteger(index) || index < 0) {
      res.status(400).json({
        error: 'INVALID_DERIVATION_INDEX',
        message: 'Field "index" must be a non-negative integer.',
      });
      return;
    }

    try {
      const result = wallet.derive(index);
      req.log.info(
        { index, transactionId, derivationPath: result.derivationPath },
        'Ethereum deposit address derived',
      );
      res.status(200).json(result);
    } catch (err) {
      if (err instanceof InvalidDerivationIndexError) {
        res.status(400).json({ error: err.code, message: err.message });
        return;
      }
      const code = err instanceof SidecarError ? err.code : 'INTERNAL_ERROR';
      req.log.error({ code, index }, 'Ethereum derive failed');
      res.status(500).json({ error: code, message: 'Derivation failed.' });
    }
  };
}
