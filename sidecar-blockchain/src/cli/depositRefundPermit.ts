import TronWeb from 'tronweb';
import { logger } from '../logger.js';
import {
  DepositRefundPermitStore,
  MAX_PERMIT_VALIDITY_MS,
} from '../transfer/DepositRefundPermits.js';
import { runPermitCli } from './permitCli.js';

/**
 * T172b — the operator's one-time permit past the TRON signer's 24-hour
 * deposit refund cap (proje sahibi kararı 2026-10-23, modal). Runs ONLY on the
 * server, inside the service's container; there is deliberately no HTTP route:
 *
 *   docker exec skinora-blockchain-sidecar node dist/cli/depositRefundPermit.js \
 *     create --tx <BlockchainTransaction.Id> --to <buyer address> --amount <decimal> \
 *     --by <operator> --reason "<why>" [--hours 24]
 *   … list
 *   … revoke --id <permit id>
 *
 * Permits are written to the deposit refund ledger's volume
 * (DEPOSIT_REFUND_LEDGER_DIR/deposit-refund-permits). DEPLOY_RUNBOOK §D.
 */
const baseDir = process.env.DEPOSIT_REFUND_LEDGER_DIR || '';

const code = runPermitCli(
  process.argv.slice(2),
  () => {
    if (!baseDir) throw new Error('DEPOSIT_REFUND_LEDGER_DIR is not set in this container.');
    return new DepositRefundPermitStore({
      baseDir,
      network: 'TRON',
      isValidAddress: (address) => TronWeb.isAddress(address),
    });
  },
  MAX_PERMIT_VALIDITY_MS,
);

logger.flush();
// Let the log transport ship the audit line before the process ends.
setTimeout(() => process.exit(code), 1500);
