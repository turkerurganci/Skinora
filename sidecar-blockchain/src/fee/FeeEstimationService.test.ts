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
  /**
   * The stake lives in a dedicated stake account (owner decision 2026-09-17)
   * rather than in the hot wallet — the chain side of it; the delegation flow
   * is configured to match, as production wires both from one variable.
   */
  stakeAccount?: boolean;
  /** SUN the account holding the stake can delegate (default 0 — nothing staked). */
  delegatableSun?: number;
  /** The contract owner's remaining Energy (default 0 — mainnet Tether). */
  ownerEnergy?: number;
  ownerThrows?: boolean;
  policy?: ContractEnergyPolicy;
  policyThrows?: boolean;
  energyFeeSun?: number;
  bandwidthFeeSun?: number;
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
    energyPerTrx: 9.52,
    ...fixture.hotWallet,
  };
  const depositExists = fixture.depositExists ?? true;
  const deposit: AccountResources = depositExists
    ? { energyAvailable: 0, bandwidthAvailable: 600, energyPerTrx: 9.52, ...fixture.deposit }
    : { energyAvailable: 0, bandwidthAvailable: 0, energyPerTrx: null };
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
          energyPerTrx: 9.52,
        };
      }
      return unexpected(address);
    }),
    getAccountState: vi.fn(async (address: string) => {
      if (address === HOT_WALLET) return { exists: true, balanceSun: 2_000_000_000 };
      if (address === DEPOSIT) return { exists: depositExists, balanceSun: 0 };
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
  it("charges only what the owner's remaining Energy leaves to the caller, read from the owner's own account", async () => {
    // Owner nominal ⌊64,285 × 70 / 100⌋ = 44,999, but it has 9,205 left →
    // caller 55,080 × 100 SUN = 5.508 TRX × 0.336 = 1.8507 → 1.86. The hot
    // wallet's 1M Energy and the deposit's 0 are both wrong answers here.
    const { service } = buildService(
      { ownerEnergy: 9_205, hotWallet: { energyAvailable: 1_000_000 } },
      0.336,
    );

    const result = await service.estimate(refund);

    expect(result.contractOwnerEnergyAvailable).toBe(9_205);
    expect(result.energyPayableByCaller).toBe(55_080);
    expect(result.burnSun).toBe(5_508_000);
    expect(result.feeUsdt).toBe('1.86');
  });

  it('charges nothing on a refund when the owner absorbs the call — although the broadcast still sends TRX for all of it', async () => {
    // MEASURED on Nile: the test USDT's owner pays. Without reading the owner
    // the estimate charged 0.97 USDT for a transfer that cost nobody anything.
    const { service } = buildService({
      energyRequired: 29_650,
      policy: OWNER_PAYS_POLICY,
      ownerEnergy: 197_517_927,
    });

    const result = await service.estimate(refund);

    expect(result.contractCallerPercent).toBe(0);
    expect(result.energyPayableByCaller).toBe(0);
    expect(result.delegationPlan).toBe('burn');
    expect(result.feeUsdt).toBe('0.00');
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
    expect(result.feeUsdt).toBe('0.00');
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
    expect(result.burnSun).toBe(6_428_500);
  });

  it("subtracts the deposit's own Energy from what burns", async () => {
    const { service } = buildService({ deposit: { energyAvailable: 4_285 } });

    const result = await service.estimate(refund);

    expect(result.energyShortfall).toBe(60_000);
    expect(result.burnSun).toBe(6_000_000);
  });

  it('charges no Bandwidth for a never-activated deposit — activation grants the free allowance', async () => {
    // Measured on Nile 2026-09-16: before activation the account reports 0
    // Bandwidth; right after it, 600 free, and the transfer used 345 of them.
    const { service } = buildService({
      energyRequired: 29_650,
      depositExists: false,
      policy: OWNER_PAYS_POLICY,
      ownerEnergy: 197_517_927,
    });

    const result = await service.estimate(refund);

    expect(result.bandwidthAvailable).toBe(600);
    expect(result.burnSun).toBe(0);
  });

  it('burns the whole transaction when the sender is short of bandwidth', async () => {
    // TRON charges bandwidth all-or-nothing: an account short of the full byte
    // count pays for every byte — 350 × 1,000 SUN = 0.35 TRX × 0.5 = 0.175 → 0.18.
    const { service } = buildService({
      energyRequired: 29_650,
      deposit: { bandwidthAvailable: 100 },
      policy: OWNER_PAYS_POLICY,
      ownerEnergy: 197_517_927,
    });

    const result = await service.estimate(refund);

    expect(result.burnSun).toBe(350_000);
    expect(result.feeUsdt).toBe('0.18');
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
});
