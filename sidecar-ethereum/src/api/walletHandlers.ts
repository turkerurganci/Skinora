import type { Request, Response } from 'express';
import { SidecarError } from '../errors/SidecarError.js';
import { InvalidDerivationIndexError } from '../wallet/hdRoot.js';
import type { EthereumHdWallet } from '../wallet/HdWalletService.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';

/**
 * `POST /api/wallet/derive { network, index, transactionId? }` →
 * `200 { address, derivationPath, index, watchFromBlock }` (05 §3.3a — the
 * TRON service's contract plus `watchFromBlock`). The backend owns index
 * allocation (`(Network, HdWalletIndex)` unique, 06 §3.7); this is a pure
 * derivation. `address` is EIP-55; the backend stores it lowercase (06 §1.4).
 *
 * <para>
 * T158 (h) — `watchFromBlock` (decimal string) is the last `finalized` block
 * the service read. The address is new — never shown to anyone before this
 * answer — so nothing can reach it at or before that block; the backend
 * persists it (`PaymentAddress.MonitorFromBlock`) and sends it with every
 * monitor start, and the monitor reads the deposit from the block after it
 * with a zero baseline, however long the service is down. Without a
 * finalized head (the service has not read one yet) the answer is 502
 * `FINALIZED_HEAD_UNAVAILABLE` and the backend's allocator retries — an
 * address without a start block would fall back to the bounded restart
 * read-back.
 * </para>
 */
export function deriveAddressHandler(wallet: EthereumHdWallet, finality: () => FinalityStatus) {
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

    const head = finality().snapshot;
    if (!head) {
      req.log.warn(
        { index, transactionId },
        'Ethereum derive refused — no finalized head read yet',
      );
      res.status(502).json({
        error: 'FINALIZED_HEAD_UNAVAILABLE',
        message:
          'No finalized block has been read yet — the start block of the deposit is unknown; try again.',
        retryable: true,
      });
      return;
    }

    try {
      const result = wallet.derive(index);
      const watchFromBlock = head.number.toString();
      req.log.info(
        { index, transactionId, derivationPath: result.derivationPath, watchFromBlock },
        'Ethereum deposit address derived',
      );
      res.status(200).json({ ...result, watchFromBlock });
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
