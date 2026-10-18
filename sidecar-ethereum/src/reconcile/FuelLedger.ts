import { canonicalAddress } from '../address/canonicalAddress.js';

/**
 * Fuel (gas top-up) ETH this service sent to its own deposits (T151 —
 * 08 §3.7.4 "Yerel ETH", §3.7.5). The fuel wallet is the only expected source
 * of ETH on a deposit; the reconciliation counts an ETH increase as fuel only
 * up to what was recorded here for the same blocks, and alarms on the rest.
 *
 * <para>
 * <b>Contract for the fuel sender (T153):</b> call {@link record} once per
 * fuel transfer, with the block it was included in, as soon as the receipt is
 * known. Until T153 nothing records fuel, so every ETH arrival on a deposit
 * alarms — which is right: no fuel is sent before T153.
 * </para>
 *
 * <para>
 * In memory only. A record lost to a restart cannot cause a missed alarm
 * (only a false one): after a restart each deposit's first reading is a fresh
 * baseline, so ETH already on a deposit is not compared against fuel at all.
 * </para>
 */
export class FuelLedger {
  private readonly byAddress = new Map<string, Array<{ block: bigint; wei: bigint }>>();

  record(address: string, wei: bigint, block: bigint): void {
    if (wei <= 0n) return;
    const key = canonicalAddress(address);
    const list = this.byAddress.get(key) ?? [];
    list.push({ block, wei });
    this.byAddress.set(key, list);
  }

  /** Fuel recorded for <c>address</c> in blocks (afterBlock, throughBlock]. */
  sentBetween(address: string, afterBlock: bigint, throughBlock: bigint): bigint {
    let total = 0n;
    for (const f of this.byAddress.get(canonicalAddress(address)) ?? []) {
      if (f.block > afterBlock && f.block <= throughBlock) total += f.wei;
    }
    return total;
  }

  /** Drops records at or below <c>block</c> (already reconciled). */
  prune(address: string, block: bigint): void {
    const key = canonicalAddress(address);
    const kept = (this.byAddress.get(key) ?? []).filter((f) => f.block > block);
    if (kept.length === 0) this.byAddress.delete(key);
    else this.byAddress.set(key, kept);
  }
}
