import { describe, it, expect, vi } from 'vitest';
import { TronDelegationClient, DelegationTronWebFactory } from './TronDelegationClient.js';
import { SidecarError } from '../errors/SidecarError.js';

const FULL_NODE = 'https://nile.trongrid.io';
const API_KEY = 'fake-api-key';
const OWNER_ADDR = 'TSweeperHotWallet';
const RECEIVER_ADDR = 'TDepositAddress42';
const OWNER_KEY = 'aa'.padStart(64, 'a');

function buildTronWebStub(overrides: Partial<StubTronWeb> = {}): StubTronWeb {
  return {
    transactionBuilder: {
      delegateResource: vi.fn(async () => ({ txID: 'delegate-tx-1' })),
      undelegateResource: vi.fn(async () => ({ txID: 'undelegate-tx-1' })),
      sendTrx: vi.fn(async () => ({ txID: 'trx-tx-1' })),
    },
    trx: {
      sign: vi.fn(async (transaction: unknown) => transaction),
      sendRawTransaction: vi.fn(async () => ({ result: true, txid: 'broadcast-tx-1' })),
    },
    ...overrides,
  };
}

interface StubTronWeb {
  transactionBuilder: {
    delegateResource: ReturnType<typeof vi.fn>;
    undelegateResource: ReturnType<typeof vi.fn>;
    sendTrx: ReturnType<typeof vi.fn>;
  };
  trx: {
    sign: ReturnType<typeof vi.fn>;
    sendRawTransaction: ReturnType<typeof vi.fn>;
  };
}

describe('TronDelegationClient.delegateEnergy()', () => {
  it('builds, signs and broadcasts a delegateResource call with ENERGY + lock=false', async () => {
    const tronWeb = buildTronWebStub();
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    const result = await client.delegateEnergy({
      ownerAddress: OWNER_ADDR,
      ownerPrivateKey: OWNER_KEY,
      receiverAddress: RECEIVER_ADDR,
      amountSun: 200_000_000,
    });

    expect(result.txHash).toBe('broadcast-tx-1');
    expect(factory).toHaveBeenCalledWith(
      expect.objectContaining({
        fullHost: FULL_NODE,
        apiKey: API_KEY,
        privateKey: OWNER_KEY,
      }),
    );
    expect(tronWeb.transactionBuilder.delegateResource).toHaveBeenCalledWith(
      200_000_000,
      RECEIVER_ADDR,
      'ENERGY',
      OWNER_ADDR,
      false,
    );
    expect(tronWeb.trx.sign).toHaveBeenCalled();
    expect(tronWeb.trx.sendRawTransaction).toHaveBeenCalled();
  });

  it('rejects DELEGATE_NO_PRIVATE_KEY when key is empty', async () => {
    const tronWeb = buildTronWebStub();
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.delegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: '',
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({ code: 'DELEGATE_NO_PRIVATE_KEY', retryable: false });

    expect(tronWeb.transactionBuilder.delegateResource).not.toHaveBeenCalled();
    expect(factory).not.toHaveBeenCalled();
  });

  it('raises DELEGATE_BUILD_FAILED when transactionBuilder returns no txID', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.transactionBuilder.delegateResource.mockResolvedValueOnce(undefined);
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.delegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: OWNER_KEY,
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({ code: 'DELEGATE_BUILD_FAILED', retryable: true });
  });

  it('raises DELEGATE_BROADCAST_REJECTED when broadcast result is false', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValueOnce({
      result: false,
      message: 'BANDWIDTH_ERROR',
    });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.delegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: OWNER_KEY,
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({ code: 'DELEGATE_BROADCAST_REJECTED', retryable: true });
  });

  it('wraps unexpected exceptions in DELEGATE_BROADCAST_FAILED (retryable)', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.transactionBuilder.delegateResource.mockRejectedValueOnce(new Error('socket reset'));
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.delegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: OWNER_KEY,
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({ code: 'DELEGATE_BROADCAST_FAILED', retryable: true });
  });
});

describe('TronDelegationClient.undelegateEnergy()', () => {
  it('builds, signs and broadcasts an undelegateResource call (no lock parameter)', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValue({ result: true, txid: 'undelegate-bx-1' });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    const result = await client.undelegateEnergy({
      ownerAddress: OWNER_ADDR,
      ownerPrivateKey: OWNER_KEY,
      receiverAddress: RECEIVER_ADDR,
      amountSun: 200_000_000,
    });

    expect(result.txHash).toBe('undelegate-bx-1');
    expect(tronWeb.transactionBuilder.undelegateResource).toHaveBeenCalledWith(
      200_000_000,
      RECEIVER_ADDR,
      'ENERGY',
      OWNER_ADDR,
    );
  });

  it('raises UNDELEGATE_BROADCAST_REJECTED when result is false', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValueOnce({
      result: false,
      code: 'CONTRACT_VALIDATE_ERROR',
    });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.undelegateEnergy({
        ownerAddress: OWNER_ADDR,
        ownerPrivateKey: OWNER_KEY,
        receiverAddress: RECEIVER_ADDR,
        amountSun: 200_000_000,
      }),
    ).rejects.toMatchObject({ code: 'UNDELEGATE_BROADCAST_REJECTED', retryable: true });
  });
});

describe('TronDelegationClient.sendTrx()', () => {
  it('builds, signs and broadcasts a TRX transfer used as 08 §3.3 fallback', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValue({ result: true, txid: 'trx-bx-1' });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    const result = await client.sendTrx({
      fromAddress: OWNER_ADDR,
      fromPrivateKey: OWNER_KEY,
      toAddress: RECEIVER_ADDR,
      amountSun: 15_000_000,
    });

    expect(result.txHash).toBe('trx-bx-1');
    expect(tronWeb.transactionBuilder.sendTrx).toHaveBeenCalledWith(
      RECEIVER_ADDR,
      15_000_000,
      OWNER_ADDR,
    );
  });

  it('raises FALLBACK_TRX_BROADCAST_REJECTED when broadcast rejects', async () => {
    const tronWeb = buildTronWebStub();
    tronWeb.trx.sendRawTransaction.mockResolvedValueOnce({ result: false });
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.sendTrx({
        fromAddress: OWNER_ADDR,
        fromPrivateKey: OWNER_KEY,
        toAddress: RECEIVER_ADDR,
        amountSun: 15_000_000,
      }),
    ).rejects.toMatchObject({ code: 'FALLBACK_TRX_BROADCAST_REJECTED', retryable: true });
  });

  it('preserves SidecarError instances thrown by the builder', async () => {
    const tronWeb = buildTronWebStub();
    const upstream = new SidecarError('custom', 'CUSTOM_CODE', false);
    tronWeb.transactionBuilder.sendTrx.mockRejectedValueOnce(upstream);
    const factory: DelegationTronWebFactory = vi.fn(() => tronWeb);
    const client = new TronDelegationClient(FULL_NODE, API_KEY, factory);

    await expect(
      client.sendTrx({
        fromAddress: OWNER_ADDR,
        fromPrivateKey: OWNER_KEY,
        toAddress: RECEIVER_ADDR,
        amountSun: 15_000_000,
      }),
    ).rejects.toBe(upstream);
  });
});
