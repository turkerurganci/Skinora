/**
 * Pure decision for a deposit-sourced transfer (sweep, refund): does the
 * sweeper delegate Energy for it, or does the deposit burn TRX?
 *
 * <para>
 * Owner decision 2026-09-16 (EnergyPerTrxAssumptionUnverified): HYBRID — the
 * hot wallet stakes for the baseline volume, anything beyond burns. Shared by
 * <c>EnergyDelegationService</c> (what the broadcast does) and
 * <c>FeeEstimationService</c> (what the user is charged on a refund), so the
 * estimate and the broadcast cannot disagree about which path runs.
 * </para>
 *
 * <para>
 * ALL-OR-NOTHING, deliberately. A delegation that covers only part of the
 * transfer leaves the rest to be burned from the deposit's own TRX, and a
 * deposit holds none — the transfer then fails for lack of Energy while the
 * delegation step reported success, so no fallback ever runs. That is the
 * exact failure the previous fixed 200 TRX delegation produced on mainnet
 * (200 TRX × ~9.5 Energy/TRX ≈ 1,900 Energy against a 64,285 Energy
 * transfer). Either the stake covers the whole shortfall, or nothing is
 * delegated and the burn path funds the whole of it.
 * </para>
 */

/**
 * Head-room on the delegated amount. The Energy a stake produces is
 * TotalEnergyLimit / TotalEnergyWeight at the moment the chain executes, which
 * moves with the network's total stake between planning and the block; 10%
 * absorbs that drift and the chain's whole-unit rounding.
 */
export const DELEGATION_MARGIN = 1.1;

/** Stake 2.0 rejects a delegation below 1 TRX. */
export const MIN_DELEGATION_SUN = 1_000_000;

/** Head-room on a burn top-up — the same price drift, from the other side. */
export const BURN_MARGIN = 1.1;

/**
 * Free daily Bandwidth of an ACTIVATED account. A never-activated deposit
 * reports 0, but the flow activates it before the transfer, so the transfer
 * sees this allowance (measured on Nile 2026-09-16: `freeNetLimit` 600 right
 * after activation, and a TRC-20 call consumed 345 bytes of it).
 */
export const ACTIVATED_ACCOUNT_FREE_BANDWIDTH = 600;

/** Typical size of a signed TRC-20 `triggersmartcontract` transaction (measured 345 bytes on Nile). */
export const ESTIMATED_TRANSFER_TX_BYTES = 350;

export interface DelegationPlanInput {
  /** Energy the transfer consumes (simulation of the exact call). */
  energyRequired: number;
  /** Share of it the caller pays, 0-100 (`consume_user_resource_percent`). */
  callerPercent: number;
  /** Energy the deposit already has. */
  senderEnergyAvailable: number;
  /** Network Energy per staked TRX; null when the node omitted it. */
  energyPerTrx: number | null;
  /** SUN the sweeper can delegate as ENERGY right now. */
  delegatableSun: number;
}

export type DelegationPlan =
  | { kind: 'no-energy-needed'; callerEnergy: number; energyShortfall: 0 }
  | { kind: 'delegate'; callerEnergy: number; energyShortfall: number; delegationSun: number }
  | {
      kind: 'burn';
      callerEnergy: number;
      energyShortfall: number;
      reason: 'no-ratio' | 'insufficient-stake';
    };

export function planDelegation(input: DelegationPlanInput): DelegationPlan {
  const callerPercent = Math.min(100, Math.max(0, input.callerPercent));
  const callerEnergy = Math.ceil((input.energyRequired * callerPercent) / 100);
  const energyShortfall = Math.max(0, callerEnergy - Math.max(0, input.senderEnergyAvailable));

  if (energyShortfall === 0) {
    return { kind: 'no-energy-needed', callerEnergy, energyShortfall: 0 };
  }

  if (input.energyPerTrx === null || !(input.energyPerTrx > 0)) {
    return { kind: 'burn', callerEnergy, energyShortfall, reason: 'no-ratio' };
  }

  const trx = Math.ceil((energyShortfall / input.energyPerTrx) * DELEGATION_MARGIN);
  const delegationSun = Math.max(MIN_DELEGATION_SUN, trx * 1_000_000);

  if (input.delegatableSun >= delegationSun) {
    return { kind: 'delegate', callerEnergy, energyShortfall, delegationSun };
  }
  return { kind: 'burn', callerEnergy, energyShortfall, reason: 'insufficient-stake' };
}

export interface BurnInput {
  /** Energy that will be burned (the whole shortfall on the burn path, 0 otherwise). */
  energyToBurn: number;
  energyFeeSun: number;
  bandwidthAvailable: number;
  txBytes: number;
  bandwidthFeeSun: number;
}

/**
 * SUN the transfer will burn. Bandwidth is all-or-nothing on TRON: an account
 * short of the full byte count burns the WHOLE transaction's bytes.
 */
export function burnSun(input: BurnInput): number {
  const bandwidthBurn =
    input.bandwidthAvailable >= input.txBytes ? 0 : input.txBytes * input.bandwidthFeeSun;
  return input.energyToBurn * input.energyFeeSun + bandwidthBurn;
}

/**
 * TRX to send to the deposit so it can pay <paramref name="requiredBurnSun"/>,
 * given what it already holds. 0 when the balance already covers it.
 */
export function topUpSun(requiredBurnSun: number, balanceSun: number): number {
  if (requiredBurnSun <= 0) return 0;
  const withMargin = Math.ceil(requiredBurnSun * BURN_MARGIN);
  return Math.max(0, withMargin - Math.max(0, balanceSun));
}
