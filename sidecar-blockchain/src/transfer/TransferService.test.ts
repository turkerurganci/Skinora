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
  it('derives signer and broadcasts deposit -> hot wallet', async () => {
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

    await service.sweep({
      blockchainTransactionId: 'bx-sweep-1',
      depositIndex: 7,
      depositAddress: 'TDepositAddress7',
      toHotWalletAddress: 'THotWallet',
      amount: '100',
      token: 'USDC',
      correlationId: 'corr-sweep-1',
    });

    expect(wallet.deriveSigner).toHaveBeenCalledWith(7);
    expect(client.sendTransfer).toHaveBeenCalledWith(
      expect.objectContaining({
        fromAddress: 'TDepositAddress7',
        contractAddress: TOKEN_USDC,
        toAddress: 'THotWallet',
        amountUnits: '100000000',
      }),
    );
  });

  it('rejects DEPOSIT_ADDRESS_MISMATCH when derived address diverges from caller-supplied', async () => {
    const client = buildStubClient();
    const wallet = buildStubWallet({
      7: { address: 'TActuallyOther', privateKey: SIGNER_HOT_KEY },
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
    ).rejects.toMatchObject({ code: 'DEPOSIT_ADDRESS_MISMATCH', retryable: false });
    expect(client.sendTransfer).not.toHaveBeenCalled();
  });
});

describe('RefundService.refund()', () => {
  it('broadcasts deposit -> buyer source with the correct token contract', async () => {
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

    await service.refund({
      blockchainTransactionId: 'bx-refund-1',
      depositIndex: 11,
      depositAddress: 'TDeposit11',
      toBuyerAddress: 'TBuyerSource',
      amount: '95.5',
      token: 'USDT',
      correlationId: 'corr-refund-1',
    });

    expect(wallet.deriveSigner).toHaveBeenCalledWith(11);
    expect(client.sendTransfer).toHaveBeenCalledWith(
      expect.objectContaining({
        fromAddress: 'TDeposit11',
        contractAddress: TOKEN_USDT,
        toAddress: 'TBuyerSource',
        amountUnits: '95500000',
      }),
    );
  });
});
