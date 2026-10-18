import { canonicalAddress } from '../address/canonicalAddress.js';

/**
 * Fuel (gas top-up) ETH this service sent to its own deposits (T151 —
 * 08 §3.7.4 "Yerel ETH", §3.7.5). The fuel wallet is the only expected source
 * of ETH on a deposit; the reconciliation counts an ETH increase as fuel only
 * up to what was recorded here for the same blocks, and alarms on the rest.
 *
 * <para>
 * <b>Fed by the fuel sender (T153):</b> the transfer service calls
 * {@link record} with the fuel transaction's hash and block whenever it sees
 * the fuel in a block (the keeper's pass, the deposit send that waits for
 * it), and at startup for every journalled fuel already in a block
 * (wiring.ts). The hash makes the call idempotent; a later call with another
 * block (a re-org moved the fuel) replaces the block.
 * </para>
 *
 * <para>
 * In memory, rebuilt from the journal at startup — so a restart does not
 * turn a fuel that lands after the deposit's first reading into a false
 * alarm. A fuel without a hash (tests) is kept as its own entry.
 * </para>
 */
export class FuelLedger {
  private readonly byAddress = new Map<
    string,
    Array<{ block: bigint; wei: bigint; hash: string | null }>
  >();

  record(address: string, wei: bigint, block: bigint, hash: string | null = null): void {
    if (wei <= 0n) return;
    const key = canonicalAddress(address);
    const list = this.byAddress.get(key) ?? [];
    const id = hash === null ? null : hash.toLowerCase();
    const existing = id === null ? undefined : list.find((f) => f.hash === id);
    if (existing) {
      existing.block = block;
      existing.wei = wei;
    } else {
      list.push({ block, wei, hash: id });
    }
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
