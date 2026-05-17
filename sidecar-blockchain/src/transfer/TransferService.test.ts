import { describe, it, expect, vi } from 'vitest';
import { TransferService } from './TransferService.js';
import { RefundService } from './RefundService.js';
import { SidecarError } from '../errors/SidecarError.js';

const SIGNER_HOT_KEY = '01'.padStart(64, '0');
const TOKEN_USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const TOKEN_USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';

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
        _depositAddress: string,
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
      hotWalletAddress: 'THotWallet',
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
      fromAddress: 'THotWallet',
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
      hotWalletAddress: 'THotWallet',
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
      hotWalletAddress: 'THotWallet',
      hotWalletPrivateKey: SIGNER_HOT_KEY,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    const result = await service.sweep({
      blockchainTransactionId: 'bx-sweep-1',
      depositIndex: 7,
      depositAddress: 'TDepositAddress7',
      toHotWalletAddress: 'THotWallet',
      amount: '100',
      token: 'USDC',
      correlationId: 'corr-sweep-1',
    });

    expect(wallet.deriveSigner).toHaveBeenCalledWith(7);
    expect(delegation.withDelegation).toHaveBeenCalledWith(
      'TDepositAddress7',
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
        toAddress: 'THotWallet',
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
      hotWalletAddress: 'THotWallet',
      hotWalletPrivateKey: SIGNER_HOT_KEY,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    const result = await service.sweep({
      blockchainTransactionId: 'bx-sweep-fb',
      depositIndex: 9,
      depositAddress: 'TDepositFB9',
      toHotWalletAddress: 'THotWallet',
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
      hotWalletAddress: 'THotWallet',
      hotWalletPrivateKey: SIGNER_HOT_KEY,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    await expect(
      service.sweep({
        blockchainTransactionId: 'bx-sweep-err',
        depositIndex: 7,
        depositAddress: 'TDepositAddress7',
        toHotWalletAddress: 'THotWallet',
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
      hotWalletAddress: 'THotWallet',
      hotWalletPrivateKey: SIGNER_HOT_KEY,
    });

    await expect(
      service.sweep({
        blockchainTransactionId: 'bx-sweep-1',
        depositIndex: 7,
        depositAddress: 'TDepositAddress7',
        toHotWalletAddress: 'THotWallet',
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
      hotWalletAddress: 'THotWallet',
      hotWalletPrivateKey: SIGNER_HOT_KEY,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      energyDelegation: delegation as any,
    });

    await expect(
      service.sweep({
        blockchainTransactionId: 'bx-sweep-1',
        depositIndex: 7,
        depositAddress: 'TDepositAddress7',
        toHotWalletAddress: 'THotWallet',
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
      'TDeposit11',
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
