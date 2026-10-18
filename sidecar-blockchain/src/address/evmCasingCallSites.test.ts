import { afterEach, describe, expect, it, vi } from 'vitest';
import TronWeb from 'tronweb';
import { TransferGuard, type OutflowHistoryRecord } from '../transfer/TransferGuard.js';
import { MonitorRegistry, type MonitorRegistryDeps } from '../monitor/MonitorRegistry.js';
import { FeeEstimationService } from '../fee/FeeEstimationService.js';
import type { TronResourceClient } from '../tron/TronResourceClient.js';
import type { TrxPriceService } from '../fee/TrxPriceService.js';
import { SidecarError } from '../errors/SidecarError.js';

/**
 * T150 (T145 validation F3) — every address comparison call site of this
 * service, driven with EVM spellings.
 *
 * <para>
 * The comparisons go through `canonicalAddress` / `sameAddress` (06 §1.4):
 * `0x` + hex compares case-insensitively, TRON base58 compares exactly. With
 * TRON-only fixtures a mutant that replaces `sameAddress(a, b)` with `a === b`
 * (or drops `canonicalAddress` from a map key) behaves identically, and the
 * T145 validation measured three such mutants surviving. Each test below feeds
 * one call site the same EVM address in two spellings (EIP-55 mixed case on
 * one side, lowercase on the other), so those mutants now fail, and pins the
 * other direction too: a TRON address whose letter case differs is a
 * DIFFERENT address and must not match.
 * </para>
 */

// One EVM address, three spellings (EIP-55 is the checksummed one).
const EVM_CHECKSUM = '0x9858EfFD232B4033E47d90003D41EC34EcaEda94';
const EVM_LOWER = EVM_CHECKSUM.toLowerCase();
const EVM_UPPER_HEX = '0x' + EVM_CHECKSUM.slice(2).toUpperCase();
const EVM_OTHER = '0x6Fac4D18c912343BF86fa7049364Dd4E424Ab9C0';

const TRON_HOT = 'TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44jE';
/** Same characters as TRON_HOT with one letter's case flipped — a different base58 string. */
const TRON_HOT_CASE_FLIPPED = 'TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44JE';
const UNIT = 1_000_000n;
const NOW = 1_700_000_000_000;

afterEach(() => {
  vi.restoreAllMocks();
});

describe('TransferGuard — sweep destination (sameAddress)', () => {
  it('accepts the configured EVM hot wallet in any letter case, refuses another address', () => {
    // The TRON service pins a TRON hot wallet at construction; the comparison
    // itself is network-agnostic and stays so (the Ethereum service shares the
    // rule). Let the constructor's TRON format check through for this case.
    vi.spyOn(TronWeb, 'isAddress').mockReturnValue(true);
    const guard = new TransferGuard({
      hotWalletAddress: EVM_CHECKSUM,
      coldWalletAddress: '',
      maxSingleTransferUnits: null,
      maxDailyOutflowUnits: null,
      tokenContracts: { USDT: '', USDC: '' },
      history: { listTrc20: async () => ({ records: [], fingerprint: null }) },
    });

    expect(() => guard.assertSweepDestination(EVM_LOWER)).not.toThrow();
    expect(() => guard.assertSweepDestination(EVM_UPPER_HEX)).not.toThrow();
    expect(() => guard.assertSweepDestination(EVM_OTHER)).toThrow(SidecarError);
  });

  it('refuses a TRON address whose letter case differs from the configured hot wallet', () => {
    const guard = new TransferGuard({
      hotWalletAddress: TRON_HOT,
      coldWalletAddress: '',
      maxSingleTransferUnits: null,
      maxDailyOutflowUnits: null,
      tokenContracts: { USDT: '', USDC: '' },
      history: { listTrc20: async () => ({ records: [], fingerprint: null }) },
    });

    expect(() => guard.assertSweepDestination(TRON_HOT)).not.toThrow();
    let caught: unknown;
    try {
      guard.assertSweepDestination(TRON_HOT_CASE_FLIPPED);
    } catch (err) {
      caught = err;
    }
    expect((caught as SidecarError).code).toBe('DESTINATION_NOT_ALLOWED');
  });
});

describe('TransferGuard — allowed contract set (canonicalAddress keys)', () => {
  function guardWithUsdt(usdtContract: string, records: OutflowHistoryRecord[]) {
    return new TransferGuard({
      hotWalletAddress: TRON_HOT,
      coldWalletAddress: '',
      maxSingleTransferUnits: 10_000n * UNIT,
      maxDailyOutflowUnits: 100n * UNIT,
      tokenContracts: { USDT: usdtContract, USDC: '' },
      history: { listTrc20: async () => ({ records, fingerprint: null }) },
      now: () => NOW,
    });
  }

  function outflow(contract: string): OutflowHistoryRecord {
    return {
      transaction_id: 'tx-' + contract,
      from: TRON_HOT,
      to: 'TGkh6US9LiJc1iovkYoAfTZpCGZtfM6nY5',
      value: String(90n * UNIT),
      block_timestamp: NOW - 60_000,
      token_info: { address: contract },
    };
  }

  it('counts a history record whose contract is the configured one in another EVM spelling', async () => {
    // 90 already spent + 20 requested > 100 limit — only if the record counts.
    // Neither spelling is the canonical (lowercase) one, so both the set and
    // the lookup have to fold the case for the record to count.
    const guard = guardWithUsdt(EVM_CHECKSUM, [outflow(EVM_UPPER_HEX)]);
    let caught: unknown;
    try {
      await guard.assertHotWalletDailyLimit(20n * UNIT);
    } catch (err) {
      caught = err;
    }
    expect((caught as SidecarError).code).toBe('DAILY_OUTFLOW_LIMIT_EXCEEDED');
  });

  it('does not count a TRON contract whose letter case differs (a different contract)', async () => {
    const usdt = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
    const flipped = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6T';
    const guard = guardWithUsdt(usdt, [outflow(flipped)]);
    await expect(guard.assertHotWalletDailyLimit(20n * UNIT)).resolves.toBeUndefined();
  });
});

describe('MonitorRegistry — monitor key (canonicalAddress)', () => {
  function registry(): MonitorRegistry {
    const client = {
      listTrc20Transactions: async () => ({ data: [], meta: {} }),
      getNowSolidBlock: async () => 0,
    } as unknown as MonitorRegistryDeps['client'];
    return new MonitorRegistry({
      client,
      allowlist: { USDT: EVM_OTHER, USDC: '' },
      intervalMs: 60_000,
      minConfirmations: 20,
      pageLimit: 20,
      webhookEndpoints: {
        paymentDetected: '/d',
        paymentConfirmed: '/c',
        wrongTokenIncoming: '/w',
        spamTokenIncoming: '/s',
      },
      webhookSender: async () => undefined,
    });
  }

  const start = (address: string) => ({
    address,
    paymentAddressId: 'pa',
    transactionId: 'tx',
    expectedContract: EVM_OTHER,
    expectedSymbol: 'USDT' as const,
  });

  it('treats two spellings of one EVM deposit as one monitor (start, cadenceOf, stop)', async () => {
    const monitors = registry();
    try {
      expect(monitors.start(start(EVM_CHECKSUM)).started).toBe(true);
      expect(monitors.start(start(EVM_LOWER)).started).toBe(false);
      expect(monitors.size()).toBe(1);
      expect(monitors.cadenceOf(EVM_UPPER_HEX)).toBe('PAYMENT');
      expect(monitors.stop(EVM_UPPER_HEX).stopped).toBe(true);
      expect(monitors.size()).toBe(0);
    } finally {
      await monitors.shutdown();
    }
  });

  it('keeps two TRON spellings that differ in letter case apart', async () => {
    const monitors = registry();
    try {
      monitors.start(start(TRON_HOT));
      expect(monitors.cadenceOf(TRON_HOT_CASE_FLIPPED)).toBeUndefined();
      expect(monitors.stop(TRON_HOT_CASE_FLIPPED).stopped).toBe(false);
      expect(monitors.size()).toBe(1);
    } finally {
      await monitors.shutdown();
    }
  });
});

describe('FeeEstimationService — hot wallet vs requested sender (sameAddress)', () => {
  function service(hotWalletAddress: string) {
    const anyAccount = {
      energyAvailable: 1_000_000,
      bandwidthAvailable: 5_000,
      stakedBandwidthAvailable: 0,
      energyPerTrx: 9.5,
    };
    const resourceClient = {
      estimateTransferEnergy: vi.fn(async () => 64_285),
      getAccountResources: vi.fn(async () => anyAccount),
      getAccountState: vi.fn(async () => ({ exists: true, balanceSun: 0 })),
      getDelegatableEnergySun: vi.fn(async () => 0),
      getChainFeeParameters: vi.fn(async () => ({
        energyFeeSun: 100,
        bandwidthFeeSun: 1000,
        createNewAccountFeeSun: 1_000_000,
        createAccountBandwidthFeeSun: 100_000,
        createNewAccountBandwidthRate: 1,
      })),
      getContractEnergyPolicy: vi.fn(async () => ({
        callerPercent: 100,
        originEnergyLimit: 0,
        originAddress: '',
      })),
    } as unknown as TronResourceClient;
    const priceService = {
      getPrice: vi.fn(async () => ({ priceUsdt: 0.3, source: 'binance' as const })),
    } as unknown as TrxPriceService;
    const fee = new FeeEstimationService({
      resourceClient,
      priceService,
      tokenContracts: { USDT: EVM_OTHER, USDC: '' },
      hotWalletAddress,
      delegationSource: { delegationOwner: hotWalletAddress } as never,
      tokenDecimals: 6,
    });
    return { fee, resourceClient };
  }

  it('prices a request whose sender is the hot wallet in another EVM spelling as the hot-wallet (payout) path', async () => {
    const { fee, resourceClient } = service(EVM_CHECKSUM);

    const result = await fee.estimate({
      fromAddress: EVM_LOWER,
      toAddress: EVM_OTHER,
      amount: '10.00',
      token: 'USDT',
    });

    // The delegated (deposit-sender) path plans a delegation and reads the
    // sender's own account state; the payout path does neither.
    expect(result.delegationPlan).toBeNull();
    expect(resourceClient.getAccountState).not.toHaveBeenCalled();
  });

  it('prices a sender that differs from the hot wallet as the delegated (refund) path', async () => {
    const { fee, resourceClient } = service(EVM_CHECKSUM);

    const result = await fee.estimate({
      fromAddress: EVM_OTHER,
      toAddress: EVM_LOWER,
      amount: '10.00',
      token: 'USDT',
    });

    expect(result.delegationPlan).not.toBeNull();
    expect(resourceClient.getAccountState).toHaveBeenCalledWith(EVM_OTHER);
  });
});
