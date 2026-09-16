import { describe, it, expect, vi } from 'vitest';
import { EnergyDelegationService } from './EnergyDelegationService.js';
import { BURN_MARGIN, DELEGATION_MARGIN, MIN_DELEGATION_SUN } from './DelegationPlanner.js';

/**
 * 08 §3.3 hybrid flow against a STATEFUL fake chain: activation creates the
 * account, a delegation delivers Energy at the fake's ratio, a TRX transfer
 * raises the balance. "Never arrives" switches model a broadcast that did not
 * land, which is where the flow must not proceed to the transfer.
 */

const DEPOSIT = 'TDepositAddress42';
const SWEEPER = 'TSweeperHotWallet';
const DUMMY_SWEEPER_KEY = 'aa'.padStart(64, 'a');
const CONTRACT = 'TContractUsdt';
const RECIPIENT = 'THotWalletRecipient';
const FALLBACK_SUN = 15_000_000;
const MAINNET_RATIO = 9.52;
const NILE_RATIO = 73.7;
const ENERGY_FEE_SUN = 100;
const BANDWIDTH_FEE_SUN = 1000;
const CONTEXT = { blockchainTransactionId: 'bx-1', correlationId: 'corr-1' };
const TRANSFER = {
  depositAddress: DEPOSIT,
  contractAddress: CONTRACT,
  toAddress: RECIPIENT,
  amountUnits: '100000000',
};

interface ChainOptions {
  depositExists?: boolean;
  depositBalanceSun?: number;
  depositEnergy?: number;
  depositBandwidth?: number;
  energyRequired?: number;
  callerPercent?: number;
  ratio?: number | null;
  delegatableSun?: number;
  simulationThrows?: boolean;
  policyThrows?: boolean;
  activationNeverArrives?: boolean;
  trxNeverArrives?: boolean;
  delegateThrows?: boolean;
  delegationNeverArrives?: boolean;
  undelegateThrows?: boolean;
  sendTrxThrows?: boolean;
}

function buildChain(o: ChainOptions = {}) {
  const ratio = o.ratio === undefined ? MAINNET_RATIO : o.ratio;
  const state = {
    exists: o.depositExists ?? true,
    balanceSun: o.depositBalanceSun ?? 0,
    energy: o.depositEnergy ?? 0,
    bandwidth: o.depositBandwidth ?? 600,
  };

  const resources = {
    estimateTransferEnergy: vi.fn(async () => {
      if (o.simulationThrows) throw new Error('simulation reverted');
      return o.energyRequired ?? 64_285;
    }),
    getContractEnergyPolicy: vi.fn(async () => {
      if (o.policyThrows) throw new Error('policy probe failed');
      return { callerPercent: o.callerPercent ?? 100, originEnergyLimit: 0 };
    }),
    getAccountResources: vi.fn(async (address: string) =>
      address === SWEEPER
        ? { energyAvailable: 0, bandwidthAvailable: 600, energyPerTrx: ratio }
        : {
            energyAvailable: state.energy,
            bandwidthAvailable: state.exists ? state.bandwidth : 0,
            energyPerTrx: ratio,
          },
    ),
    getAccountState: vi.fn(async () => ({
      exists: state.exists,
      balanceSun: state.exists ? state.balanceSun : 0,
    })),
    getDelegatableEnergySun: vi.fn(async () => o.delegatableSun ?? 0),
    getChainFeeParameters: vi.fn(async () => ({
      energyFeeSun: ENERGY_FEE_SUN,
      bandwidthFeeSun: BANDWIDTH_FEE_SUN,
    })),
  };

  const client = {
    sendTrx: vi.fn(async (request: { amountSun: number }) => {
      if (o.sendTrxThrows) throw new Error('sendTrx rejected');
      const activation = !state.exists;
      if (activation && o.activationNeverArrives) return { txHash: 'trx-activation' };
      if (!activation && o.trxNeverArrives) return { txHash: 'trx-top-up' };
      state.exists = true;
      state.balanceSun += request.amountSun;
      return { txHash: activation ? 'trx-activation' : 'trx-top-up' };
    }),
    delegateEnergy: vi.fn(async (request: { amountSun: number }) => {
      if (o.delegateThrows) throw new Error('delegate rejected');
      if (!o.delegationNeverArrives && ratio)
        state.energy += Math.floor((request.amountSun / 1_000_000) * ratio);
      return { txHash: 'delegate-tx' };
    }),
    undelegateEnergy: vi.fn(async () => {
      if (o.undelegateThrows) throw new Error('undelegate rejected');
      state.energy = 0;
      return { txHash: 'undelegate-tx' };
    }),
  };

  const sleep = vi.fn(async () => {});
  const service = new EnergyDelegationService({
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    client: client as any,
    resources,
    sweeperAddress: SWEEPER,
    sweeperPrivateKey: DUMMY_SWEEPER_KEY,
    fallbackAmountSun: FALLBACK_SUN,
    pollIntervalMs: 1,
    pollAttempts: 3,
    sleep,
  });
  return { service, client, resources, state };
}

function delegationFor(energy: number, ratio: number): number {
  return Math.max(MIN_DELEGATION_SUN, Math.ceil((energy / ratio) * DELEGATION_MARGIN) * 1_000_000);
}

function topUpFor(energy: number, balanceSun = 0): number {
  return Math.max(0, Math.ceil(energy * ENERGY_FEE_SUN * BURN_MARGIN) - balanceSun);
}

// ---------------------------------------------------------------------------

describe('activation — a TRC-20-only deposit is not an account', () => {
  it('creates the account with 1 SUN before anything else', async () => {
    const { service, client } = buildChain({
      depositExists: false,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    const action = vi.fn(async () => ({ txHash: 'transfer' }));

    const outcome = await service.withDelegation(TRANSFER, action, CONTEXT);

    expect(client.sendTrx).toHaveBeenNthCalledWith(1, {
      fromAddress: SWEEPER,
      fromPrivateKey: DUMMY_SWEEPER_KEY,
      toAddress: DEPOSIT,
      amountSun: 1,
    });
    expect(outcome.activationSun).toBe(1);
    // Activation precedes delegation — delegating to a missing account is rejected on-chain.
    expect(client.sendTrx.mock.invocationCallOrder[0]).toBeLessThan(
      client.delegateEnergy.mock.invocationCallOrder[0],
    );
  });

  it('does not re-activate an existing account', async () => {
    const { service, client } = buildChain({
      depositExists: true,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });

    const outcome = await service.withDelegation(TRANSFER, async () => 'ok', CONTEXT);

    expect(client.sendTrx).not.toHaveBeenCalled();
    expect(outcome.activationSun).toBe(0);
  });

  it('stops (retryable) when the activation never becomes visible — the transfer does not run', async () => {
    const { service } = buildChain({ depositExists: false, activationNeverArrives: true });
    const action = vi.fn();

    await expect(service.withDelegation(TRANSFER, action, CONTEXT)).rejects.toMatchObject({
      code: 'DEPOSIT_ACTIVATION_NOT_CONFIRMED',
      retryable: true,
    });
    expect(action).not.toHaveBeenCalled();
  });
});

describe('delegate path — the stake covers the whole shortfall', () => {
  it.each([
    { name: 'mainnet ratio', ratio: MAINNET_RATIO, energy: 64_285 },
    { name: 'nile ratio', ratio: NILE_RATIO, energy: 130_285 },
  ])(
    'delegates exactly the planned amount, runs the transfer, reclaims ($name)',
    async ({ ratio, energy }) => {
      const { service, client } = buildChain({
        ratio,
        energyRequired: energy,
        delegatableSun: Number.MAX_SAFE_INTEGER,
      });
      const action = vi.fn(async () => ({ txHash: 'transfer' }));
      const expected = delegationFor(energy, ratio);

      const outcome = await service.withDelegation(TRANSFER, action, CONTEXT);

      expect(client.delegateEnergy).toHaveBeenCalledWith({
        ownerAddress: SWEEPER,
        ownerPrivateKey: DUMMY_SWEEPER_KEY,
        receiverAddress: DEPOSIT,
        amountSun: expected,
      });
      expect(action).toHaveBeenCalledOnce();
      expect(client.undelegateEnergy).toHaveBeenCalledWith(
        expect.objectContaining({ amountSun: expected }),
      );
      expect(client.sendTrx).not.toHaveBeenCalled();
      expect(outcome).toMatchObject({
        mode: 'delegated',
        delegationAmountSun: expected,
        fallbackAmountSun: 0,
      });
    },
  );

  it('simulates the exact transfer it was given', async () => {
    const { service, resources } = buildChain({ delegatableSun: Number.MAX_SAFE_INTEGER });

    await service.withDelegation(TRANSFER, async () => 'ok', CONTEXT);

    expect(resources.estimateTransferEnergy).toHaveBeenCalledWith(
      CONTRACT,
      DEPOSIT,
      RECIPIENT,
      '100000000',
    );
  });

  it('reclaims and BURNS when the delegated Energy never arrives', async () => {
    // A broadcast short of Energy fails on-chain with no fallback after it —
    // so the flow must see the Energy before sending, or switch paths.
    const { service, client } = buildChain({
      delegatableSun: Number.MAX_SAFE_INTEGER,
      delegationNeverArrives: true,
    });
    const action = vi.fn(async () => 'ok');

    const outcome = await service.withDelegation(TRANSFER, action, CONTEXT);

    expect(client.undelegateEnergy).toHaveBeenCalledOnce();
    expect(client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: topUpFor(64_285) }),
    );
    expect(client.undelegateEnergy.mock.invocationCallOrder[0]).toBeLessThan(
      action.mock.invocationCallOrder[0],
    );
    expect(outcome).toMatchObject({
      mode: 'burn',
      delegationAmountSun: 0,
      fallbackAmountSun: topUpFor(64_285),
    });
  });

  it('burns when the delegation broadcast itself is rejected', async () => {
    const { service, client } = buildChain({
      delegatableSun: Number.MAX_SAFE_INTEGER,
      delegateThrows: true,
    });

    const outcome = await service.withDelegation(TRANSFER, async () => 'ok', CONTEXT);

    expect(client.undelegateEnergy).not.toHaveBeenCalled();
    expect(outcome.mode).toBe('burn');
  });

  it('reclaims before re-throwing when the transfer fails', async () => {
    const { service, client } = buildChain({ delegatableSun: Number.MAX_SAFE_INTEGER });

    await expect(
      service.withDelegation(
        TRANSFER,
        async () => {
          throw new Error('broadcast failed');
        },
        CONTEXT,
      ),
    ).rejects.toThrow('broadcast failed');
    expect(client.undelegateEnergy).toHaveBeenCalledOnce();
  });

  it('still returns the transfer when only the reclaim fails', async () => {
    const { service } = buildChain({
      delegatableSun: Number.MAX_SAFE_INTEGER,
      undelegateThrows: true,
    });

    const outcome = await service.withDelegation(
      TRANSFER,
      async () => ({ txHash: 'transfer' }),
      CONTEXT,
    );

    expect(outcome.action).toEqual({ txHash: 'transfer' });
    expect(outcome.mode).toBe('delegated');
  });
});

describe('burn path — the stake cannot cover the whole shortfall', () => {
  it('sends exactly the TRX the transfer will burn, never a partial delegation', async () => {
    // One SUN short of the planned delegation must behave like no stake at all.
    const { service, client } = buildChain({
      delegatableSun: delegationFor(64_285, MAINNET_RATIO) - 1,
    });
    const action = vi.fn(async () => 'ok');

    const outcome = await service.withDelegation(TRANSFER, action, CONTEXT);

    expect(client.delegateEnergy).not.toHaveBeenCalled();
    expect(client.sendTrx).toHaveBeenCalledWith({
      fromAddress: SWEEPER,
      fromPrivateKey: DUMMY_SWEEPER_KEY,
      toAddress: DEPOSIT,
      amountSun: topUpFor(64_285),
    });
    expect(action).toHaveBeenCalledOnce();
    expect(outcome).toMatchObject({
      mode: 'burn',
      delegationAmountSun: 0,
      fallbackAmountSun: topUpFor(64_285),
    });
  });

  it.each([
    { energy: 64_285, balance: 0 },
    { energy: 130_285, balance: 4_000_000 },
  ])(
    'sizes the top-up from the simulation and subtracts the balance ($energy Energy, $balance SUN)',
    async ({ energy, balance }) => {
      const { service, client } = buildChain({
        energyRequired: energy,
        depositBalanceSun: balance,
        delegatableSun: 0,
      });

      await service.withDelegation(TRANSFER, async () => 'ok', CONTEXT);

      expect(client.sendTrx).toHaveBeenCalledWith(
        expect.objectContaining({ amountSun: topUpFor(energy, balance) }),
      );
    },
  );

  it('sends nothing when the deposit already holds enough TRX', async () => {
    const { service, client } = buildChain({ depositBalanceSun: 20_000_000, delegatableSun: 0 });

    const outcome = await service.withDelegation(TRANSFER, async () => 'ok', CONTEXT);

    expect(client.sendTrx).not.toHaveBeenCalled();
    expect(outcome).toMatchObject({ mode: 'burn', fallbackAmountSun: 0 });
  });

  it('does not run the transfer until the top-up has arrived', async () => {
    const { service } = buildChain({ delegatableSun: 0, trxNeverArrives: true });
    const action = vi.fn();

    await expect(service.withDelegation(TRANSFER, action, CONTEXT)).rejects.toMatchObject({
      code: 'TRX_TOP_UP_NOT_CONFIRMED',
      retryable: true,
    });
    expect(action).not.toHaveBeenCalled();
  });

  it('plans as if the caller pays everything when the policy probe fails', async () => {
    const { service, client } = buildChain({
      callerPercent: 0,
      policyThrows: true,
      delegatableSun: 0,
    });

    await service.withDelegation(TRANSFER, async () => 'ok', CONTEXT);

    expect(client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: topUpFor(64_285) }),
    );
  });
});

describe('no-energy path — the contract owner pays', () => {
  it('sends nothing when Bandwidth is available', async () => {
    const { service, client } = buildChain({
      callerPercent: 0,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });

    const outcome = await service.withDelegation(TRANSFER, async () => 'ok', CONTEXT);

    expect(client.delegateEnergy).not.toHaveBeenCalled();
    expect(client.sendTrx).not.toHaveBeenCalled();
    expect(outcome).toMatchObject({ mode: 'no-energy', fallbackAmountSun: 0 });
  });

  it('tops up only Bandwidth when the free allowance is used up', async () => {
    const { service, client } = buildChain({ callerPercent: 0, depositBandwidth: 100 });

    const outcome = await service.withDelegation(TRANSFER, async () => 'ok', CONTEXT);

    const bandwidthSun = Math.ceil(350 * BANDWIDTH_FEE_SUN * BURN_MARGIN);
    expect(client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: bandwidthSun }),
    );
    expect(outcome).toMatchObject({ mode: 'no-energy', fallbackAmountSun: bandwidthSun });
  });
});

describe('plan unavailable — fixed fallback keeps the money path open', () => {
  it('sends the configured fallback when the simulation fails', async () => {
    const { service, client } = buildChain({
      simulationThrows: true,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    const action = vi.fn(async () => 'ok');

    const outcome = await service.withDelegation(TRANSFER, action, CONTEXT);

    expect(client.delegateEnergy).not.toHaveBeenCalled();
    expect(client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: FALLBACK_SUN }),
    );
    expect(action).toHaveBeenCalledOnce();
    expect(outcome).toMatchObject({ mode: 'fallback', fallbackAmountSun: FALLBACK_SUN });
  });

  it('raises DELEGATION_AND_FALLBACK_FAILED (retryable) when the TRX cannot be sent', async () => {
    const { service } = buildChain({ simulationThrows: true, sendTrxThrows: true });

    await expect(service.withDelegation(TRANSFER, vi.fn(), CONTEXT)).rejects.toMatchObject({
      code: 'DELEGATION_AND_FALLBACK_FAILED',
      retryable: true,
    });
  });
});

describe('configuration', () => {
  it('rejects missing sweeper credentials', async () => {
    const service = new EnergyDelegationService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: {} as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      resources: {} as any,
      sweeperAddress: '',
      sweeperPrivateKey: '',
      fallbackAmountSun: FALLBACK_SUN,
    });

    await expect(service.withDelegation(TRANSFER, vi.fn(), CONTEXT)).rejects.toMatchObject({
      code: 'SWEEPER_NOT_CONFIGURED',
      retryable: false,
    });
  });

  it('rejects a non-positive fallback amount', async () => {
    const service = new EnergyDelegationService({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      client: {} as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      resources: {} as any,
      sweeperAddress: SWEEPER,
      sweeperPrivateKey: DUMMY_SWEEPER_KEY,
      fallbackAmountSun: 0,
    });

    await expect(service.withDelegation(TRANSFER, vi.fn(), CONTEXT)).rejects.toMatchObject({
      code: 'INVALID_FALLBACK_AMOUNT',
      retryable: false,
    });
  });
});
