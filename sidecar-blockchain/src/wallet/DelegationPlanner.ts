/**
 * Pure decisions for a deposit-sourced transfer (sweep, refund): does the
 * sweeper delegate Energy for it or does the deposit burn TRX, and how much
 * of the transfer's Energy does the chain actually bill the caller?
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
 *
 * <para>
 * SIZED FOR THE WHOLE TRANSFER, CHARGED FOR WHAT THE CHAIN BILLS (owner
 * decision 2026-09-17, #323 validation). How much of a call the chain bills
 * its caller depends on the contract owner's REMAINING Energy at execution —
 * a pool every other caller of the contract is spending in the same block.
 * Sizing a delegation or a burn top-up from it fails the transfer on-chain
 * whenever that pool drains between plan and block, with no fallback after
 * it. Over-sizing costs nothing lasting: a delegation is reclaimed and surplus
 * TRX stays in the deposit. So <see cref="planDelegation"/> plans the whole
 * transfer, and only the user's charge uses <see cref="callerEnergyShare"/>.
 * </para>
 */

/**
 * Head-room on the delegated amount. The Energy a stake produces is
 * TotalEnergyLimit / TotalEnergyWeight at the moment the chain executes, which
 * moves with the network's total stake between planning and the block; 10%
 * absorbs that drift and the chain's whole-unit rounding.
 */
export const DELEGATION_MARGIN = 1.1;

/**
 * Head-room on a burn top-up, in percent — the same price drift, from the
 * other side. Integer so SUN amounts stay exact: 6,428,500 × 1.1 in floating
 * point is 7,071,350.000000001, which rounds up to a SUN nobody asked for.
 */
export const BURN_MARGIN_PERCENT = 110;

/**
 * Free daily Bandwidth of an ACTIVATED account. A never-activated deposit
 * reports 0, but the flow activates it before the transfer, so the transfer
 * sees this allowance (measured on Nile 2026-09-16: `freeNetLimit` 600 right
 * after activation, and a TRC-20 call consumed 345 bytes of it).
 */
export const ACTIVATED_ACCOUNT_FREE_BANDWIDTH = 600;

/** Typical size of a signed TRC-20 `triggersmartcontract` transaction (measured 345 bytes on Nile). */
export const ESTIMATED_TRANSFER_TX_BYTES = 350;

/**
 * Bandwidth the activating TRX transfer (hot wallet → deposit, 1 SUN) draws
 * when its sender pays it from STAKED Bandwidth. java-tron sizes a
 * transaction as its serialized form without the result plus 64 result bytes
 * per contract; the Nile activations measured 2026-10-02 carry a 131-byte
 * `raw_data` and one signature → 134 + 67 + 64 = 265 bytes. Rounded up.
 */
export const ACTIVATION_TX_BYTES = 270;

export interface ActivationCostInput {
  /** Whether the deposit already exists on-chain (a TRC-20 receipt does not create it). */
  depositExists: boolean;
  /**
   * STAKED (or delegated-in) Bandwidth of the account that sends the
   * activation — the hot wallet. The free daily allowance cannot pay for
   * creating an account (java-tron `consumeForCreateNewAccount`).
   */
  senderStakedBandwidth: number;
  /** getCreateNewAccountFeeInSystemContract — burned on every activation; null when unread. */
  createNewAccountFeeSun: number | null;
  /** getCreateAccountFee — burned when staked Bandwidth cannot cover the bytes; null when unread. */
  createAccountBandwidthFeeSun: number | null;
  /** getCreateNewAccountBandwidthRate — bytes × rate is what staked Bandwidth must cover. */
  createNewAccountBandwidthRate: number | null;
}

/**
 * SUN the chain burns from the hot wallet when a refund has to activate its
 * deposit first (owner decision 2026-10-02, RefundActivationCostNotCharged:
 * the buyer pays it, as 02 §4.6 says the platform does not lose on a refund).
 * Measured on Nile 2026-10-02: every 1-SUN activation from the hot wallet
 * burned 1,100,000 SUN — `fee` 1 TRX with `receipt.net_fee` 0.1 TRX inside it.
 *
 * <para>
 * 0 when the deposit already exists. Null when a fee the answer depends on was
 * not read — the caller then fails the estimate (the backend falls back to
 * the configured refund fee) rather than charging a guess. An unread rate is
 * the one input that does not fail it: it counts as "staked Bandwidth does not
 * cover the bytes", which can only make the charge larger than what the chain
 * bills, never smaller.
 * </para>
 */
export function activationBurnSun(input: ActivationCostInput): number | null {
  if (input.depositExists) return 0;
  if (input.createNewAccountFeeSun === null) return null;

  const rate = input.createNewAccountBandwidthRate;
  const stakedCoversBytes =
    rate !== null && input.senderStakedBandwidth >= ACTIVATION_TX_BYTES * rate;
  if (stakedCoversBytes) return input.createNewAccountFeeSun;

  if (input.createAccountBandwidthFeeSun === null) return null;
  return input.createNewAccountFeeSun + input.createAccountBandwidthFeeSun;
}

export interface DelegationPlanInput {
  /**
   * Energy the transfer consumes (simulation of the exact call) — planned in
   * FULL, whatever share the chain ends up billing the caller.
   */
  energyRequired: number;
  /** Energy the deposit already has. */
  senderEnergyAvailable: number;
  /** Network Energy per staked TRX; null when the node omitted it. */
  energyPerTrx: number | null;
  /** SUN the sweeper can delegate as ENERGY right now. */
  delegatableSun: number;
}

export type DelegationPlan =
  | { kind: 'no-energy-needed'; energyRequired: number; energyShortfall: 0 }
  | {
      kind: 'delegate';
      energyRequired: number;
      energyShortfall: number;
      delegationSun: number;
    }
  | {
      kind: 'burn';
      energyRequired: number;
      energyShortfall: number;
      reason: 'no-ratio' | 'insufficient-stake';
    };

export function planDelegation(input: DelegationPlanInput): DelegationPlan {
  const energyRequired = Math.max(0, input.energyRequired);
  const energyShortfall = Math.max(0, energyRequired - Math.max(0, input.senderEnergyAvailable));

  if (energyShortfall === 0) {
    return { kind: 'no-energy-needed', energyRequired, energyShortfall: 0 };
  }

  const ratio = input.energyPerTrx;
  if (ratio === null || !Number.isFinite(ratio) || !(ratio > 0)) {
    return { kind: 'burn', energyRequired, energyShortfall, reason: 'no-ratio' };
  }

  // Whole TRX, rounded UP — so a positive shortfall never plans below the
  // 1 TRX minimum Stake 2.0 accepts for a delegation.
  const delegationSun = Math.ceil((energyShortfall / ratio) * DELEGATION_MARGIN) * 1_000_000;

  if (input.delegatableSun >= delegationSun) {
    return { kind: 'delegate', energyRequired, energyShortfall, delegationSun };
  }
  return { kind: 'burn', energyRequired, energyShortfall, reason: 'insufficient-stake' };
}

export interface CallerShareInput {
  /** Energy the call consumes in total. */
  energyRequired: number;
  /** The contract's `consume_user_resource_percent`, 0-100. */
  callerPercent: number;
  /** The contract's `origin_energy_limit` — the most its owner pays for one call. */
  originEnergyLimit: number;
  /** Energy the contract OWNER has left right now. */
  ownerEnergyAvailable: number;
}

/**
 * Energy the chain bills the CALLER of a contract call — java-tron's split
 * (`ReceiptCapsule.payEnergyBill`): the owner pays
 * min(⌊total × (100 − percent) / 100⌋, its remaining Energy, origin_energy_limit)
 * and the caller pays the rest.
 *
 * The percent alone is not the answer. Mainnet Tether sets 30, but its owner
 * has no Energy left, so its callers pay everything: 542 of 542 USDT calls in
 * five sampled blocks, 100.00% of 43.4M Energy (measured 2026-09-17).
 * Unreadable inputs count as "the owner pays nothing", so the result can only
 * come out above what the chain bills, never below.
 */
export function callerEnergyShare(input: CallerShareInput): number {
  const total = nonNegative(input.energyRequired);
  const callerPercent = Number.isFinite(input.callerPercent)
    ? Math.min(100, Math.max(0, input.callerPercent))
    : 100;
  const ownerNominal = Math.floor((total * (100 - callerPercent)) / 100);
  const ownerPays = Math.min(
    ownerNominal,
    nonNegative(input.ownerEnergyAvailable),
    nonNegative(input.originEnergyLimit),
  );
  return total - ownerPays;
}

function nonNegative(value: number): number {
  return Number.isFinite(value) && value > 0 ? value : 0;
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
  const withMargin = Math.ceil((requiredBurnSun * BURN_MARGIN_PERCENT) / 100);
  return Math.max(0, withMargin - Math.max(0, balanceSun));
}
