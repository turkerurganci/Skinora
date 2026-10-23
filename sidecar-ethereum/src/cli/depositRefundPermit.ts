import { checksummedEvmAddress } from '../address/evmAddress.js';
import { DEFAULT_TX_JOURNAL_DIR } from '../config/index.js';
import { logger } from '../logger.js';
import {
  DepositRefundPermitStore,
  MAX_PERMIT_VALIDITY_MS,
} from '../transfer/DepositRefundPermits.js';
import { runPermitCli } from './permitCli.js';

/**
 * T172b — the operator's one-time permit past the Ethereum signer's 24-hour
 * deposit refund cap (proje sahibi kararı 2026-10-23, modal). Runs ONLY on the
 * server, inside the service's container; there is deliberately no HTTP route:
 *
 *   docker exec skinora-ethereum-sidecar node dist/cli/depositRefundPermit.js \
 *     create --tx <BlockchainTransaction.Id> --to <buyer 0x address> --amount <decimal> \
 *     --by <operator> --reason "<why>" [--hours 24]
 *   … list
 *   … revoke --id <permit id>
 *
 * Permits are written to the transaction journal's volume
 * (ETHEREUM_TX_JOURNAL_DIR/deposit-refund-permits). DEPLOY_RUNBOOK §D.
 */
const baseDir = process.env.ETHEREUM_TX_JOURNAL_DIR || DEFAULT_TX_JOURNAL_DIR;

const code = runPermitCli(
  process.argv.slice(2),
  () =>
    new DepositRefundPermitStore({
      baseDir,
      network: 'ETHEREUM',
      isValidAddress: (address) => checksummedEvmAddress(address) !== null,
    }),
  MAX_PERMIT_VALIDITY_MS,
);

logger.flush();
// Let the log transport ship the audit line before the process ends.
setTimeout(() => process.exit(code), 1500);
