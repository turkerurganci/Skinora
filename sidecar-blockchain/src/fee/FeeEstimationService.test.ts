import { describe, it, expect, vi } from 'vitest';
import { FeeEstimationService } from './FeeEstimationService.js';
import type {
  TronResourceClient,
  AccountResources,
  ContractEnergyPolicy,
} from '../tron/TronResourceClient.js';
import type { TrxPriceService } from './TrxPriceService.js';
import { EnergyDelegationService } from '../wallet/EnergyDelegationService.js';

const HOT_WALLET = 'THotWalletFixtureAddress';
/** A real address: the delegation flow refuses a malformed stake account. The
 * Nile stake account measured 2026-09-18. */
const STAKE_ACCOUNT = 'TVqvXQhRcmcCQudYx6uhq1WWuNHVmEEyQ6';
const DUMMY_HOT_WALLET_KEY = 'aa'.padStart(64, 'a');
const DEPOSIT = 'TDepositFixtureAddress';
const BUYER = 'TBuyerFixtureAddress';
const USDT_CONTRACT = 'TUsdtContractFixture';
const OWNER = 'TContractOwnerFixture';

/** Mainnet Tether as measured 2026-09-17 (its owner's Energy is set per test). */
const TETHER_POLICY: ContractEnergyPolicy = {
  callerPercent: 30,
  originEnergyLimit: 10_000_000,
  originAddress: OWNER,
};
/** The Nile test USDT as measured 2026-09-17: percent omitted (0), limit 1B. */
const OWNER_PAYS_POLICY: ContractEnergyPolicy = {
  callerPercent: 0,
  originEnergyLimit: 1_000_000_000,
  originAddress: OWNER,
};

interface Fixture {
  energyRequired?: number;
  hotWallet?: Partial<AccountResources>;
  deposit?: Partial<AccountResources>;
  /** Whether the deposit account exists on-chain (default true). */
  depositExists?: boolean;
  /** T169a — TRX the deposit already holds, SUN (default 0; an earlier top-up's remainder). */
  depositBalanceSun?: number;
  /**
   * The stake lives in a dedicated stake account (owner decision 2026-09-17)
   * rather than in the hot wallet — the chain side of it; the delegation flow
   * is configured to match, as production wires both from one variable.
   */
  stakeAccount?: boolean;
  /** SUN the account holding the stake can delegate (default 0 — nothing staked). */
  delegatableSun?: number;
  /**
   * T149 — the dormant stake switch. The refund scenarios below were written
   * for the hybrid model and run with it ON; the production default (OFF) has
   * its own describe block.
   */
  stakingEnabled?: boolean;
  /** The contract owner's remaining Energy (default 0 — mainnet Tether). */
  ownerEnergy?: number;
  ownerThrows?: boolean;
  policy?: ContractEnergyPolicy;
  policyThrows?: boolean;
  energyFeeSun?: number;
  bandwidthFeeSun?: number;
  /** getCreateNewAccountFeeInSystemContract (default 1 TRX, mainnet and Nile 2026-10-02). */
  createNewAccountFeeSun?: number | null;
  /** getCreateAccountFee (default 0.1 TRX, mainnet and Nile 2026-10-02). */
  createAccountBandwidthFeeSun?: number | null;
  /** getCreateNewAccountBandwidthRate (default 1). */
  createNewAccountBandwidthRate?: number | null;
}

/**
 * Every account answers for itself: the hot wallet, the deposit and the
 * contract owner each have their own figures, and any other address throws.
 * A probe pointed at the wrong account therefore changes the charge — a fake
 * that ignored its address argument let such mutations pass (#323 validation).
 */
function buildService(fixture: Fixture = {}, priceUsdt = 0.5) {
  const hotWallet: AccountResources = {
    energyAvailable: 0,
    bandwidthAvailable: 5_000,
    stakedBandwidthAvailable: 0,
    energyPerTrx: 9.52,
    ...fixture.hotWallet,
  };
  const depositExists = fixture.depositExists ?? true;
  const deposit: AccountResources = depositExists
    ? {
        energyAvailable: 0,
        bandwidthAvailable: 600,
        stakedBandwidthAvailable: 0,
        energyPerTrx: 9.52,
        ...fixture.deposit,
      }
    : {
        energyAvailable: 0,
        bandwidthAvailable: 0,
        stakedBandwidthAvailable: 0,
        energyPerTrx: null,
      };
  const unexpected = (address: string): never => {
    throw new Error(`fake chain: no account ${address}`);
  };
  // Who actually holds the frozen TRX in this scenario.
  const stakeHolder = fixture.stakeAccount ? STAKE_ACCOUNT : HOT_WALLET;

  const resourceClient = {
    estimateTransferEnergy: vi.fn(async () => fixture.energyRequired ?? 64_285),
    getAccountResources: vi.fn(async (address: string) => {
      if (address === HOT_WALLET) return hotWallet;
      if (address === DEPOSIT) return deposit;
      if (address === OWNER) {
        if (fixture.ownerThrows) throw new Error('owner probe failed');
        return {
          energyAvailable: fixture.ownerEnergy ?? 0,
          bandwidthAvailable: 0,
          stakedBandwidthAvailable: 0,
          energyPerTrx: 9.52,
        };
      }
      return unexpected(address);
    }),
    getAccountState: vi.fn(async (address: string) => {
      if (address === HOT_WALLET) return { exists: true, balanceSun: 2_000_000_000 };
      if (address === DEPOSIT)
        return {
          exists: depositExists,
          balanceSun: depositExists ? (fixture.depositBalanceSun ?? 0) : 0,
        };
      return unexpected(address);
    }),
    getDelegatableEnergySun: vi.fn(async (address: string) => {
      // The stake lives in exactly ONE account. The other platform account
      // answers 0, the way the chain answers an account with nothing frozen —
      // so a read pointed at the hot wallet after the stake moved plans a burn
      // where the broadcast will delegate, and the charge shows it.
      if (address === stakeHolder) return fixture.delegatableSun ?? 0;
      if (address === HOT_WALLET || address === STAKE_ACCOUNT || address === DEPOSIT) return 0;
      return unexpected(address);
    }),
    getChainFeeParameters: vi.fn(async () => ({
      energyFeeSun: fixture.energyFeeSun ?? 100,
      bandwidthFeeSun: fixture.bandwidthFeeSun ?? 1000,
      createNewAccountFeeSun:
        fixture.createNewAccountFeeSun === undefined ? 1_000_000 : fixture.createNewAccountFeeSun,
      createAccountBandwidthFeeSun:
        fixture.createAccountBandwidthFeeSun === undefined
          ? 100_000
          : fixture.createAccountBandwidthFeeSun,
      createNewAccountBandwidthRate:
        fixture.createNewAccountBandwidthRate === undefined
          ? 1
          : fixture.createNewAccountBandwidthRate,
    })),
    getContractEnergyPolicy: vi.fn(async (contract: string) => {
      if (fixture.policyThrows) throw new Error('probe failed');
      if (contract !== USDT_CONTRACT) return unexpected(contract);
      return fixture.policy ?? TETHER_POLICY;
    }),
  } as unknown as TronResourceClient;
  const priceService = {
    getPrice: vi.fn(async () => ({ priceUsdt, source: 'binance' as const })),
  } as unknown as TrxPriceService;

  // The production object, not a copy of its rule: which account holds the
  // stake is the delegation flow's answer, and the estimate must use it
  // (#325 validation, B1). Only its configuration is exercised here.
  const delegationSource = new EnergyDelegationService({
    client: {} as never,
    resources: {} as never,
    sweeperAddress: HOT_WALLET,
    sweeperPrivateKey: DUMMY_HOT_WALLET_KEY,
    stakingEnabled: fixture.stakingEnabled ?? true,
    stakeAddress: fixture.stakeAccount ? STAKE_ACCOUNT : undefined,
    fallbackAmountSun: 15_000_000,
  });

  const service = new FeeEstimationService({
    resourceClient,
    priceService,
    tokenContracts: { USDT: USDT_CONTRACT, USDC: '' },
    hotWalletAddress: HOT_WALLET,
    delegationSource,
    tokenDecimals: 6,
  });
  return { service, resourceClient, priceService };
}

const payout = { toAddress: BUYER, amount: '10.20', token: 'USDT' as const };
const refund = { fromAddress: DEPOSIT, toAddress: BUYER, amount: '10.20', token: 'USDT' as const };

describe('FeeEstimationService — payout path (hot wallet sends directly)', () => {
  it("charges zero when the hot wallet's own Energy and Bandwidth cover the transfer", async () => {
    const { service } = buildService({ hotWallet: { energyAvailable: 100_000 } });

    const result = await service.estimate(payout);

    expect(result.feeUsdt).toBe('0.00');
    expect(result.energyShortfall).toBe(0);
    expect(result.burnSun).toBe(0);
    // No delegation on this path — the whole pool legitimately applies.
    expect(result.delegationPlan).toBeNull();
  });

  it('prices the Energy shortfall at the chain energy fee', async () => {
    // 64,285 needed − 4,285 held = 60,000 × 420 SUN = 25.2 TRX × 0.5 = 12.60.
    const { service } = buildService({
      hotWallet: { energyAvailable: 4_285 },
      energyFeeSun: 420,
    });

    const result = await service.estimate(payout);

    expect(result.energyShortfall).toBe(60_000);
    expect(result.burnSun).toBe(25_200_000);
    expect(result.feeUsdt).toBe('12.60');
  });

  it('prices a payout from the hot wallet pool even with a stake account — the payout share is delegated into that pool', async () => {
    // #325 validation, B2 (owner decision 2026-09-18): the stake account
    // delegates the payout share to the hot wallet once and leaves it there.
    // The chain reports delegated Energy in the receiver's own EnergyLimit
    // (Nile, 2026-09-18: 7,370 → 7,444 for 1 TRX), and the payout sends from
    // the hot wallet — the stake account is never asked on this path.
    const { service } = buildService({
      stakeAccount: true,
      hotWallet: { energyAvailable: 130_285 },
    });

    const result = await service.estimate(payout);

    expect(result.energyShortfall).toBe(0);
    expect(result.feeUsdt).toBe('0.00');
    expect(result.delegationPlan).toBeNull();
  });

  it('charges the WHOLE transfer on mainnet Tether although the contract says 30%', async () => {
    // Its owner has no Energy left, so the chain bills the caller 100%:
    // 64,285 × 100 SUN = 6.4285 TRX × 0.336 = 2.1599 → 2.16. Charging the
    // nominal 30% would have taken 0.65.
    const { service } = buildService({ ownerEnergy: 0 }, 0.336);

    const result = await service.estimate(payout);

    expect(result.energyPayableByCaller).toBe(64_285);
    expect(result.contractCallerPercent).toBe(30);
    expect(result.burnSun).toBe(6_428_500);
    expect(result.feeUsdt).toBe('2.16');
  });

  it('charges nothing when the contract owner absorbs the call', async () => {
    const { service } = buildService({
      energyRequired: 29_650,
      policy: OWNER_PAYS_POLICY,
      ownerEnergy: 197_517_927,
    });

    const result = await service.estimate(payout);

    expect(result.energyPayableByCaller).toBe(0);
    expect(result.feeUsdt).toBe('0.00');
  });

  it("never credits the owner past the contract's origin_energy_limit", async () => {
    // Owner pays min(64,285, 1B left, 20,000 limit) = 20,000; caller 44,285.
    const { service } = buildService({
      policy: { callerPercent: 0, originEnergyLimit: 20_000, originAddress: OWNER },
      ownerEnergy: 1_000_000_000,
    });

    const result = await service.estimate(payout);

    expect(result.energyPayableByCaller).toBe(44_285);
    expect(result.burnSun).toBe(4_428_500);
  });
});

describe('FeeEstimationService — who pays the contract Energy', () => {
  it("charges a payout only what the owner's remaining Energy leaves to the caller, read from the owner's own account", async () => {
    // Owner nominal ⌊64,285 × 70 / 100⌋ = 44,999, but it has 9,205 left →
    // caller 55,080 × 100 SUN = 5.508 TRX × 0.336 = 1.8507 → 1.86. A hot
    // wallet pool of 0 burns all of it. (T169a: a payout still pays the
    // chain-billed share; a refund pays what the broadcast sends — below.)
    const { service } = buildService({ ownerEnergy: 9_205 }, 0.336);

    const result = await service.estimate(payout);

    expect(result.contractOwnerEnergyAvailable).toBe(9_205);
    expect(result.energyPayableByCaller).toBe(55_080);
    expect(result.burnSun).toBe(5_508_000);
    expect(result.feeUsdt).toBe('1.86');
  });

  it("T169a — a refund is charged the TRX the broadcast sends, whatever share the owner's Energy would absorb", async () => {
    // Same chain as above, but the deposit sends: the burn top-up is sized for
    // ALL 64,285 Energy (owner share ignored — 2026-09-17), 6,428,500 SUN +
    // 10% = 7,071,350 SUN, none of it collected back. Before T169a the refund
    // was charged the caller share, 5,508,000 SUN → 1.86.
    // 7.07135 TRX × 0.336 = 2.37597… → 2.38.
    const { service } = buildService(
      { ownerEnergy: 9_205, hotWallet: { energyAvailable: 1_000_000 } },
      0.336,
    );

    const result = await service.estimate(refund);

    expect(result.energyPayableByCaller).toBe(55_080);
    expect(result.energyShortfall).toBe(64_285);
    expect(result.topUpSun).toBe(7_071_350);
    expect(result.burnSun).toBe(7_071_350);
    expect(result.feeUsdt).toBe('2.38');
  });

  it('T169a — charges the refund the top-up even when the owner absorbs the whole call (Nile): the TRX stays on the deposit', async () => {
    // MEASURED on Nile: the test USDT's owner pays, so the deposit burns
    // nothing — but the broadcast still sends the TRX for all of it and none
    // comes back (08 §3.3 "Yakma yolu": ~1.6 TRX left per Nile transfer).
    // Before T169a this refund was charged 0.00.
    // 29,650 × 100 SUN = 2,965,000 SUN + 10% = 3,261,500 SUN × 0.5 = 1.63075 → 1.64.
    const { service } = buildService({
      energyRequired: 29_650,
      policy: OWNER_PAYS_POLICY,
      ownerEnergy: 197_517_927,
    });

    const result = await service.estimate(refund);

    expect(result.contractCallerPercent).toBe(0);
    expect(result.energyPayableByCaller).toBe(0);
    expect(result.delegationPlan).toBe('burn');
    expect(result.topUpSun).toBe(3_261_500);
    expect(result.feeUsdt).toBe('1.64');
    // The total is still reported — only who pays it changed.
    expect(result.energyRequired).toBe(29_650);
  });

  it('assumes the caller pays everything when the policy probe fails', async () => {
    // Conservative direction: a failed probe can only make the estimate
    // larger, never smaller, so an outage cannot quietly undercharge.
    const { service } = buildService({ energyRequired: 10_000, policyThrows: true });

    const result = await service.estimate(refund);

    expect(result.contractCallerPercent).toBe(100);
    expect(result.energyPayableByCaller).toBe(10_000);
  });

  it("assumes the owner pays nothing when the owner's account cannot be read", async () => {
    const { service } = buildService({
      policy: OWNER_PAYS_POLICY,
      ownerEnergy: 197_517_927,
      ownerThrows: true,
    });

    const result = await service.estimate(refund);

    expect(result.contractOwnerEnergyAvailable).toBe(0);
    expect(result.energyPayableByCaller).toBe(64_285);
  });

  it('assumes the owner pays nothing when getcontract names no owner', async () => {
    const { service } = buildService({
      policy: { ...OWNER_PAYS_POLICY, originAddress: null },
      ownerEnergy: 197_517_927,
    });

    const result = await service.estimate(refund);

    expect(result.energyPayableByCaller).toBe(64_285);
  });
});

describe('FeeEstimationService — refund path (deposit sends, the broadcast plans the whole transfer)', () => {
  it.each([
    // 64,285 ÷ 9.52 × 1.1 = 7,427.9 → 7,428 TRX
    { name: 'mainnet ratio', ratio: 9.52, energy: 64_285, sun: 7_428_000_000 },
    // 130,285 ÷ 73.7 × 1.1 = 1,944.6 → 1,945 TRX
    { name: 'nile ratio', ratio: 73.7, energy: 130_285, sun: 1_945_000_000 },
  ])(
    'charges no Energy when the stake can delegate the WHOLE transfer ($name)',
    async ({ ratio, energy, sun }) => {
      const { service } = buildService({
        energyRequired: energy,
        hotWallet: { energyPerTrx: ratio },
        delegatableSun: sun,
      });

      const result = await service.estimate(refund);

      expect(result.delegationPlan).toBe('delegate');
      expect(result.delegationSun).toBe(sun);
      expect(result.energyShortfall).toBe(0);
      expect(result.feeUsdt).toBe('0.00');
    },
  );

  /**
   * #325 validation, B1. Once the stake moves to its own account the hot
   * wallet has nothing to delegate, but the broadcast delegates from the stake
   * account. An estimate still reading the hot wallet planned a burn and took
   * 64,285 × 100 SUN = 6.4285 TRX × 0.336 = 2.16 USDT from the buyer's refund
   * for Energy the broadcast then delegated for free.
   */
  it('reads what can be delegated from the stake account once one is configured, as the broadcast does', async () => {
    const { service, resourceClient } = buildService(
      { stakeAccount: true, delegatableSun: 7_428_000_000 },
      0.336,
    );

    const result = await service.estimate(refund);

    expect(resourceClient.getDelegatableEnergySun).toHaveBeenCalledWith(STAKE_ACCOUNT);
    expect(result.delegationPlan).toBe('delegate');
    expect(result.delegationSun).toBe(7_428_000_000);
    expect(result.feeUsdt).toBe('0.00');
    // What the startup line reports for the estimate (index.ts).
    expect(service.delegationOwner).toBe(STAKE_ACCOUNT);
  });

  it('plans for the WHOLE transfer even when the owner would pay all of it — as the broadcast does', async () => {
    // One SUN short of delegating the whole 64,285 → the broadcast burns. A plan
    // sized to the caller's share (0 here) would claim no Energy is needed.
    const { service } = buildService({
      policy: OWNER_PAYS_POLICY,
      ownerEnergy: 197_517_927,
      delegatableSun: 7_427_999_999,
    });

    const result = await service.estimate(refund);

    expect(result.delegationPlan).toBe('burn');
    // T169a — the burn it plans is what is charged: 7,071,350 SUN × 0.5 = 3.535675 → 3.54.
    expect(result.feeUsdt).toBe('3.54');
  });

  it('charges the whole shortfall when the stake is one SUN short — no partial credit', async () => {
    // The broadcast will not delegate a partial amount, so the refund must not
    // be charged as if part were covered. A large hot wallet POOL is irrelevant:
    // only what can be delegated in full counts.
    const { service } = buildService({
      hotWallet: { energyAvailable: 5_000_000 },
      delegatableSun: 7_427_999_999,
    });

    const result = await service.estimate(refund);

    expect(result.delegationPlan).toBe('burn');
    expect(result.delegationSun).toBeNull();
    expect(result.energyShortfall).toBe(64_285);
    // T169a — the top-up: 6,428,500 + 10% (before: the bare 6,428,500).
    expect(result.burnSun).toBe(7_071_350);
  });

  it("subtracts the deposit's own Energy from what burns", async () => {
    const { service } = buildService({ deposit: { energyAvailable: 4_285 } });

    const result = await service.estimate(refund);

    expect(result.energyShortfall).toBe(60_000);
    // 6,000,000 + 10%.
    expect(result.burnSun).toBe(6_600_000);
  });

  it("charges no Bandwidth for a never-activated deposit's transfer — activation grants the free allowance", async () => {
    // Measured on Nile 2026-09-16: before activation the account reports 0
    // Bandwidth; right after it, 600 free, and the transfer used 345 of them.
    // The activation itself is priced separately (next block of tests).
    const { service } = buildService({
      energyRequired: 29_650,
      depositExists: false,
      policy: OWNER_PAYS_POLICY,
      ownerEnergy: 197_517_927,
    });

    const result = await service.estimate(refund);

    expect(result.bandwidthAvailable).toBe(600);
    // T169a — the top-up carries Energy only: 2,965,000 + 10% − the 1 SUN the
    // activation left = 3,261,499 (a Bandwidth part would add 385,000).
    expect(result.topUpSun).toBe(3_261_499);
    expect(result.burnSun - result.activationBurnSun).toBe(1 + 3_261_499);
  });

  it('burns the whole transaction when the sender is short of bandwidth', async () => {
    // TRON charges bandwidth all-or-nothing: an account short of the full byte
    // count pays for every byte — 350 × 1,000 SUN = 0.35 TRX. T169a: the
    // top-up carries it with the Energy, (2,965,000 + 350,000) + 10% =
    // 3,646,500 SUN × 0.5 = 1.82325 → 1.83 (before: 0.18, the bytes alone).
    const { service } = buildService({
      energyRequired: 29_650,
      deposit: { bandwidthAvailable: 100 },
      policy: OWNER_PAYS_POLICY,
      ownerEnergy: 197_517_927,
    });

    const result = await service.estimate(refund);

    expect(result.burnSun).toBe(3_646_500);
    expect(result.feeUsdt).toBe('1.83');
  });
});

/**
 * RefundActivationCostNotCharged — owner decision 2026-10-02: the buyer pays
 * the activation of a deposit that has only ever received TRC-20 (02 §4.6).
 * The transfer itself is made free here (the contract owner absorbs it, the
 * activated deposit's free Bandwidth covers the bytes), so every SUN in the
 * charge is the activation.
 */
describe('FeeEstimationService — refund from a never-activated deposit', () => {
  const freeTransfer = {
    energyRequired: 29_650,
    depositExists: false,
    policy: OWNER_PAYS_POLICY,
    ownerEnergy: 197_517_927,
  };

  it('charges the 1 TRX creation fee plus the 0.1 TRX Bandwidth fee when the hot wallet has no staked Bandwidth', async () => {
    // Nile 2026-10-02: every 1-SUN activation from the hot wallet burned
    // 1,100,000 SUN (`fee` 1 TRX, `receipt.net_fee` 0.1 TRX). The hot wallet's
    // 5,000 FREE Bandwidth (fixture default) does not help: the chain cannot
    // create an account from it. T169a: the 1 SUN and the top-up are charged
    // too — 1 + 1,100,000 + 3,261,499 = 4,361,500 SUN × 0.336 = 1.465464 → 1.47
    // (before: 1.1 TRX alone, 0.37).
    const { service } = buildService(freeTransfer, 0.336);

    const result = await service.estimate(refund);

    expect(result.activationBurnSun).toBe(1_100_000);
    expect(result.burnSun).toBe(4_361_500);
    expect(result.feeUsdt).toBe('1.47');
  });

  it("drops the Bandwidth fee when the hot wallet's STAKED Bandwidth covers the activation bytes", async () => {
    const { service } = buildService({
      ...freeTransfer,
      hotWallet: { stakedBandwidthAvailable: 270 },
    });

    const result = await service.estimate(refund);

    expect(result.activationBurnSun).toBe(1_000_000);
  });

  it('keeps the Bandwidth fee when staked Bandwidth is one byte short', async () => {
    const { service } = buildService({
      ...freeTransfer,
      hotWallet: { stakedBandwidthAvailable: 269 },
    });

    const result = await service.estimate(refund);

    expect(result.activationBurnSun).toBe(1_100_000);
  });

  it('scales the activation bytes by getCreateNewAccountBandwidthRate', async () => {
    // 300 staked covers 270 bytes at rate 1, not 540 at rate 2.
    const { service } = buildService({
      ...freeTransfer,
      hotWallet: { stakedBandwidthAvailable: 300 },
      createNewAccountBandwidthRate: 2,
    });

    const result = await service.estimate(refund);

    expect(result.activationBurnSun).toBe(1_100_000);
  });

  it('counts an unread rate as "staked Bandwidth does not cover it" — the charge can only come out larger', async () => {
    const { service } = buildService({
      ...freeTransfer,
      hotWallet: { stakedBandwidthAvailable: 10_000 },
      createNewAccountBandwidthRate: null,
    });

    const result = await service.estimate(refund);

    expect(result.activationBurnSun).toBe(1_100_000);
  });

  it('fails the estimate when the creation fee is unread — the backend then charges its configured refund fee', async () => {
    const { service } = buildService({ ...freeTransfer, createNewAccountFeeSun: null });

    await expect(service.estimate(refund)).rejects.toMatchObject({
      code: 'FEE_ESTIMATE_CHAIN_PARAMS_FAILED',
      retryable: true,
    });
  });

  it('fails the estimate when the Bandwidth fee is needed but unread', async () => {
    const { service } = buildService({ ...freeTransfer, createAccountBandwidthFeeSun: null });

    await expect(service.estimate(refund)).rejects.toMatchObject({
      code: 'FEE_ESTIMATE_CHAIN_PARAMS_FAILED',
    });
  });

  it('does not need the Bandwidth fee when staked Bandwidth covers the bytes', async () => {
    const { service } = buildService({
      ...freeTransfer,
      hotWallet: { stakedBandwidthAvailable: 270 },
      createAccountBandwidthFeeSun: null,
    });

    const result = await service.estimate(refund);

    expect(result.activationBurnSun).toBe(1_000_000);
  });

  it('charges no activation for a deposit that already exists — and does not need the creation fees', async () => {
    const { service } = buildService({
      ...freeTransfer,
      depositExists: true,
      createNewAccountFeeSun: null,
      createAccountBandwidthFeeSun: null,
      createNewAccountBandwidthRate: null,
    });

    const result = await service.estimate(refund);

    expect(result.activationBurnSun).toBe(0);
    // T169a — only the top-up (2,965,000 + 10%; the deposit holds 0).
    expect(result.burnSun).toBe(3_261_500);
  });

  it('never charges activation on a payout — the hot wallet sends, and it exists', async () => {
    const { service } = buildService({
      ...freeTransfer,
      createNewAccountFeeSun: null,
      createAccountBandwidthFeeSun: null,
    });

    const result = await service.estimate(payout);

    expect(result.activationBurnSun).toBe(0);
  });
});

describe('FeeEstimationService — request handling', () => {
  it('rounds the USDT charge up, never down', async () => {
    // 1 Energy × 100 SUN = 0.0001 TRX × 0.5 = 0.00005 USDT → 0.01.
    const { service } = buildService({ energyRequired: 1 });

    const result = await service.estimate({ toAddress: BUYER, amount: '1.00', token: 'USDT' });

    expect(result.feeUsdt).toBe('0.01');
  });

  it('rejects a token without a configured contract', async () => {
    const { service } = buildService();

    await expect(
      service.estimate({ toAddress: BUYER, amount: '1.00', token: 'USDC' }),
    ).rejects.toMatchObject({ code: 'TOKEN_CONTRACT_NOT_CONFIGURED', retryable: false });
  });

  it('simulates the transfer as the requested sender', async () => {
    const { service, resourceClient } = buildService();

    await service.estimate(refund);

    expect(resourceClient.estimateTransferEnergy).toHaveBeenCalledWith(
      USDT_CONTRACT,
      DEPOSIT,
      BUYER,
      '10200000',
    );
  });

  it('simulates a payout as the hot wallet when the request names no sender', async () => {
    // The backend omits fromAddress on payouts (PayoutGasEstimateAlwaysFallsBack),
    // and the hot wallet is the account that signs them. A stake account is
    // configured so that a sender taken from the delegation owner — or from
    // the recipient — differs from the hot wallet and fails here; before this
    // test only an EMPTY sender failed, via HOT_WALLET_NOT_CONFIGURED (#327
    // validation).
    const { service, resourceClient } = buildService({ stakeAccount: true });

    await service.estimate(payout);

    expect(resourceClient.estimateTransferEnergy).toHaveBeenCalledWith(
      USDT_CONTRACT,
      HOT_WALLET,
      BUYER,
      '10200000',
    );
  });
});

/**
 * T149 (11 §F8, öneriyle kaydedildi — ⚠) — with the dormant stake OFF (the
 * production default) the refund estimate reads no stake and plans the burn
 * the broadcast will take, whatever the stake could cover.
 */
describe('FeeEstimationService — staking switched off (production default)', () => {
  it('reads no stake and charges the burn, although the stake could delegate the whole transfer', async () => {
    const { service, resourceClient } = buildService({
      stakingEnabled: false,
      delegatableSun: 7_428_000_000 * 10,
    });

    const result = await service.estimate(refund);

    expect(resourceClient.getDelegatableEnergySun).not.toHaveBeenCalled();
    expect(result.delegationPlan).toBe('burn');
    expect(result.delegationSun).toBeNull();
    // T169a — the top-up it sends: 64,285 × 100 SUN + 10% = 7,071,350 SUN ×
    // 0.5 = 3.535675 → 3.54 (before: the bare burn, 3.22).
    expect(result.feeUsdt).toBe('3.54');
  });

  it('the same stake is read and delegated with the switch ON — the switch is what decides', async () => {
    const { service, resourceClient } = buildService({
      stakingEnabled: true,
      delegatableSun: 7_428_000_000 * 10,
    });

    const result = await service.estimate(refund);

    expect(resourceClient.getDelegatableEnergySun).toHaveBeenCalledOnce();
    expect(result.delegationPlan).toBe('delegate');
    expect(result.feeUsdt).toBe('0.00');
  });
});

/**
 * T169a (proje sahibi kararı 2026-10-21 — ⚠ PARA) — a refund is charged the
 * platform's whole outlay for it: what EnergyDelegationService sends and
 * reports as `platformSpendSun` (T160c F1). Every expected value is a literal
 * worked out by hand; the T160c "before / after" figures are reproduced.
 */
describe('FeeEstimationService — T169a refund charge = the platform spend of the broadcast', () => {
  /** T159b's TRX price (T160c F1 table). Not a constant the code uses. */
  const T159B_TRX = 0.3313;
  /** Mainnet 2026-10: 100 SUN per Energy, 1,000 SUN per Bandwidth byte (08 §3.3). */
  const holder = { energyRequired: 64_285, depositExists: false, stakingEnabled: false };

  it('Nile 6 (INCORRECT_AMOUNT_REFUND, T160c F1): 2,711,500 SUN — charged 0.90, was 0.37 against a measured spend of 0.898320', async () => {
    // Measured spend: 1 SUN activation + 1,100,000 creation fee + 1,611,499
    // top-up (left on the deposit — the Nile owner paid the Energy) = 2,711,500
    // SUN = 0.898320 USDT. The fixture reproduces the top-up with 14,650 Energy
    // × 100 SUN = 1,465,000 SUN; + 10% = 1,611,500, less the activation's 1 SUN.
    // The hot wallet had free Bandwidth for the top-up (measured fee 0).
    const { service } = buildService(
      {
        energyRequired: 14_650,
        depositExists: false,
        stakingEnabled: false,
        policy: OWNER_PAYS_POLICY,
        ownerEnergy: 197_517_927,
      },
      T159B_TRX,
    );

    const result = await service.estimate(refund);

    expect(result.energyPayableByCaller).toBe(0);
    expect(result.activationBurnSun).toBe(1_100_000);
    expect(result.topUpSun).toBe(1_611_499);
    expect(result.topUpFeeSun).toBe(0);
    expect(result.burnSun).toBe(2_711_500);
    // 2.7115 TRX × 0.3313 = 0.89831995 → 0.90 (the old charge priced 1,100,000 SUN → 0.37).
    expect(result.feeUsdt).toBe('0.90');
  });

  it('mainnet, recipient holds USDT, hot wallet out of Bandwidth: 8,441,350 SUN → 2.80 (was 7,528,500 → 2.50)', async () => {
    // 1 + 1,100,000 + (6,428,500 + 10% − 1 = 7,071,349) + 270 × 1,000 = 8,441,350.
    const { service } = buildService(
      { ...holder, hotWallet: { bandwidthAvailable: 0 } },
      T159B_TRX,
    );

    const result = await service.estimate(refund);

    expect(result.topUpSun).toBe(7_071_349);
    expect(result.topUpFeeSun).toBe(270_000);
    expect(result.burnSun).toBe(8_441_350);
    // 8.44135 × 0.3313 = 2.796619… → 2.80.
    expect(result.feeUsdt).toBe('2.80');
  });

  it('mainnet, first USDT to the recipient: 15,701,350 SUN → 5.21 (was 14,128,500 → 4.69)', async () => {
    // 130,285 × 100 = 13,028,500 + 10% = 14,331,350 − 1 = 14,331,349.
    const { service } = buildService(
      { ...holder, energyRequired: 130_285, hotWallet: { bandwidthAvailable: 0 } },
      T159B_TRX,
    );

    const result = await service.estimate(refund);

    expect(result.topUpSun).toBe(14_331_349);
    expect(result.burnSun).toBe(15_701_350);
    // 15.70135 × 0.3313 = 5.2018572… → 5.21.
    expect(result.feeUsdt).toBe('5.21');
  });

  it('the top-up rides on the hot wallet Bandwidth while 270 bytes are left, and burns them one byte short', async () => {
    const enough = buildService({ ...holder, hotWallet: { bandwidthAvailable: 270 } }).service;
    const short = buildService({ ...holder, hotWallet: { bandwidthAvailable: 269 } }).service;

    const [covered, burned] = await Promise.all([enough.estimate(refund), short.estimate(refund)]);

    expect(covered.topUpFeeSun).toBe(0);
    expect(burned.topUpFeeSun).toBe(270_000);
    expect(burned.burnSun - covered.burnSun).toBe(270_000);
  });

  it("an existing deposit's own TRX is subtracted, as the broadcast's top-up subtracts it", async () => {
    // An earlier top-up left 2,000,000 SUN: 7,071,350 − 2,000,000 = 5,071,350.
    const { service } = buildService({
      energyRequired: 64_285,
      stakingEnabled: false,
      depositBalanceSun: 2_000_000,
    });

    const result = await service.estimate(refund);

    expect(result.activationBurnSun).toBe(0);
    expect(result.topUpSun).toBe(5_071_350);
    expect(result.burnSun).toBe(5_071_350);
  });

  it('nothing is sent when the deposit already holds enough TRX — nothing charged, no top-up fee', async () => {
    const { service } = buildService({
      energyRequired: 64_285,
      stakingEnabled: false,
      depositBalanceSun: 7_071_350,
      hotWallet: { bandwidthAvailable: 0 },
    });

    const result = await service.estimate(refund);

    expect(result.topUpSun).toBe(0);
    expect(result.topUpFeeSun).toBe(0);
    expect(result.feeUsdt).toBe('0.00');
  });

  it('a delegated refund (stake switch ON) is charged its Bandwidth top-up only', async () => {
    // The stake covers the Energy; the deposit is short of Bandwidth:
    // 350 × 1,000 = 350,000 + 10% = 385,000 SUN × 0.5 = 0.1925 → 0.20.
    const { service } = buildService({
      stakingEnabled: true,
      delegatableSun: 7_428_000_000,
      deposit: { bandwidthAvailable: 0 },
    });

    const result = await service.estimate(refund);

    expect(result.delegationPlan).toBe('delegate');
    expect(result.topUpSun).toBe(385_000);
    expect(result.feeUsdt).toBe('0.20');
  });

  it('a payout is unchanged: no top-up, the chain-billed burn only', async () => {
    const { service } = buildService({ energyRequired: 64_285 }, T159B_TRX);

    const result = await service.estimate(payout);

    expect(result.topUpSun).toBe(0);
    expect(result.topUpFeeSun).toBe(0);
    // 6,428,500 × 0.3313 = 2.12976… → 2.13.
    expect(result.burnSun).toBe(6_428_500);
    expect(result.feeUsdt).toBe('2.13');
  });
});

/**
 * T149 — the basis of the buyer's TRON network fee line (11 §F8 K8, 08 §3.1a):
 * one fresh deposit swept to the hot wallet on the burn path. Expected values
 * are worked out by hand from the mainnet parameters (2026-09/10): 100 SUN per
 * Energy, 1,000 SUN per Bandwidth byte, 1 TRX + 0.1 TRX account creation.
 */
describe('FeeEstimationService — sweep cost (TRON network fee line)', () => {
  it('prices activation + the whole-transfer burn top-up with its 10% margin + the top-up bandwidth', async () => {
    const { service } = buildService({}, 0.34);

    const result = await service.estimateSweepCost('USDT');

    // 1,000,000 + 100,000 (hot wallet has no staked Bandwidth).
    expect(result.activationSun).toBe(1_100_000);
    // 64,285 × 100 = 6,428,500 × 1.1 = 7,071,350 − the 1 SUN activation left in the deposit.
    expect(result.burnTopUpSun).toBe(7_071_349);
    // 270 bytes × 1,000 SUN — the hot wallet's TRX transfer, assumed burned.
    expect(result.bandwidthSun).toBe(270_000);
    expect(result.sweepCostSun).toBe(8_441_349);
    expect(result.energyRequired).toBe(64_285);
    expect(result.trxPriceUsdt).toBe(0.34);
    // 8.441349 × 0.34 = 2.870059 — unrounded; the backend adds its buffer and rounds once.
    expect(result.costUsdt).toBe('2.870059');
  });

  it('simulates a 1-unit transfer into the hot wallet from the hot wallet — a transfer between two holders', async () => {
    const { service, resourceClient } = buildService();

    await service.estimateSweepCost('USDT');

    expect(resourceClient.estimateTransferEnergy).toHaveBeenCalledWith(
      USDT_CONTRACT,
      HOT_WALLET,
      HOT_WALLET,
      '1',
    );
  });

  it('prices the burn for the WHOLE transfer even when the contract owner would pay its share — the TRX sent is not recovered', async () => {
    const { service } = buildService({ policy: OWNER_PAYS_POLICY, ownerEnergy: 10_000_000 });

    const result = await service.estimateSweepCost('USDT');

    expect(result.burnTopUpSun).toBe(7_071_349);
  });

  it("drops the activation's bandwidth fee when the hot wallet's staked Bandwidth covers it", async () => {
    const { service } = buildService({ hotWallet: { stakedBandwidthAvailable: 270 } });

    const result = await service.estimateSweepCost('USDT');

    expect(result.activationSun).toBe(1_000_000);
  });

  it('reads no stake, whatever the switch says — the line always prices the burn path', async () => {
    const { service, resourceClient } = buildService({
      stakingEnabled: true,
      delegatableSun: 7_428_000_000 * 10,
    });

    const result = await service.estimateSweepCost('USDT');

    expect(resourceClient.getDelegatableEnergySun).not.toHaveBeenCalled();
    expect(result.burnTopUpSun).toBe(7_071_349);
  });

  it('fails retryable when the account-creation fee is unread — the backend writes its fallback line', async () => {
    const { service } = buildService({ createNewAccountFeeSun: null });

    await expect(service.estimateSweepCost('USDT')).rejects.toMatchObject({
      code: 'FEE_ESTIMATE_CHAIN_PARAMS_FAILED',
      retryable: true,
    });
  });

  it('rejects a token without a configured contract', async () => {
    const { service } = buildService();

    await expect(service.estimateSweepCost('USDC')).rejects.toMatchObject({
      code: 'TOKEN_CONTRACT_NOT_CONFIGURED',
      retryable: false,
    });
  });
});
