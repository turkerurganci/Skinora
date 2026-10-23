import { describe, expect, it } from 'vitest';
import { Interface } from 'ethers';
import { EthFeeEstimationService, type FeeRpc } from './FeeEstimationService.js';
import { RpcError, AllProvidersFailedError, type FeeHistory } from '../rpc/RpcClient.js';
import { SidecarError } from '../errors/SidecarError.js';
import type { EthPriceQuote } from '../price/EthPriceService.js';
import { MAINNET_TOKEN_CONTRACTS } from '../network.js';

const HOT = '0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed';
const DEPOSIT = '0x9858EfFD232B4033E47d90003D41EC34EcaEda94';
const BUYER = '0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359';
const LATEST = 26_215_000n;
const GWEI = 1_000_000_000n;

const PRICE: EthPriceQuote = {
  priceMicroUsdt: 2_364_480_000n,
  priceUsdt: '2364.480000',
  source: 'binance',
  ageSeconds: 0,
};

interface Seen {
  estimateGas: Array<{
    tx: { from: string; to: string; data: string; value?: bigint };
    block: bigint;
  }>;
  feeHistory: Array<{ count: number; newest: bigint; percentiles: readonly number[] }>;
  balances: Array<{ address: string; block: bigint }>;
}

function rpc(
  opts: {
    gas?: bigint | Error;
    /** T159a — the fuel probe's answer (a plain ETH send); mainnet: 21,000. */
    fuelGas?: bigint | Error;
    history?: FeeHistory | Error;
    /** T169a — the deposit's own ETH (an earlier fuel's remainder); default 0. */
    depositBalance?: bigint | Error;
  } = {},
): FeeRpc & { seen: Seen } {
  const seen: Seen = { estimateGas: [], feeHistory: [], balances: [] };
  return {
    seen,
    async getBlockHeader() {
      return { number: LATEST, hash: '0xabc', timestamp: 0, provider: 'alchemy' };
    },
    async getBalance(address, block) {
      seen.balances.push({ address, block });
      const balance = opts.depositBalance ?? 0n;
      if (balance instanceof Error) throw balance;
      return balance;
    },
    async estimateGas(tx, block) {
      seen.estimateGas.push({ tx, block });
      const gas = tx.data === '0x' ? (opts.fuelGas ?? 21_000n) : (opts.gas ?? 46_201n);
      if (gas instanceof Error) throw gas;
      return gas;
    },
    async feeHistory(count, newest, percentiles) {
      seen.feeHistory.push({ count, newest, percentiles });
      const h =
        opts.history ??
        // Five blocks' tips 0.03 / 0.05 / 0.05 / 0.2 / 0.04 gwei; next base fee 0.167 gwei.
        ({
          baseFeePerGas: [
            150_000_000n,
            160_000_000n,
            170_000_000n,
            165_000_000n,
            160_000_000n,
            167_000_000n,
          ],
          reward: [[30_000_000n], [50_000_000n], [50_000_000n], [200_000_000n], [40_000_000n]],
        } satisfies FeeHistory);
      if (h instanceof Error) throw h;
      return h;
    },
  };
}

const FUEL = '0x7e5f4552091a69125d5dfcb7b8c2659029395bdf';

function service(
  r: FeeRpc,
  price: EthPriceQuote | Error = PRICE,
  hot: string | null = HOT,
  fuel: string | null = FUEL,
) {
  return new EthFeeEstimationService({
    rpc: r,
    prices: {
      async getPrice() {
        if (price instanceof Error) throw price;
        return price;
      },
    },
    tokenContracts: { ...MAINNET_TOKEN_CONTRACTS },
    hotWalletAddress: hot,
    fuelWalletAddress: fuel,
  });
}

const ERC20 = new Interface(['function transfer(address to, uint256 amount)']);

describe('EthFeeEstimationService — the exact transfer is simulated', () => {
  it('estimates transfer(to, amount) on the token contract from the sender, at the latest block', async () => {
    const r = rpc();
    await service(r).estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '10.2',
      token: 'USDT',
    });
    const [call] = r.seen.estimateGas;
    expect(call.block).toBe(LATEST);
    expect(call.tx.from).toBe(DEPOSIT);
    expect(call.tx.to).toBe(MAINNET_TOKEN_CONTRACTS.USDT);
    const [to, amount] = ERC20.decodeFunctionData('transfer', call.tx.data);
    expect(String(to).toLowerCase()).toBe(BUYER);
    expect(amount).toBe(10_200_000n); // 6 decimals
    expect(r.seen.feeHistory).toEqual([{ count: 20, newest: LATEST, percentiles: [50] }]);
  });

  it('prices a USDC transfer on the USDC contract (wrong-token refund)', async () => {
    const r = rpc();
    await service(r).estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '5',
      token: 'USDC',
    });
    expect(r.seen.estimateGas[0].tx.to).toBe(MAINNET_TOKEN_CONTRACTS.USDC);
  });

  it('omitted sender = hot wallet', async () => {
    const r = rpc();
    await service(r).estimate({ toAddress: BUYER, amount: '1234.567891', token: 'USDT' });
    expect(r.seen.estimateGas[0].tx.from).toBe(HOT);
  });
});

describe('EthFeeEstimationService — cost model (08 §3.7.5, §3.7.7)', () => {
  it('hot wallet sender: gas × (next base fee + median tip)', async () => {
    const result = await service(rpc({ gas: 64_031n })).estimate({
      toAddress: BUYER,
      amount: '20',
      token: 'USDT',
    });
    // tip = median(0.03, 0.05, 0.05, 0.2, 0.04) = 0.05 gwei; base = 0.167 gwei.
    const wei = 64_031n * (167_000_000n + 50_000_000n);
    expect(result).toMatchObject({
      network: 'ETHEREUM',
      sender: 'HOT_WALLET',
      feeWei: wei.toString(),
      gasLimit: '64031',
      fuelGasLimit: '0',
      baseFeePerGasWei: '167000000',
      maxPriorityFeePerGasWei: '50000000',
      maxFeePerGasWei: '384000000',
      blockNumber: '26215000',
      ethPriceUsdt: '2364.480000',
      priceSource: 'binance',
      priceAgeSeconds: 0,
    });
    // 13,894,727,000,000 wei × 2,364.48 = 0.0328… $ → rounded up.
    expect(result.feeUsdt).toBe('0.04');
  });

  it('deposit sender: the whole fuel (the signer’s gas limit × (2 × base + tip)) + the fuel send’s estimated gas at the expected price', async () => {
    const result = await service(rpc()).estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '10.2',
      token: 'USDT',
    });
    // T159a: the fuel is sized from the gas LIMIT the signer uses (46,201 + 10 %),
    // and the fuel send's gas is its own estimate (21,000 on mainnet).
    const wei = 50_822n * 384_000_000n + 21_000n * 217_000_000n;
    expect(result.sender).toBe('DEPOSIT');
    expect(result.gasLimit).toBe('50822');
    expect(result.fuelGasLimit).toBe('21000');
    expect(result.feeWei).toBe(wei.toString());
    expect(result.feeUsdt).toBe('0.06'); // 0.0569… $ rounded up
  });

  it('T159a — Sepolia tariff: the fuel’s gas is ESTIMATED (207,391 to a new account), a first receipt 153,000 → the deduction prices both', async () => {
    const r = rpc({ gas: 153_000n, fuelGas: 207_391n });
    const result = await service(r).estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '10.2',
      token: 'USDT',
    });
    const probe = r.seen.estimateGas.find((c) => c.tx.data === '0x')!;
    // A plain ETH send of 1 wei from the fuel wallet to this deposit, at the same block.
    expect(probe.tx.from).toBe(FUEL);
    expect(probe.tx.to).toBe(DEPOSIT);
    expect(probe.tx.value).toBe(1n);
    expect(probe.block).toBe(LATEST);
    expect(result.gasLimit).toBe('168300');
    expect(result.fuelGasLimit).toBe('207391');
    expect(result.feeWei).toBe((168_300n * 384_000_000n + 207_391n * 217_000_000n).toString());
    expect(result.feeUsdt).toBe('0.26');
  });

  it('no fuel wallet configured: the fuel probe is sent from the hot wallet (a plain send’s gas depends on the recipient only)', async () => {
    const r = rpc();
    await service(r, PRICE, HOT, null).estimate({
      fromAddress: DEPOSIT,
      toAddress: BUYER,
      amount: '1',
      token: 'USDT',
    });
    expect(r.seen.estimateGas.find((c) => c.tx.data === '0x')!.tx.from).toBe(HOT);
  });

  it('a hot-wallet transfer makes no fuel probe', async () => {
    const r = rpc();
    await service(r).estimate({ toAddress: BUYER, amount: '1', token: 'USDT' });
    expect(r.seen.estimateGas.filter((c) => c.tx.data === '0x')).toHaveLength(0);
  });

  it('T159a — the tip is the signer’s: the upper middle of an even list (not the lower)', async () => {
    const result = await service(
      rpc({
        history: {
          baseFeePerGas: [1n, 100_000_000n],
          reward: [[10_000_000n], [20_000_000n], [30_000_000n], [40_000_000n]],
        },
      }),
    ).estimate({ toAddress: BUYER, amount: '1', token: 'USDT' });
    expect(result.maxPriorityFeePerGasWei).toBe('30000000');
  });

  it('a deposit address equal to the hot wallet (any casing) is priced as the hot wallet', async () => {
    const result = await service(rpc()).estimate({
      fromAddress: '0x5AAEB6053F3E94C9B9A09F33669435E7EF1BEAED',
      toAddress: BUYER,
      amount: '1',
      token: 'USDT',
    });
    expect(result.sender).toBe('HOT_WALLET');
  });

  it('a crash-day price per gas (360 gwei base) is priced exactly, not capped here', async () => {
    const result = await service(
      rpc({ history: { baseFeePerGas: [360n * GWEI], reward: [[2n * GWEI]] } }),
      { ...PRICE, priceMicroUsdt: 3_950_000_000n, priceUsdt: '3950.000000' },
    ).estimate({ fromAddress: DEPOSIT, toAddress: BUYER, amount: '1', token: 'USDT' });
    const wei = 50_822n * 722n * GWEI + 21_000n * 362n * GWEI;
    expect(result.feeWei).toBe(wei.toString());
    // 0.044295… ETH × 3950 = 174.96… $ — the backend's 10 USDT ceiling refuses it, not this service.
    expect(result.feeUsdt).toBe('174.97');
  });

  it('no tip data (empty reward) → the signer’s 0.01 gwei floor (T159a; was 0 — below what the signer pays)', async () => {
    const result = await service(
      rpc({ history: { baseFeePerGas: [1n, 100_000_000n], reward: [] } }),
    ).estimate({ toAddress: BUYER, amount: '1', token: 'USDT' });
    expect(result.maxPriorityFeePerGasWei).toBe('10000000');
    expect(result.feeWei).toBe((46_201n * 110_000_000n).toString());
  });

  it('every integer leaves as a decimal string — none as a JS number', async () => {
    const result = await service(rpc()).estimate({ toAddress: BUYER, amount: '1', token: 'USDT' });
    for (const key of [
      'feeUsdt',
      'feeWei',
      'blockNumber',
      'gasLimit',
      'fuelGasLimit',
      'baseFeePerGasWei',
      'maxPriorityFeePerGasWei',
      'maxFeePerGasWei',
      'ethPriceUsdt',
    ] as const) {
      expect(typeof result[key]).toBe('string');
    }
  });
});

/**
 * T169a (proje sahibi kararı 2026-10-21 — ⚠ PARA) — a refund from a deposit is
 * charged what the platform sends for it: the fuel the signer sends (send needs
 * − the deposit's own ETH, `TransferService.sendFuel`) plus that fuel's own fee;
 * no fuel, no charge (`platformSpendWei` "0" — T160c F1). Literals by hand.
 */
describe('EthFeeEstimationService — T169a the fuel actually sent', () => {
  /** T159b's ETH price (T160c F1 table) — not the PRICE fixture above. */
  const T159B_ETH: EthPriceQuote = {
    priceMicroUsdt: 2_444_861_904n,
    priceUsdt: '2444.861904',
    source: 'binance',
    ageSeconds: 0,
  };
  /** Sepolia-like block: base 0.5 gwei, tips below the 0.01 gwei floor. */
  const SEPOLIA_HISTORY: FeeHistory = { baseFeePerGas: [500_000_000n], reward: [[1_000_000n]] };
  const refund = { fromAddress: DEPOSIT, toAddress: BUYER, amount: '10.2', token: 'USDT' as const };

  it("subtracts the deposit's own ETH from the fuel and keeps the fuel's own fee", async () => {
    // Budget 50,822 × 0.384 gwei = 19,515,648,000,000; the deposit holds
    // 10,000,000,000,000 → fuel 9,515,648,000,000 + 21,000 × 0.217 gwei
    // (4,557,000,000,000) = 14,072,648,000,000 wei × 2,364.48 = 0.03327… → 0.04
    // (with an empty deposit: 24,072,648,000,000 → 0.06).
    const r = rpc({ depositBalance: 10_000_000_000_000n });

    const result = await service(r).estimate(refund);

    expect(r.seen.balances).toEqual([{ address: DEPOSIT, block: LATEST }]);
    expect(result.depositBalanceWei).toBe('10000000000000');
    expect(result.feeWei).toBe('14072648000000');
    expect(result.feeUsdt).toBe('0.04');
  });

  it('charges nothing when the deposit already holds the whole budget — the signer sends no fuel', async () => {
    const r = rpc({ depositBalance: 19_515_648_000_000n });

    const result = await service(r).estimate(refund);

    expect(result.feeWei).toBe('0');
    expect(result.feeUsdt).toBe('0.00');
  });

  it('one wei short of the budget: a one-wei fuel and its whole fee', async () => {
    const r = rpc({ depositBalance: 19_515_647_999_999n });

    const result = await service(r).estimate(refund);

    expect(result.feeWei).toBe((1n + 4_557_000_000_000n).toString());
  });

  it("T159b pair — the second refund from one Sepolia deposit rides on the first fuel's remainder: 0.00, was 0.25", async () => {
    // First refund's fuel: 168,300 (153,000 + 10 %) × 1.01 gwei = 169,983,000,000,000
    // wei; it used 152,417 gas at 0.51 gwei = 77,732,670,000,000 → the deposit keeps
    // 92,250,330,000,000. The second refund (recipient now holds the token,
    // 80,000 gas → limit 88,000, budget 88,880,000,000,000) needs no fuel.
    // Before T169a: 88,880,000,000,000 + 21,000 × 0.51 gwei = 99,590,000,000,000
    // wei × 2,444.861904 = 0.24348… → 0.25 for ETH nobody sent.
    const r = rpc({
      gas: 80_000n,
      fuelGas: 21_000n,
      history: SEPOLIA_HISTORY,
      depositBalance: 92_250_330_000_000n,
    });

    const result = await service(r, T159B_ETH).estimate(refund);

    expect(result.gasLimit).toBe('88000');
    expect(result.maxFeePerGasWei).toBe('1010000000');
    expect(result.feeWei).toBe('0');
    expect(result.feeUsdt).toBe('0.00');
  });

  it('Sepolia first refund from an empty deposit: fuel + its fee, exactly as before T169a', async () => {
    // 168,300 × 1.01 gwei + 207,391 × 0.51 gwei = 169,983,000,000,000 +
    // 105,769,410,000,000 = 275,752,410,000,000 wei × 2,444.861904 = 0.67416… → 0.68.
    const r = rpc({ gas: 153_000n, fuelGas: 207_391n, history: SEPOLIA_HISTORY });

    const result = await service(r, T159B_ETH).estimate(refund);

    expect(result.depositBalanceWei).toBe('0');
    expect(result.feeWei).toBe('275752410000000');
    expect(result.feeUsdt).toBe('0.68');
  });

  it('the hot wallet path reads no balance and is unchanged', async () => {
    const r = rpc({ gas: 64_031n, depositBalance: 10n ** 18n });

    const result = await service(r).estimate({ toAddress: BUYER, amount: '20', token: 'USDT' });

    expect(r.seen.balances).toEqual([]);
    expect(result.depositBalanceWei).toBe('0');
    expect(result.feeWei).toBe((64_031n * 217_000_000n).toString());
  });

  it('an unreadable deposit balance fails the estimate — never a guessed (or zero) fuel', async () => {
    const r = rpc({ depositBalance: new AllProvidersFailedError('eth_getBalance', []) });

    await expect(service(r).estimate(refund)).rejects.toSatisfy(
      (e: unknown) =>
        e instanceof SidecarError && e.code === 'RPC_ALL_PROVIDERS_FAILED' && e.retryable,
    );
  });
});

describe('EthFeeEstimationService — failures', () => {
  async function failsWith(p: Promise<unknown>, code: string, retryable: boolean) {
    await expect(p).rejects.toSatisfy(
      (e: unknown) => e instanceof SidecarError && e.code === code && e.retryable === retryable,
    );
  }

  it('a reverted simulation is FEE_ESTIMATE_SIMULATION_FAILED (retryable, like TRON)', async () => {
    const r = rpc({
      gas: new RpcError('eth_estimateGas: execution reverted', 'rpc', 'alchemy', 3),
    });
    await failsWith(
      service(r).estimate({ fromAddress: DEPOSIT, toAddress: BUYER, amount: '1', token: 'USDT' }),
      'FEE_ESTIMATE_SIMULATION_FAILED',
      true,
    );
  });

  it('zero gas from eth_estimateGas is refused as a failed simulation — never a 0-fee estimate', async () => {
    await failsWith(
      service(rpc({ gas: 0n })).estimate({ toAddress: BUYER, amount: '1', token: 'USDT' }),
      'FEE_ESTIMATE_SIMULATION_FAILED',
      true,
    );
  });

  it('T159a — the fuel probe failing is a failed simulation too (the backend charges its fallback)', async () => {
    const r = rpc({ fuelGas: new RpcError('eth_estimateGas: block not found', 'rpc', 'alchemy') });
    await failsWith(
      service(r).estimate({ fromAddress: DEPOSIT, toAddress: BUYER, amount: '1', token: 'USDT' }),
      'FEE_ESTIMATE_SIMULATION_FAILED',
      true,
    );
  });

  it.each([
    ['token', { gas: 1_000_001n }],
    ['token below 21,000', { gas: 20_999n }],
    ['fuel', { fuelGas: 1_000_001n }],
  ])(
    'T159a — an implausible %s estimate is not believed: GAS_ESTIMATE_ABNORMAL (retryable)',
    async (_label, opts) => {
      await failsWith(
        service(rpc(opts)).estimate({
          fromAddress: DEPOSIT,
          toAddress: BUYER,
          amount: '1',
          token: 'USDT',
        }),
        'GAS_ESTIMATE_ABNORMAL',
        true,
      );
    },
  );

  it('both providers down propagates as RPC_ALL_PROVIDERS_FAILED (retryable)', async () => {
    const r = rpc({ history: new AllProvidersFailedError('eth_feeHistory', []) });
    await failsWith(
      service(r).estimate({ toAddress: BUYER, amount: '1', token: 'USDT' }),
      'RPC_ALL_PROVIDERS_FAILED',
      true,
    );
  });

  it('no price → ETH_PRICE_UNAVAILABLE propagates; no estimate at a guessed price', async () => {
    await failsWith(
      service(rpc(), new SidecarError('x', 'ETH_PRICE_UNAVAILABLE', true)).estimate({
        toAddress: BUYER,
        amount: '1',
        token: 'USDT',
      }),
      'ETH_PRICE_UNAVAILABLE',
      true,
    );
  });

  it('hot wallet path with no hot wallet configured is refused', async () => {
    await failsWith(
      service(rpc(), PRICE, null).estimate({ toAddress: BUYER, amount: '1', token: 'USDT' }),
      'HOT_WALLET_NOT_CONFIGURED',
      false,
    );
  });

  it('a token with no contract on this chain is refused', async () => {
    const s = new EthFeeEstimationService({
      rpc: rpc(),
      prices: { getPrice: async () => PRICE },
      tokenContracts: { USDT: '', USDC: MAINNET_TOKEN_CONTRACTS.USDC },
      hotWalletAddress: HOT,
    });
    await failsWith(
      s.estimate({ toAddress: BUYER, amount: '1', token: 'USDT' }),
      'TOKEN_CONTRACT_NOT_CONFIGURED',
      false,
    );
  });
});
