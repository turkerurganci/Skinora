import { describe, it, expect, vi } from 'vitest';
import { EnergyDelegationService } from './EnergyDelegationService.js';
import { SidecarError } from '../errors/SidecarError.js';

const DEPOSIT_ADDR = 'TDepositAddress42';
const SWEEPER_ADDR = 'TSweeperHotWallet';
const SWEEPER_KEY = 'aa'.padStart(64, 'a');
const DELEGATION_SUN = 200_000_000;
const FALLBACK_SUN = 15_000_000;

const CONTEXT = {
  blockchainTransactionId: 'bx-1',
  correlationId: 'corr-1',
};

function buildClient(overrides: Partial<DelegationClientStub> = {}): DelegationClientStub {
  return {
    delegateEnergy: vi.fn(async () => ({ txHash: 'delegate-tx-1' })),
    undelegateEnergy: vi.fn(async () => ({ txHash: 'undelegate-tx-1' })),
    sendTrx: vi.fn(async () => ({ txHash: 'trx-tx-1' })),
    ...overrides,
  };
}

interface DelegationClientStub {
  delegateEnergy: ReturnType<typeof vi.fn>;
  undelegateEnergy: ReturnType<typeof vi.fn>;
  sendTrx: ReturnType<typeof vi.fn>;
}

function buildService(client: DelegationClientStub): EnergyDelegationService {
  return new EnergyDelegationService({
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    client: client as any,
    sweeperAddress: SWEEPER_ADDR,
    sweeperPrivateKey: SWEEPER_KEY,
    delegationAmountSun: DELEGATION_SUN,
    fallbackAmountSun: FALLBACK_SUN,
  });
}

describe('EnergyDelegationService.withDelegation() — happy path', () => {
  it('delegates, runs the action, then undelegates', async () => {
    const client = buildClient();
    const service = buildService(client);
    const action = vi.fn(async () => ({ txHash: 'transfer-tx-1' }));

    const outcome = await service.withDelegation(DEPOSIT_ADDR, action, CONTEXT);

    expect(client.delegateEnergy).toHaveBeenCalledWith({
      ownerAddress: SWEEPER_ADDR,
      ownerPrivateKey: SWEEPER_KEY,
      receiverAddress: DEPOSIT_ADDR,
      amountSun: DELEGATION_SUN,
    });
    expect(action).toHaveBeenCalled();
    expect(client.undelegateEnergy).toHaveBeenCalledWith({
      ownerAddress: SWEEPER_ADDR,
      ownerPrivateKey: SWEEPER_KEY,
      receiverAddress: DEPOSIT_ADDR,
      amountSun: DELEGATION_SUN,
    });
    expect(client.sendTrx).not.toHaveBeenCalled();
    expect(outcome).toEqual({
      mode: 'delegated',
      delegationAmountSun: DELEGATION_SUN,
      fallbackAmountSun: 0,
      action: { txHash: 'transfer-tx-1' },
    });
  });

  it('preserves the action return type through the outcome', async () => {
    const client = buildClient();
    const service = buildService(client);
    const action = vi.fn(async () => 'arbitrary-result' as const);

    const outcome = await service.withDelegation(DEPOSIT_ADDR, action, CONTEXT);

    expect(outcome.action).toBe('arbitrary-result');
  });
});

describe('EnergyDelegationService.withDelegation() — fallback path', () => {
  it('falls back to TRX prefund when delegation broadcast fails', async () => {
    const client = buildClient({
      delegateEnergy: vi.fn(async () => {
        throw new SidecarError('rejected', 'DELEGATE_BROADCAST_REJECTED', true);
      }),
    });
    const service = buildService(client);
    const action = vi.fn(async () => ({ txHash: 'transfer-tx-after-fallback' }));

    const outcome = await service.withDelegation(DEPOSIT_ADDR, action, CONTEXT);

    expect(client.delegateEnergy).toHaveBeenCalled();
    expect(client.sendTrx).toHaveBeenCalledWith({
      fromAddress: SWEEPER_ADDR,
      fromPrivateKey: SWEEPER_KEY,
      toAddress: DEPOSIT_ADDR,
      amountSun: FALLBACK_SUN,
    });
    expect(client.undelegateEnergy).not.toHaveBeenCalled();
    expect(action).toHaveBeenCalled();
    expect(outcome).toEqual({
      mode: 'fallback',
      delegationAmountSun: 0,
      fallbackAmountSun: FALLBACK_SUN,
      action: { txHash: 'transfer-tx-after-fallback' },
    });
  });

  it('raises DELEGATION_AND_FALLBACK_FAILED (retryable) when both delegate and sendTrx fail', async () => {
    const client = buildClient({
      delegateEnergy: vi.fn(async () => {
        throw new SidecarError('reject-d', 'DELEGATE_BROADCAST_REJECTED', true);
      }),
      sendTrx: vi.fn(async () => {
        throw new SidecarError('reject-f', 'FALLBACK_TRX_BROADCAST_REJECTED', true);
      }),
    });
    const service = buildService(client);
    const action = vi.fn(async () => ({ txHash: 'never-reached' }));

    await expect(service.withDelegation(DEPOSIT_ADDR, action, CONTEXT)).rejects.toMatchObject({
      code: 'DELEGATION_AND_FALLBACK_FAILED',
      retryable: true,
    });
    expect(action).not.toHaveBeenCalled();
    expect(client.undelegateEnergy).not.toHaveBeenCalled();
  });
});

describe('EnergyDelegationService.withDelegation() — undelegate fault tolerance', () => {
  it('preserves the action result when undelegate broadcast fails (best-effort reclaim)', async () => {
    const undelegateErr = new SidecarError('boom', 'UNDELEGATE_BROADCAST_REJECTED', true);
    const client = buildClient({
      undelegateEnergy: vi.fn(async () => {
        throw undelegateErr;
      }),
    });
    const service = buildService(client);
    const action = vi.fn(async () => ({ txHash: 'transfer-tx-survives' }));

    const outcome = await service.withDelegation(DEPOSIT_ADDR, action, CONTEXT);

    expect(client.undelegateEnergy).toHaveBeenCalled();
    expect(outcome.mode).toBe('delegated');
    expect(outcome.action).toEqual({ txHash: 'transfer-tx-survives' });
  });
});

describe('EnergyDelegationService.withDelegation() — action-failed cleanup', () => {
  it('still attempts undelegate when the action throws after a successful delegate', async () => {
    const client = buildClient();
    const service = buildService(client);
    const actionErr = new SidecarError('broadcast', 'TRANSFER_BROADCAST_FAILED', true);
    const action = vi.fn(async () => {
      throw actionErr;
    });

    await expect(service.withDelegation(DEPOSIT_ADDR, action, CONTEXT)).rejects.toBe(actionErr);
    expect(client.undelegateEnergy).toHaveBeenCalledTimes(1);
  });

  it('does not attempt undelegate when the fallback path was used and the action throws', async () => {
    const client = buildClient({
      delegateEnergy: vi.fn(async () => {
        throw new SidecarError('reject', 'DELEGATE_BROADCAST_REJECTED', true);
      }),
    });
    const service = buildService(client);
    const action = vi.fn(async () => {
      throw new SidecarError('broadcast', 'TRANSFER_BROADCAST_FAILED', true);
    });

    await expect(service.withDelegation(DEPOSIT_ADDR, action, CONTEXT)).rejects.toMatchObject({
      code: 'TRANSFER_BROADCAST_FAILED',
    });
    expect(client.sendTrx).toHaveBeenCalled();
    expect(client.undelegateEnergy).not.toHaveBeenCalled();
  });
});

describe('EnergyDelegationService — configuration guards', () => {
  it('rejects SWEEPER_NOT_CONFIGURED when sweeper credentials are missing', async () => {
    const client = buildClient();
    const service = new EnergyDelegationService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      sweeperAddress: '',
      sweeperPrivateKey: '',
      delegationAmountSun: DELEGATION_SUN,
      fallbackAmountSun: FALLBACK_SUN,
    });

    await expect(
      service.withDelegation(DEPOSIT_ADDR, async () => ({ txHash: 'x' }), CONTEXT),
    ).rejects.toMatchObject({ code: 'SWEEPER_NOT_CONFIGURED', retryable: false });
  });

  it('rejects INVALID_DELEGATION_AMOUNT for non-positive delegation sun', async () => {
    const client = buildClient();
    const service = new EnergyDelegationService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      sweeperAddress: SWEEPER_ADDR,
      sweeperPrivateKey: SWEEPER_KEY,
      delegationAmountSun: 0,
      fallbackAmountSun: FALLBACK_SUN,
    });

    await expect(
      service.withDelegation(DEPOSIT_ADDR, async () => ({ txHash: 'x' }), CONTEXT),
    ).rejects.toMatchObject({ code: 'INVALID_DELEGATION_AMOUNT', retryable: false });
  });

  it('rejects INVALID_FALLBACK_AMOUNT for non-positive fallback sun', async () => {
    const client = buildClient();
    const service = new EnergyDelegationService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: client as any,
      sweeperAddress: SWEEPER_ADDR,
      sweeperPrivateKey: SWEEPER_KEY,
      delegationAmountSun: DELEGATION_SUN,
      fallbackAmountSun: -1,
    });

    await expect(
      service.withDelegation(DEPOSIT_ADDR, async () => ({ txHash: 'x' }), CONTEXT),
    ).rejects.toMatchObject({ code: 'INVALID_FALLBACK_AMOUNT', retryable: false });
  });
});
