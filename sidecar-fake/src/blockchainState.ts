import type { Network } from './networks.js';

/**
 * T155 — in-memory state of the fake's two blockchain surfaces. Three things a
 * test needs to see that the backend's own database cannot show it:
 *
 *   • the CALL JOURNAL — every backend → service call, per network. "The payout
 *     left from the network the buyer paid on" (11 §F8 K3) is a claim about
 *     which SERVICE the backend talked to, and only this side of the seam saw
 *     that;
 *   • the MONITOR REGISTRY — which deposits are being watched, per network, with
 *     the token contract the backend armed them for;
 *   • the FEE KNOBS — what each surface's estimate endpoints answer, so a test
 *     can prove a fee came from the estimate (not the backend's fallback) and
 *     can turn an estimate off to watch the fallback.
 *
 * Nothing here persists; `resetBlockchainState()` returns all three to their
 * defaults between scenarios.
 */

export interface JournalEntry {
  seq: number;
  network: Network;
  method: string;
  path: string;
  body: unknown;
  status: number;
  at: string;
}

const JOURNAL_LIMIT = 5_000;
let journal: JournalEntry[] = [];
let seq = 0;

export function recordCall(entry: Omit<JournalEntry, 'seq' | 'at'>): void {
  seq += 1;
  journal.push({ ...entry, seq, at: new Date().toISOString() });
  if (journal.length > JOURNAL_LIMIT) journal = journal.slice(-JOURNAL_LIMIT);
}

export interface JournalQuery {
  network?: Network;
  /** Path prefix — `/api/transfer/status` matches every status read. */
  path?: string;
  sinceSeq?: number;
}

export function queryJournal(query: JournalQuery = {}): JournalEntry[] {
  return journal.filter(
    (e) =>
      (query.network === undefined || e.network === query.network) &&
      (query.path === undefined || e.path.startsWith(query.path)) &&
      (query.sinceSeq === undefined || e.seq > query.sinceSeq),
  );
}

export interface MonitorEntry {
  network: Network;
  address: string;
  paymentAddressId: string;
  transactionId: string;
  expectedContract: string;
  expectedSymbol: string;
  cadence: string;
  startedAt: string;
}

// Keyed `${network}|${canonical address}`.
const monitors = new Map<string, MonitorEntry>();

function monitorKey(network: Network, address: string): string {
  return `${network}|${network === 'ETHEREUM' ? address.toLowerCase() : address}`;
}

/** Returns true when the address was not monitored before (the real `started`). */
export function startMonitor(entry: Omit<MonitorEntry, 'startedAt'>): boolean {
  const key = monitorKey(entry.network, entry.address);
  const fresh = !monitors.has(key);
  monitors.set(key, { ...entry, startedAt: new Date().toISOString() });
  return fresh;
}

/** Returns true when a monitor was removed. */
export function stopMonitor(network: Network, address: string): boolean {
  return monitors.delete(monitorKey(network, address));
}

export function listMonitors(network?: Network): MonitorEntry[] {
  return [...monitors.values()].filter((m) => network === undefined || m.network === network);
}

/**
 * What the estimate endpoints answer. `available: false` makes both answer
 * 502 `FEE_ESTIMATE_UNAVAILABLE` — the shape the real services use for a
 * failed chain read — so the backend takes its fallback.
 */
export interface FeeKnobs {
  available: boolean;
  /** TRON `estimate-fee` — USDT, 2 decimals. */
  tronFeeUsdt: string;
  /** TRON `estimate-sweep-fee` — SUN and TRX price (T149). */
  tronSweepCostSun: number;
  tronTrxPriceUsdt: number;
  /** Ethereum `estimate-fee` — wei (decimal string) and ETH price (T154). */
  ethFeeWei: string;
  ethPriceUsdt: string;
}

/**
 * Defaults, each chosen for a reason a test can lean on:
 *
 *   • TRON estimate-fee 2.00 — equal to the backend's TRON refund fallback
 *     (`blockchain.refund_gas_fee_estimate_usdt`), so the T110 suites' refund
 *     arithmetic ("net 10 − 2 gas ≥ 4 threshold") reads the same whether the
 *     estimate or the fallback answered. ~2 USDT is also what a mainnet TRC-20
 *     transfer burns.
 *   • TRON sweep 8,441,349 SUN at 0.34 — the 02 §5 / T149 worked example:
 *     8.441349 TRX × 0.34 × 1.007 buffer = 2.8901 → a 2.90 network fee line,
 *     which is NOT the 3.00 fallback — so an e2e that reads 2.90 has proven the
 *     estimate path ran.
 *   • Ethereum 2·10^13 wei at 2414.579991 — 0.0483 USDT, rounded up to 0.05
 *     (≈ the T142 measured per-transfer cost; under the 0.50 fallback, so the
 *     two are told apart).
 */
const DEFAULT_FEES: FeeKnobs = {
  available: true,
  tronFeeUsdt: '2.00',
  tronSweepCostSun: 8_441_349,
  tronTrxPriceUsdt: 0.34,
  ethFeeWei: '20000000000000',
  ethPriceUsdt: '2414.579991',
};

const fees: Record<Network, FeeKnobs> = {
  TRON: { ...DEFAULT_FEES },
  ETHEREUM: { ...DEFAULT_FEES },
};

export function getFees(network: Network): FeeKnobs {
  return fees[network];
}

export function setFees(network: Network, patch: Partial<FeeKnobs>): FeeKnobs {
  fees[network] = { ...fees[network], ...patch };
  return fees[network];
}

export function resetBlockchainState(): void {
  // `seq` keeps counting: a test holding a `sinceSeq` from before the reset
  // must not see the next scenario's calls renumbered under it.
  journal = [];
  monitors.clear();
  fees.TRON = { ...DEFAULT_FEES };
  fees.ETHEREUM = { ...DEFAULT_FEES };
}
