import { describe, it, expect, vi } from 'vitest';
import { FeeEstimationService, ESTIMATED_TRANSFER_TX_BYTES } from './FeeEstimationService.js';
import type {
  TronResourceClient,
  AccountResources,
  ContractEnergyPolicy,
} from '../tron/TronResourceClient.js';
import type { TrxPriceService } from './TrxPriceService.js';
import { DELEGATION_MARGIN, MIN_DELEGATION_SUN } from '../wallet/DelegationPlanner.js';

const HOT_WALLET = 'THotWalletFixtureAddress';
const DEPOSIT = 'TDepositFixtureAddress';
const BUYER = 'TBuyerFixtureAddress';
const USDT_CONTRACT = 'TUsdtContractFixture';

/** Required delegation for a transfer at a ratio — mirrors DelegationPlanner. */
function requiredDelegationSun(energy: number, ratio: number): number {
  return Math.max(MIN_DELEGATION_SUN, Math.ceil((energy / ratio) * DELEGATION_MARGIN) * 1_000_000);
}

interface ResourceFixture {
  energyRequired?: number;
  hotWallet?: Partial<AccountResources>;
  sender?: Partial<AccountResources>;
  energyFeeSun?: number;
  bandwidthFeeSun?: number;
  policy?: ContractEnergyPolicy;
  policyThrows?: boolean;
  /** SUN the hot wallet can delegate (default 0 — nothing staked). */
  delegatableSun?: number;
  /** Whether the deposit account exists on-chain (default true). */
  senderExists?: boolean;
}

function account(partial: Partial<AccountResources> = {}): AccountResources {
  return {
    energyAvailable: 0,
    bandwidthAvailable: 0,
    // Mainnet-measured ratio (2026-08-29): 180e9 / 18.81e9.
    energyPerTrx: 9.57,
    ...partial,
  };
}

function buildService(fixture: ResourceFixture = {}, priceUsdt = 0.5) {
  const resourceClient = {
    estimateTransferEnergy: vi.fn(async () => fixture.energyRequired ?? 64_285),
    getAccountResources: vi.fn(async (address: string) =>
      address === HOT_WALLET
        ? account({ bandwidthAvailable: 600, ...fixture.hotWallet })
        : account({ ...fixture.sender }),
    ),
    getAccountState: vi.fn(async () => ({ exists: fixture.senderExists ?? true, balanceSun: 0 })),
    getDelegatableEnergySun: vi.fn(async () => fixture.delegatableSun ?? 0),
    getChainFeeParameters: vi.fn(async () => ({
      energyFeeSun: fixture.energyFeeSun ?? 420,
      bandwidthFeeSun: fixture.bandwidthFeeSun ?? 1000,
    })),
    getContractEnergyPolicy: vi.fn(async () => {
      if (fixture.policyThrows) throw new Error('probe failed');
      // Mainnet Tether: the caller pays everything.
      return fixture.policy ?? { callerPercent: 100, originEnergyLimit: 0 };
    }),
  } as unknown as TronResourceClient;
  const priceService = {
    getPrice: vi.fn(async () => ({ priceUsdt, source: 'binance' as const })),
  } as unknown as TrxPriceService;

  const service = new FeeEstimationService({
    resourceClient,
    priceService,
    tokenContracts: { USDT: USDT_CONTRACT, USDC: '' },
    hotWalletAddress: HOT_WALLET,
    tokenDecimals: 6,
  });
  return { service, resourceClient, priceService };
}

describe('FeeEstimationService — payout path (hot wallet sends directly)', () => {
  it('charges zero when the hot wallet covers energy and bandwidth', async () => {
    const { service } = buildService({
      energyRequired: 64_285,
      hotWallet: { energyAvailable: 100_000, bandwidthAvailable: 600 },
    });

    const result = await service.estimate({ toAddress: BUYER, amount: '10.20', token: 'USDT' });

    expect(result.feeUsdt).toBe('0.00');
    expect(result.energyShortfall).toBe(0);
    expect(result.burnSun).toBe(0);
    // No delegation on this path — the whole pool legitimately applies.
    expect(result.delegationPlan).toBeNull();
  });

  it('prices the energy shortfall at the chain energy fee', async () => {
    // 64,285 needed − 4,285 available = 60,000 short × 420 sun = 25.2 TRX.
    const { service } = buildService(
      { energyRequired: 64_285, hotWallet: { energyAvailable: 4_285, bandwidthAvailable: 600 } },
      0.5,
    );

    const result = await service.estimate({ toAddress: BUYER, amount: '10.20', token: 'USDT' });

    expect(result.energyShortfall).toBe(60_000);
    expect(result.burnSun).toBe(60_000 * 420);
    expect(result.feeUsdt).toBe('12.60');
  });
});

describe('FeeEstimationService — refund path (deposit sends, energy is delegated)', () => {
  it.each([
    { name: 'mainnet ratio', ratio: 9.52, energy: 64_285 },
    { name: 'nile ratio', ratio: 73.7, energy: 130_285 },
  ])(
    'charges no Energy when the stake can delegate the WHOLE shortfall ($name)',
    async ({ ratio, energy }) => {
      const { service } = buildService({
        energyRequired: energy,
        hotWallet: { energyAvailable: 0, bandwidthAvailable: 600, energyPerTrx: ratio },
        sender: { bandwidthAvailable: 600 },
        delegatableSun: requiredDelegationSun(energy, ratio),
      });

      const result = await service.estimate({
        fromAddress: DEPOSIT,
        toAddress: BUYER,
        amount: '10.20',
        token: 'USDT',
      });

      expect(result.delegationPlan).toBe('delegate');
      expect(result.delegationSun).toBe(requiredDelegationSun(energy, ratio));
      expect(result.energyShortfall).toBe(0);
      expect(result.feeUsdt).toBe('0.00');
    },
  );

  it('charges the WHOLE shortfall when the stake is one SUN short — no partial credit', async () => {
    // The broadcast will not delegate a partial amount (a deposit without TRX
    // would fail on the rest), so the refund must not be charged as if part
    // were covered. A large hot wallet POOL is irrelevant here: only what can
    // be delegated in full counts.
    const { service } = buildService({
      energyRequired: 64_285,
      hotWallet: { energyAvailable: 5_000_000, bandwidthAvailable: 600, energyPerTrx: 9.52 },
      sender: { bandwidthAvailable: 600 },
      delegatableSun: requiredDelegationSun(64_285, 9.52) - 1,
    });

    const result = await service.estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '10.20',
      token: 'USDT',
    });

    expect(result.delegationPlan).toBe('burn');
    expect(result.delegationSun).toBeNull();
    expect(result.energyShortfall).toBe(64_285);
    expect(result.burnSun).toBe(64_285 * 420);
  });

  it('charges the whole shortfall when nothing is staked', async () => {
    const { service } = buildService({
      energyRequired: 64_285,
      hotWallet: { energyAvailable: 0, bandwidthAvailable: 600 },
      sender: { bandwidthAvailable: 600 },
      delegatableSun: 0,
    });

    const result = await service.estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '10.20',
      token: 'USDT',
    });

    expect(result.energyAvailable).toBe(0);
    expect(result.energyShortfall).toBe(64_285);
  });

  it('charges no Bandwidth for a never-activated deposit — activation grants the free allowance', async () => {
    // Measured on Nile 2026-09-16: before activation the account reports 0
    // Bandwidth; right after it, 600 free, and the transfer used 345 of them.
    const { service } = buildService({
      energyRequired: 29_650,
      hotWallet: { energyAvailable: 0, bandwidthAvailable: 600 },
      sender: { bandwidthAvailable: 0 },
      senderExists: false,
      policy: { callerPercent: 0, originEnergyLimit: 1_000_000_000 },
    });

    const result = await service.estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '8.20',
      token: 'USDT',
    });

    expect(result.bandwidthAvailable).toBe(600);
    expect(result.burnSun).toBe(0);
  });

  it('burns the whole transaction when the sender is short of bandwidth', async () => {
    // TRON charges bandwidth all-or-nothing: an account short of the full byte
    // count pays for every byte, not just the missing ones.
    const { service } = buildService({
      energyRequired: 29_650,
      hotWallet: { energyAvailable: 5_000_000, bandwidthAvailable: 600 },
      sender: { bandwidthAvailable: 100 },
      policy: { callerPercent: 0, originEnergyLimit: 1_000_000_000 },
    });

    const result = await service.estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '8.20',
      token: 'USDT',
    });

    expect(result.burnSun).toBe(ESTIMATED_TRANSFER_TX_BYTES * 1000);
    expect(result.feeUsdt).toBe('0.18');
  });
});

describe('FeeEstimationService — who pays the contract energy', () => {
  it('charges nothing when the contract owner absorbs the energy', async () => {
    // MEASURED 2026-09-04 on Nile: the test USDT (TXYZop…) is deployed with
    // consume_user_resource_percent = 0, so its owner pays. Every rehearsal
    // transfer showed fee: 0 for THIS reason — not delegation, which delivered
    // nothing while the hot wallet held no stake. Without this rule the
    // estimate charged 0.97 USDT for a transfer that cost nobody anything.
    const { service } = buildService({
      energyRequired: 29_650,
      hotWallet: { energyAvailable: 0, bandwidthAvailable: 600 },
      sender: { bandwidthAvailable: 600 },
      policy: { callerPercent: 0, originEnergyLimit: 1_000_000_000 },
    });

    const result = await service.estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '8.20',
      token: 'USDT',
    });

    expect(result.contractCallerPercent).toBe(0);
    expect(result.energyPayableByCaller).toBe(0);
    expect(result.feeUsdt).toBe('0.00');
    // The total is still reported — only who pays it changed.
    expect(result.energyRequired).toBe(29_650);
  });

  it('splits the energy when the contract shares the cost', async () => {
    const { service } = buildService(
      {
        energyRequired: 10_000,
        hotWallet: { energyAvailable: 0, bandwidthAvailable: 600 },
        sender: { bandwidthAvailable: 600 },
        policy: { callerPercent: 30, originEnergyLimit: 1_000 },
        energyFeeSun: 100,
      },
      0.5,
    );

    const result = await service.estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '1.00',
      token: 'USDT',
    });

    expect(result.energyPayableByCaller).toBe(3_000);
    expect(result.burnSun).toBe(3_000 * 100);
  });

  it('assumes the caller pays everything when the policy probe fails', async () => {
    // Conservative direction: a failed probe can only make the estimate
    // larger, never smaller, so an outage cannot quietly undercharge.
    const { service } = buildService({
      energyRequired: 10_000,
      hotWallet: { energyAvailable: 0, bandwidthAvailable: 600 },
      sender: { bandwidthAvailable: 600 },
      policyThrows: true,
      energyFeeSun: 100,
    });

    const result = await service.estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '1.00',
      token: 'USDT',
    });

    expect(result.contractCallerPercent).toBe(100);
    expect(result.energyPayableByCaller).toBe(10_000);
  });
});

describe('FeeEstimationService — request handling', () => {
  it('rounds the USDT charge up, never down', async () => {
    const { service } = buildService(
      { energyRequired: 1, hotWallet: { energyAvailable: 0, bandwidthAvailable: 600 } },
      0.5,
    );

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

    await service.estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '10.20',
      token: 'USDT',
    });

    expect(resourceClient.estimateTransferEnergy).toHaveBeenCalledWith(
      USDT_CONTRACT,
      DEPOSIT,
      BUYER,
      '10200000',
    );
    expect(resourceClient.getAccountResources).toHaveBeenCalledWith(HOT_WALLET);
    expect(resourceClient.getAccountResources).toHaveBeenCalledWith(DEPOSIT);
  });
});
