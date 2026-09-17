import { describe, it, expect, vi } from 'vitest';
import { TransferService } from './TransferService.js';
import { RefundService } from './RefundService.js';
import { TransferGuard, OutflowHistoryPage, OutflowHistoryRecord } from './TransferGuard.js';
import { SidecarError } from '../errors/SidecarError.js';

const SIGNER_HOT_KEY = '01'.padStart(64, '0');
const TOKEN_USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const TOKEN_USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
// Real base58check addresses: TransferGuard refuses to construct on a
// malformed pinned address, so the placeholders this suite used before
// ('THotWallet') can no longer stand in for one.
const HOT_WALLET = 'TMmY2ARUpirKFwuW8HMGDuEkBWZZjK44jE';
const COLD_WALLET = 'TGpQ6KteKAbJDu7zZuoRnUvUTxvjKG4tv5';
const OTHER_ADDRESS = 'TGkh6US9LiJc1iovkYoAfTZpCGZtfM6nY5';
const UNIT = 1_000_000n;
// dummy signing material for the stub wallet — never reaches a chain
const DUMMY_SIGNER = 'ab'.padStart(64, 'a');

interface GuardOptions {
  hotWalletAddress?: string;
  coldWalletAddress?: string;
  maxSingleTransferUnits?: bigint | null;
  maxDailyOutflowUnits?: bigint | null;
  history?: OutflowHistoryRecord[];
  now?: number;
}

/**
 * Guard with limits wide enough that the pre-existing expectations of this
 * suite keep measuring what they measured before; the limit-specific cases
 * narrow them explicitly.
 */
function buildGuard(options: GuardOptions = {}): TransferGuard {
  const records = options.history ?? [];
  return new TransferGuard({
    hotWalletAddress: options.hotWalletAddress ?? HOT_WALLET,
    coldWalletAddress: options.coldWalletAddress ?? COLD_WALLET,
    maxSingleTransferUnits:
      options.maxSingleTransferUnits === undefined
        ? 10_000n * UNIT
        : options.maxSingleTransferUnits,
    maxDailyOutflowUnits:
      options.maxDailyOutflowUnits === undefined ? 100_000n * UNIT : options.maxDailyOutflowUnits,
    tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
    history: {
      listTrc20: vi.fn(async (): Promise<OutflowHistoryPage> => ({ records, fingerprint: null })),
    },
    now: () => options.now ?? 1_700_000_000_000,
  });
}

function buildStubClient() {
  return {
    sendTransfer: vi.fn(async () => ({ txHash: 'tx-fake' })),
    getTransactionStatus: vi.fn(async () => ({
      txHash: 'tx-fake',
      blockNumber: 100,
      confirmations: 30,
      contractRet: 'SUCCESS',
    })),
  };
}

function buildStubWallet(map: Record<number, { address: string; privateKey: string }>) {
  return {
    deriveSigner: vi.fn((index: number) => {
      const m = map[index];
      if (!m) throw new Error(`No stub signer for index ${index}`);
      return {
        address: m.address,
        derivationPath: `m/.../${index}`,
        index,
        privateKey: m.privateKey,
      };
    }),
  };
}

/**
 * Build a stub EnergyDelegationService that simply forwards <c>action</c>
 * without touching the on-chain client. Tests that exercise specific
 * delegation paths (fallback, undelegate failure) override
 * <c>withDelegation</c> directly via vi.fn().
 *
 * <para>
 * Return type is inferred: <c>vi.fn</c> erases the generic parameter on the
 * mocked implementation, so we cannot annotate the return as
 * <c>Pick&lt;EnergyDelegationService, 'withDelegation'&gt;</c> (TS2322 — the
 * generic implementation does not satisfy the generic interface signature
 * after the mock wrap). Callers cast via <c>as any</c> at the injection
 * site, which is the established stub pattern across this test suite.
 * </para>
 */
function buildStubDelegation(
  mode: 'delegated' | 'fallback' = 'delegated',
  options: { delegationAmountSun?: number; fallbackAmountSun?: number } = {},
) {
  return {
    withDelegation: vi.fn(
      async (
        _transfer: { depositAddress: string },
        action: () => Promise<unknown>,
        _context: { blockchainTransactionId: string; correlationId: string },
      ) => {
        const result = await action();
        return {
          mode,
          delegationAmountSun:
            mode === 'delegated' ? (options.delegationAmountSun ?? 200_000_000) : 0,
          fallbackAmountSun: mode === 'fallback' ? (options.fallbackAmountSun ?? 15_000_000) : 0,
          action: result,
        };
      },
    ),
  };
}

describe('TransferService.toRawUnits()', () => {
  const power = 10n ** 6n;
  it('handles integer amounts', () => {
    expect(TransferService.toRawUnits('100', power)).toBe('100000000');
  });
  it('handles fractional amounts up to 6 digits', () => {
    expect(TransferService.toRawUnits('100.5', power)).toBe('100500000');
    expect(TransferService.toRawUnits('0.000001', power)).toBe('1');
  });
  it('rejects amounts with too many fractional digits', () => {
    expect(() => TransferService.toRawUnits('1.0000001', power)).toThrow(SidecarError);
  });
  it('rejects negative or malformed amounts', () => {
    expect(() => TransferService.toRawUnits('-1', power)).toThrow(SidecarError);
    expect(() => TransferService.toRawUnits('abc', power)).toThrow(SidecarError);
    expect(() => TransferService.toRawUnits('', power)).toThrow(SidecarError);
  });
});

describe('TransferService.payout()', () => {
  it('broadcasts from hot wallet with the USDT contract', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: SIGNER_HOT_KEY,
    });

    const result = await service.payout({
      blockchainTransactionId: 'bx-1',
      toAddress: 'TSellerAddress',
      amount: '50.25',
      token: 'USDT',
      correlationId: 'corr-1',
    });

    expect(result.txHash).toBe('tx-fake');
    expect(client.sendTransfer).toHaveBeenCalledWith({
      fromAddress: HOT_WALLET,
      privateKey: SIGNER_HOT_KEY,
      contractAddress: TOKEN_USDT,
      toAddress: 'TSellerAddress',
      amountUnits: '50250000',
    });
  });

  it('refuses to broadcast when hot wallet private key is unset', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: '',
    });

    await expect(
      service.payout({
        blockchainTransactionId: 'bx-1',
        toAddress: 'TSeller',
        amount: '10',
        token: 'USDT',
        correlationId: 'corr-1',
      }),
    ).rejects.toMatchObject({ code: 'HOT_WALLET_NOT_CONFIGURED', retryable: false });
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });
});

describe('TransferService.coldWalletTransfer()', () => {
  it('broadcasts from hot wallet to admin-supplied cold address with the requested token', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: SIGNER_HOT_KEY,
    });

    const result = await service.coldWalletTransfer({
      coldTransferId: 'cwt-77',
      toColdAddress: COLD_WALLET,
      amount: '10000',
      token: 'USDC',
      correlationId: 'corr-cwt',
    });

    expect(result.txHash).toBe('tx-fake');
    expect(client.sendTransfer).toHaveBeenCalledWith({
      fromAddress: HOT_WALLET,
      privateKey: SIGNER_HOT_KEY,
      contractAddress: TOKEN_USDC,
      toAddress: COLD_WALLET,
      amountUnits: '10000000000',
    });
  });

  it('refuses to broadcast when hot wallet credentials are unset', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: '',
      hotWalletPrivateKey: '',
    });

    await expect(
      service.coldWalletTransfer({
        coldTransferId: 'cwt-77',
        toColdAddress: COLD_WALLET,
        amount: '100',
        token: 'USDT',
        correlationId: 'corr-cwt',
      }),
    ).rejects.toMatchObject({ code: 'HOT_WALLET_NOT_CONFIGURED', retryable: false });
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });

  it('rejects fractional amounts beyond 6 decimals', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: SIGNER_HOT_KEY,
    });

    await expect(
      service.coldWalletTransfer({
        coldTransferId: 'cwt-77',
        toColdAddress: COLD_WALLET,
        amount: '1.0000001',
        token: 'USDT',
        correlationId: 'corr-cwt',
      }),
    ).rejects.toMatchObject({ code: 'INVALID_TRANSFER_AMOUNT' });
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });
});

describe('TransferService.sweep()', () => {
  it('derives signer, delegates Energy, broadcasts deposit -> hot wallet and reports delegated mode', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      7: { address: 'TDepositAddress7', privateKey: 'ab'.padStart(64, 'a') },
    });
    const delegation = buildStubDelegation('delegated');
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: SIGNER_HOT_KEY,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    const result = await service.sweep({
      blockchainTransactionId: 'bx-sweep-1',
      depositIndex: 7,
      depositAddress: 'TDepositAddress7',
      toHotWalletAddress: HOT_WALLET,
      amount: '100',
      token: 'USDC',
      correlationId: 'corr-sweep-1',
    });

    expect(wallet.deriveSigner).toHaveBeenCalledWith(7);
    // The spec the delegation flow simulates must be the transfer that is
    // broadcast — same sender, contract, recipient and raw amount.
    expect(delegation.withDelegation).toHaveBeenCalledWith(
      {
        depositAddress: 'TDepositAddress7',
        contractAddress: TOKEN_USDC,
        toAddress: HOT_WALLET,
        amountUnits: '100000000',
      },
      expect.any(Function),
      expect.objectContaining({
        blockchainTransactionId: 'bx-sweep-1',
        correlationId: 'corr-sweep-1',
      }),
    );
    expect(client.sendTransfer).toHaveBeenCalledWith(
      expect.objectContaining({
        fromAddress: 'TDepositAddress7',
        contractAddress: TOKEN_USDC,
        toAddress: HOT_WALLET,
        amountUnits: '100000000',
      }),
    );
    expect(result).toEqual({
      txHash: 'tx-fake',
      delegationMode: 'delegated',
      delegationAmountSun: 200_000_000,
      fallbackAmountSun: 0,
    });
  });

  it('reports fallback mode when delegation falls back to TRX prefund', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      9: { address: 'TDepositFB9', privateKey: 'aa'.padStart(64, 'a') },
    });
    const delegation = buildStubDelegation('fallback');
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: SIGNER_HOT_KEY,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    const result = await service.sweep({
      blockchainTransactionId: 'bx-sweep-fb',
      depositIndex: 9,
      depositAddress: 'TDepositFB9',
      toHotWalletAddress: HOT_WALLET,
      amount: '50',
      token: 'USDT',
      correlationId: 'corr-fb',
    });

    expect(result).toEqual({
      txHash: 'tx-fake',
      delegationMode: 'fallback',
      delegationAmountSun: 0,
      fallbackAmountSun: 15_000_000,
    });
  });

  it('propagates broadcast errors raised inside the delegation envelope', async () => {
    const client = buildStubClient();
    client.sendTransfer.mockRejectedValueOnce(
      new SidecarError('rejected', 'TRANSFER_BROADCAST_REJECTED', true),
    );
    const wallet = buildStubWallet({
      7: { address: 'TDepositAddress7', privateKey: 'ab'.padStart(64, 'a') },
    });
    const delegation = buildStubDelegation('delegated');
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: SIGNER_HOT_KEY,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    await expect(
      service.sweep({
        blockchainTransactionId: 'bx-sweep-err',
        depositIndex: 7,
        depositAddress: 'TDepositAddress7',
        toHotWalletAddress: HOT_WALLET,
        amount: '100',
        token: 'USDT',
        correlationId: 'corr-err',
      }),
    ).rejects.toMatchObject({ code: 'TRANSFER_BROADCAST_REJECTED' });
    expect(delegation.withDelegation).toHaveBeenCalled();
  });

  it('rejects DELEGATION_NOT_WIRED when energy delegation service is not injected', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      7: { address: 'TDepositAddress7', privateKey: 'ab'.padStart(64, 'a') },
    });
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: SIGNER_HOT_KEY,
    });

    await expect(
      service.sweep({
        blockchainTransactionId: 'bx-sweep-1',
        depositIndex: 7,
        depositAddress: 'TDepositAddress7',
        toHotWalletAddress: HOT_WALLET,
        amount: '100',
        token: 'USDT',
        correlationId: 'corr-sweep-1',
      }),
    ).rejects.toMatchObject({ code: 'DELEGATION_NOT_WIRED', retryable: false });
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });

  it('rejects DEPOSIT_ADDRESS_MISMATCH when derived address diverges from caller-supplied', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      7: { address: 'TActuallyOther', privateKey: SIGNER_HOT_KEY },
    });
    const delegation = buildStubDelegation('delegated');
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: SIGNER_HOT_KEY,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    await expect(
      service.sweep({
        blockchainTransactionId: 'bx-sweep-1',
        depositIndex: 7,
        depositAddress: 'TDepositAddress7',
        toHotWalletAddress: HOT_WALLET,
        amount: '100',
        token: 'USDT',
        correlationId: 'corr-sweep-1',
      }),
    ).rejects.toMatchObject({ code: 'DEPOSIT_ADDRESS_MISMATCH', retryable: false });
    expect(client.sendTransfer).not.toHaveBeenCalled();
    expect(delegation.withDelegation).not.toHaveBeenCalled();
  });
});

describe('RefundService.refund()', () => {
  it('broadcasts deposit -> buyer source with the correct token contract and reports delegation mode', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      11: { address: 'TDeposit11', privateKey: 'cd'.padStart(64, 'c') },
    });
    const delegation = buildStubDelegation('delegated');
    const service = new RefundService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    const result = await service.refund({
      blockchainTransactionId: 'bx-refund-1',
      depositIndex: 11,
      depositAddress: 'TDeposit11',
      toBuyerAddress: 'TBuyerSource',
      amount: '95.5',
      token: 'USDT',
      correlationId: 'corr-refund-1',
    });

    expect(wallet.deriveSigner).toHaveBeenCalledWith(11);
    expect(delegation.withDelegation).toHaveBeenCalledWith(
      {
        depositAddress: 'TDeposit11',
        contractAddress: TOKEN_USDT,
        toAddress: 'TBuyerSource',
        amountUnits: '95500000',
      },
      expect.any(Function),
      expect.objectContaining({
        blockchainTransactionId: 'bx-refund-1',
        correlationId: 'corr-refund-1',
      }),
    );
    expect(client.sendTransfer).toHaveBeenCalledWith(
      expect.objectContaining({
        fromAddress: 'TDeposit11',
        contractAddress: TOKEN_USDT,
        toAddress: 'TBuyerSource',
        amountUnits: '95500000',
      }),
    );
    expect(result).toEqual({
      txHash: 'tx-fake',
      delegationMode: 'delegated',
      delegationAmountSun: 200_000_000,
      fallbackAmountSun: 0,
    });
  });

  it('reports fallback mode when delegation degrades to TRX prefund', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      12: { address: 'TDeposit12', privateKey: 'cd'.padStart(64, 'c') },
    });
    const delegation = buildStubDelegation('fallback');
    const service = new RefundService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    const result = await service.refund({
      blockchainTransactionId: 'bx-refund-fb',
      depositIndex: 12,
      depositAddress: 'TDeposit12',
      toBuyerAddress: 'TBuyerSource',
      amount: '50',
      token: 'USDC',
      correlationId: 'corr-refund-fb',
    });

    expect(result.delegationMode).toBe('fallback');
    expect(result.fallbackAmountSun).toBe(15_000_000);
    expect(result.delegationAmountSun).toBe(0);
  });

  it('rejects DELEGATION_NOT_WIRED when energy delegation service is not injected', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      11: { address: 'TDeposit11', privateKey: 'cd'.padStart(64, 'c') },
    });
    const service = new RefundService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
    });

    await expect(
      service.refund({
        blockchainTransactionId: 'bx-refund-1',
        depositIndex: 11,
        depositAddress: 'TDeposit11',
        toBuyerAddress: 'TBuyerSource',
        amount: '95.5',
        token: 'USDT',
        correlationId: 'corr-refund-1',
      }),
    ).rejects.toMatchObject({ code: 'DELEGATION_NOT_WIRED', retryable: false });
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });
});

describe('destination pinning and amount limits (05 §3.3)', () => {
  function buildSweepService(guard: TransferGuard) {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      7: { address: 'TDepositAddress7', privateKey: DUMMY_SIGNER },
    });
    const delegation = buildStubDelegation('delegated');
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard,
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: DUMMY_SIGNER,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });
    return { service, client, wallet, delegation };
  }

  it('refuses a sweep that would credit anything but the configured hot wallet', async () => {
    const { service, client, wallet, delegation } = buildSweepService(buildGuard());

    await expect(
      service.sweep({
        blockchainTransactionId: 'bx-sweep-evil',
        depositIndex: 7,
        depositAddress: 'TDepositAddress7',
        toHotWalletAddress: OTHER_ADDRESS,
        amount: '100',
        token: 'USDT',
        correlationId: 'corr-evil',
      }),
    ).rejects.toMatchObject({ code: 'DESTINATION_NOT_ALLOWED', retryable: false });
    expect(wallet.deriveSigner).not.toHaveBeenCalled();
    expect(delegation.withDelegation).not.toHaveBeenCalled();
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });

  it('sweeps any amount — the ceilings guard customer-facing transfers, not a pinned destination', async () => {
    const { service, client } = buildSweepService(buildGuard({ maxSingleTransferUnits: 1n }));

    const result = await service.sweep({
      blockchainTransactionId: 'bx-sweep-big',
      depositIndex: 7,
      depositAddress: 'TDepositAddress7',
      toHotWalletAddress: HOT_WALLET,
      amount: '9000',
      token: 'USDT',
      correlationId: 'corr-big',
    });

    expect(result.txHash).toBe('tx-fake');
    expect(client.sendTransfer).toHaveBeenCalled();
  });

  it('refuses a cold consolidation to anything but the configured cold wallet', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard(),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: DUMMY_SIGNER,
    });

    await expect(
      service.coldWalletTransfer({
        coldTransferId: 'cwt-evil',
        toColdAddress: OTHER_ADDRESS,
        amount: '5000',
        token: 'USDT',
        correlationId: 'corr-evil',
      }),
    ).rejects.toMatchObject({ code: 'DESTINATION_NOT_ALLOWED', retryable: false });
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });

  it('consolidates above the single-transfer ceiling — the destination is pinned', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard({ maxSingleTransferUnits: 1n, maxDailyOutflowUnits: 1n }),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: DUMMY_SIGNER,
    });

    const result = await service.coldWalletTransfer({
      coldTransferId: 'cwt-big',
      toColdAddress: COLD_WALLET,
      amount: '50000',
      token: 'USDT',
      correlationId: 'corr-cwt-big',
    });

    expect(result.txHash).toBe('tx-fake');
    expect(client.sendTransfer).toHaveBeenCalled();
  });

  it('refuses a payout above the single-transfer ceiling', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard({ maxSingleTransferUnits: 100n * UNIT }),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: DUMMY_SIGNER,
    });

    await expect(
      service.payout({
        blockchainTransactionId: 'bx-big',
        toAddress: OTHER_ADDRESS,
        amount: '100.000001',
        token: 'USDT',
        correlationId: 'corr-big',
      }),
    ).rejects.toMatchObject({ code: 'TRANSFER_AMOUNT_ABOVE_LIMIT', retryable: false });
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });

  it('refuses a payout that would push the last 24h of hot wallet outflow past the ceiling', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard({
        maxDailyOutflowUnits: 1_000n * UNIT,
        history: [
          {
            transaction_id: 'tx-earlier',
            from: HOT_WALLET,
            to: OTHER_ADDRESS,
            value: (950n * UNIT).toString(),
            block_timestamp: 1_700_000_000_000 - 60_000,
            token_info: { address: TOKEN_USDT },
          },
        ],
      }),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: DUMMY_SIGNER,
    });

    await expect(
      service.payout({
        blockchainTransactionId: 'bx-daily',
        toAddress: OTHER_ADDRESS,
        amount: '51',
        token: 'USDT',
        correlationId: 'corr-daily',
      }),
    ).rejects.toMatchObject({ code: 'DAILY_OUTFLOW_LIMIT_EXCEEDED', retryable: false });
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });

  it('counts a payout it just broadcast against the next one, before the chain shows it', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({});
    const service = new TransferService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      // History stays empty: only the in-flight record can stop the second one.
      guard: buildGuard({ maxDailyOutflowUnits: 100n * UNIT }),
      hotWalletAddress: HOT_WALLET,
      hotWalletPrivateKey: DUMMY_SIGNER,
    });

    await service.payout({
      blockchainTransactionId: 'bx-1',
      toAddress: OTHER_ADDRESS,
      amount: '60',
      token: 'USDT',
      correlationId: 'corr-1',
    });
    await expect(
      service.payout({
        blockchainTransactionId: 'bx-2',
        toAddress: OTHER_ADDRESS,
        amount: '60',
        token: 'USDT',
        correlationId: 'corr-2',
      }),
    ).rejects.toMatchObject({ code: 'DAILY_OUTFLOW_LIMIT_EXCEEDED' });
    expect(client.sendTransfer).toHaveBeenCalledTimes(1);
  });

  it('refuses a refund above the single-transfer ceiling', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      11: { address: 'TDeposit11', privateKey: DUMMY_SIGNER },
    });
    const delegation = buildStubDelegation('delegated');
    const service = new RefundService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      walletManager: wallet as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      tokenContracts: { USDT: TOKEN_USDT, USDC: TOKEN_USDC },
      guard: buildGuard({ maxSingleTransferUnits: 10n * UNIT }),
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    await expect(
      service.refund({
        blockchainTransactionId: 'bx-refund-big',
        depositIndex: 11,
        depositAddress: 'TDeposit11',
        toBuyerAddress: OTHER_ADDRESS,
        amount: '11',
        token: 'USDT',
        correlationId: 'corr-refund-big',
      }),
    ).rejects.toMatchObject({ code: 'TRANSFER_AMOUNT_ABOVE_LIMIT', retryable: false });
    expect(delegation.withDelegation).not.toHaveBeenCalled();
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });
});
