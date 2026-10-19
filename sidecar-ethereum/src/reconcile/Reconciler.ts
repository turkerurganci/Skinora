import { logger } from '../logger.js';
import {
  nativeEthReceivedTotal,
  reconciliationDurationSeconds,
  reconciliationIntervalSeconds,
  reconciliationLastSuccess,
  reconciliationMismatchTotal,
  reconciliationSkippedTotal,
} from '../metrics.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import {
  contiguousThrough,
  type DepositRegistry,
  type WatchEntry,
} from '../monitor/DepositRegistry.js';
import type { AddressBalances, BalanceReader } from './BalanceReader.js';
import type { FuelLedger } from './FuelLedger.js';

/**
 * Hourly balance reconciliation of the watched deposits (T151 — 08 §3.7.4
 * "Bakiye mutabakatı", "Yerel ETH", §3.7.8; 05 §3.3 "Reconciliation" rule's
 * Ethereum counterpart).
 *
 * <para>
 * <b>What is compared.</b> Between two readings of the same deposit, its
 * token balance must have moved by exactly the transfers the payment monitor
 * saw in and out of it in those blocks. A difference means the log path
 * missed a transfer — a provider that answered `eth_getLogs` incompletely
 * without an error (measured on Tenderly, T142) — and is an admin alarm
 * (`ethereum-deposit-balance-mismatch`). A deposit the monitor knows was
 * empty when it started (a fresh PAYMENT deposit) is compared from zero, so a
 * missed payment is caught even before its first reading.
 * </para>
 *
 * <para>
 * <b>Native ETH.</b> An ETH payment leaves no Transfer log. ETH on a deposit
 * is expected only as fuel (08 §3.7.5); an increase beyond the fuel recorded
 * for those blocks ({@link FuelLedger}) is an admin alarm
 * (`ethereum-deposit-native-eth`). There is no automatic refund. Decreases
 * are gas spent by the deposit's own sweep / refund.
 * </para>
 *
 * <para>
 * <b>Where it reads.</b> At one block for the whole round: the finalized head,
 * or the monitor's cursor if that is behind (the flows must be complete up to
 * the block). Deposits still being read back — including a deposit whose
 * history lane has not reached its live lane yet — are left for a later round.
 * </para>
 *
 * <para>
 * <b>Never silent.</b> The reads go through the {@link RatePacer} (Alchemy
 * Free: 300 CU/s — a round over ~300 deposits takes at least ~46 s at the
 * full limit; the default budget of 200 CU/s takes longer and leaves the
 * scanner room). If the finalized head is older than
 * `maxFinalizedAgeSeconds` (25 min: past ~128 blocks state reads need archive
 * access — 2 RU on Chainstack, impossible on PublicNode) the round is skipped
 * with a warning and a counter; a read that fails (an archive refusal fails
 * over and then fails) skips it the same way. The `ethereum-reconciliation-
 * stale` alarm fires when no round has completed for 2.5 intervals.
 * </para>
 */

export interface ReconcilerDeps {
  registry: DepositRegistry;
  finality: () => FinalityStatus;
  scannedBlock: () => bigint | null;
  reader: Pick<BalanceReader, 'read' | 'configuredSymbols'>;
  fuel: FuelLedger;
  intervalMs: number;
  maxFinalizedAgeSeconds: number;
  /** Delay before the first round after start (baselines early). */
  firstRunDelayMs?: number;
  now?: () => number;
}

export type ReconcileOutcome =
  | { kind: 'completed'; block: bigint; deposits: number; mismatches: number; nativeEth: number }
  | { kind: 'skipped'; reason: 'no_finalized' | 'finalized_stale' | 'read_failed' };

export class Reconciler {
  private timer?: NodeJS.Timeout;
  private running = false;
  private readonly now: () => number;

  constructor(private readonly deps: ReconcilerDeps) {
    this.now = deps.now ?? (() => Date.now());
    reconciliationIntervalSeconds.set(deps.intervalMs / 1000);
    // "Fresh" at start: the stale alarm counts from process start until the first round.
    reconciliationLastSuccess.set(Math.floor(this.now() / 1000));
  }

  start(): void {
    const schedule = (delay: number) => {
      this.timer = setTimeout(() => {
        void this.runOnce().finally(() => schedule(this.deps.intervalMs));
      }, delay);
      this.timer.unref();
    };
    schedule(this.deps.firstRunDelayMs ?? 120_000);
  }

  stop(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = undefined;
  }

  async runOnce(): Promise<ReconcileOutcome> {
    if (this.running) return { kind: 'skipped', reason: 'read_failed' };
    this.running = true;
    try {
      return await this.round();
    } finally {
      this.running = false;
    }
  }

  private skip(reason: 'no_finalized' | 'finalized_stale' | 'read_failed'): ReconcileOutcome {
    reconciliationSkippedTotal.inc({ reason });
    return { kind: 'skipped', reason };
  }

  private async round(): Promise<ReconcileOutcome> {
    const status = this.deps.finality();
    const cursor = this.deps.scannedBlock();
    if (!status.snapshot || cursor === null) return this.skip('no_finalized');
    if (status.ageSeconds !== null && status.ageSeconds > this.deps.maxFinalizedAgeSeconds) {
      logger.warn(
        {
          finalizedAgeSeconds: status.ageSeconds,
          maxFinalizedAgeSeconds: this.deps.maxFinalizedAgeSeconds,
        },
        'Ethereum reconciliation skipped — finalized head is too old: reading state that deep may need archive access (08 §3.7.4)',
      );
      return this.skip('finalized_stale');
    }
    const block = status.snapshot.number < cursor ? status.snapshot.number : cursor;
    // Only deposits read without a gap up to the block: one whose history
    // lane (T158 düzeltme turu) is still running has flows missing between
    // its start block and the live lane — comparing it would be a false alarm.
    const entries = this.deps.registry.entries().filter((e) => {
      const through = contiguousThrough(e.scan);
      return through !== null && through >= block;
    });

    const started = this.now();
    const readings: Array<[WatchEntry, AddressBalances]> = [];
    try {
      for (const entry of entries) {
        readings.push([entry, await this.deps.reader.read(entry.address, block)]);
      }
    } catch (err) {
      logger.error(
        { block: String(block), reason: (err as Error).message },
        'Ethereum reconciliation skipped — a balance read failed (an archive refusal looks like this)',
      );
      return this.skip('read_failed');
    }

    let mismatches = 0;
    let nativeEth = 0;
    for (const [entry, balances] of readings) {
      const result = this.compare(entry, balances, block);
      mismatches += result.mismatches;
      nativeEth += result.nativeEth;
    }
    const seconds = (this.now() - started) / 1000;
    reconciliationDurationSeconds.set(seconds);
    reconciliationLastSuccess.set(Math.floor(this.now() / 1000));
    logger.info(
      { block: String(block), deposits: entries.length, mismatches, nativeEth, seconds },
      'Ethereum reconciliation round completed',
    );
    return { kind: 'completed', block, deposits: entries.length, mismatches, nativeEth };
  }

  /** Compares one reading with the previous one and the transfers seen in between. */
  private compare(
    entry: WatchEntry,
    balances: AddressBalances,
    block: bigint,
  ): { mismatches: number; nativeEth: number } {
    const scan = entry.scan;
    const base = scan.baseline;
    let mismatches = 0;
    let nativeEth = 0;
    if (base !== null && block > base.block) {
      for (const symbol of this.deps.reader.configuredSymbols()) {
        const moved = scan.flows
          .filter((f) => f.symbol === symbol && f.block > base.block && f.block <= block)
          .reduce((sum, f) => sum + f.delta, 0n);
        const expected = (base.tokens[symbol] ?? 0n) + moved;
        const actual = balances.tokens[symbol] ?? 0n;
        if (actual !== expected) {
          mismatches++;
          reconciliationMismatchTotal.inc({ asset: symbol });
          logger.error(
            {
              address: entry.address,
              transactionId: entry.transactionId,
              asset: symbol,
              expected: expected.toString(),
              actual: actual.toString(),
              fromBlock: String(base.block),
              toBlock: String(block),
            },
            'Ethereum deposit balance does not match the transfers the monitor saw — a transfer was missed (admin alarm)',
          );
        }
      }
      const fuel = this.deps.fuel.sentBetween(entry.address, base.block, block);
      if (balances.ETH > base.eth + fuel) {
        nativeEth++;
        nativeEthReceivedTotal.inc();
        logger.error(
          {
            address: entry.address,
            transactionId: entry.transactionId,
            receivedWei: (balances.ETH - base.eth - fuel).toString(),
            fromBlock: String(base.block),
            toBlock: String(block),
          },
          'Native ETH reached an Ethereum deposit and is not fuel — no automatic refund (admin alarm)',
        );
      }
    }
    if (base === null || block > base.block) {
      scan.baseline = { block, eth: balances.ETH, tokens: { ...balances.tokens } };
      scan.flows = scan.flows.filter((f) => f.block > block);
      this.deps.fuel.prune(entry.address, block);
    }
    return { mismatches, nativeEth };
  }
}
