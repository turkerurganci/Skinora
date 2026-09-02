import { describe, it, expect, vi } from 'vitest';
import { FeeEstimationService, ESTIMATED_TRANSFER_TX_BYTES } from './FeeEstimationService.js';
import type { TronResourceClient, AccountResources } from '../tron/TronResourceClient.js';
import type { TrxPriceService } from './TrxPriceService.js';

const HOT_WALLET = 'THotWalletFixtureAddress';
const DEPOSIT = 'TDepositFixtureAddress';
const BUYER = 'TBuyerFixtureAddress';
const USDT_CONTRACT = 'TUsdtContractFixture';

interface ResourceFixture {
  energyRequired?: number;
  hotWallet?: AccountResources;
  sender?: AccountResources;
  energyFeeSun?: number;
  bandwidthFeeSun?: number;
}

function buildService(fixture: ResourceFixture = {}, priceUsdt = 0.5) {
  const resourceClient = {
    estimateTransferEnergy: vi.fn(async () => fixture.energyRequired ?? 64_285),
    getAccountResources: vi.fn(async (address: string) => {
      if (address === HOT_WALLET) {
        return fixture.hotWallet ?? { energyAvailable: 0, bandwidthAvailable: 600 };
      }
      return fixture.sender ?? { energyAvailable: 0, bandwidthAvailable: 0 };
    }),
    getChainFeeParameters: vi.fn(async () => ({
      energyFeeSun: fixture.energyFeeSun ?? 420,
      bandwidthFeeSun: fixture.bandwidthFeeSun ?? 1000,
    })),
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

describe('FeeEstimationService', () => {
  it('charges zero when hot-wallet energy and sender bandwidth cover the transfer', async () => {
    const { service } = buildService({
      energyRequired: 64_285,
      hotWallet: { energyAvailable: 100_000, bandwidthAvailable: 600 },
    });

    const result = await service.estimate({ toAddress: BUYER, amount: '10.20', token: 'USDT' });

    expect(result.feeUsdt).toBe('0.00');
    expect(result.energyShortfall).toBe(0);
    expect(result.burnSun).toBe(0);
  });

  it('prices the energy shortfall at the chain energy fee', async () => {
    // 64,285 needed − 4,285 available = 60,000 shortfall × 420 sun = 25.2 TRX.
    // Sender (hot wallet default) has bandwidth, so no bandwidth burn.
    const { service } = buildService(
      {
        energyRequired: 64_285,
        hotWallet: { energyAvailable: 4_285, bandwidthAvailable: 600 },
      },
      0.5,
    );

    const result = await service.estimate({ toAddress: BUYER, amount: '10.20', token: 'USDT' });

    expect(result.energyShortfall).toBe(60_000);
    expect(result.burnSun).toBe(60_000 * 420);
    // 25.2 TRX × 0.5 USDT = 12.6 USDT
    expect(result.feeUsdt).toBe('12.60');
  });

  it('adds the bandwidth burn when the sender is a bare deposit address', async () => {
    const { service, resourceClient } = buildService({
      energyRequired: 29_650,
      hotWallet: { energyAvailable: 100_000, bandwidthAvailable: 600 },
      sender: { energyAvailable: 0, bandwidthAvailable: 0 },
    });

    const result = await service.estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '8.20',
      token: 'USDT',
    });

    // Energy fully covered by hot wallet; bandwidth shortfall burns TRX.
    expect(result.energyShortfall).toBe(0);
    expect(result.burnSun).toBe(ESTIMATED_TRANSFER_TX_BYTES * 1000);
    // 0.35 TRX × 0.5 = 0.175 → rounded UP to 0.18.
    expect(result.feeUsdt).toBe('0.18');
    // Bandwidth read against the deposit, energy against the hot wallet.
    expect(resourceClient.getAccountResources).toHaveBeenCalledWith(HOT_WALLET);
    expect(resourceClient.getAccountResources).toHaveBeenCalledWith(DEPOSIT);
  });

  it('rounds the USDT charge up, never down', async () => {
    // 1 energy shortfall × 420 sun = 0.00042 TRX × 0.5 = 0.00021 USDT → 0.01.
    const { service } = buildService(
      {
        energyRequired: 1,
        hotWallet: { energyAvailable: 0, bandwidthAvailable: 600 },
      },
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
  });
});
