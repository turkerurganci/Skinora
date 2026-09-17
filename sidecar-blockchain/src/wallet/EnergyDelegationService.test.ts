import { describe, it, expect, vi } from 'vitest';
import { EnergyDelegationService } from './EnergyDelegationService.js';

/**
 * 08 §3.3 hybrid flow against a fake chain that behaves like the node measured
 * in #323's validation (2026-09-17):
 *
 * <list type="bullet">
 *   <item>ACCOUNT READS ANSWER FROM PENDING STATE. A broadcast's effect shows
 *     the moment the node accepts it, before any block holds it — so a flow
 *     that takes a read as "it happened" passes here exactly as it passed on
 *     Nile, where delegation, transfer and reclaim shared one block.</item>
 *   <item>BLOCKS ARE SEPARATE. A transaction gets a block number only when the
 *     fake produces a block, which it does once per poll interval slept.</item>
 *   <item>EVERY ACCOUNT ANSWERS FOR ITSELF. The sweeper is rich in Energy, TRX
 *     and stake, the deposit starts with nothing, any other address throws —
 *     a probe pointed at the wrong account changes the outcome.</item>
 * </list>
 *
 * Expected amounts are literals worked out by hand, not recomputed from the
 * planner's constants: a test that imports the margin cannot notice it change.
 */

const DEPOSIT = 'TDepositAddress42';
const SWEEPER = 'TSweeperHotWallet';
const DUMMY_SWEEPER_KEY = 'aa'.padStart(64, 'a');
const CONTRACT = 'TContractUsdt';
const RECIPIENT = 'THotWalletRecipient';
const FALLBACK_SUN = 15_000_000;
const MAINNET_RATIO = 9.52;
const NILE_RATIO = 73.7;
const CONTEXT = { blockchainTransactionId: 'bx-1', correlationId: 'corr-1' };
const TRANSFER = {
  depositAddress: DEPOSIT,
  contractAddress: CONTRACT,
  toAddress: RECIPIENT,
  amountUnits: '100000000',
};

/** 64,285 Energy at 9.52 Energy/TRX: 64,285 ÷ 9.52 × 1.1 = 7,427.9 → 7,428 TRX. */
const DELEGATION_64K_MAINNET_SUN = 7_428_000_000;
/** 130,285 Energy at 73.7 Energy/TRX: 130,285 ÷ 73.7 × 1.1 = 1,944.6 → 1,945 TRX. */
const DELEGATION_130K_NILE_SUN = 1_945_000_000;
/** 64,285 Energy × 100 SUN = 6,428,500 SUN, + 10% = 7,071,350 SUN. */
const BURN_TOP_UP_64K_SUN = 7_071_350;
/** 350 bytes × 1,000 SUN = 350,000 SUN, + 10% = 385,000 SUN. */
const BANDWIDTH_TOP_UP_SUN = 385_000;
/** The node stamps every transaction it builds with a 60 s expiration. */
const TX_EXPIRATION_MS = 60_000;

type TxKind = 'activation' | 'trx' | 'delegate' | 'undelegate' | 'transfer';

interface ChainOptions {
  depositExists?: boolean;
  depositBalanceSun?: number;
  depositEnergy?: number;
  depositBandwidth?: number;
  energyRequired?: number;
  ratio?: number | null;
  delegatableSun?: number;
  /** Share of the planned Energy a delegation actually delivers (the ratio moved). */
  delegationDelivers?: number;
  /** Transactions of these kinds never reach a block (dropped, expired). */
  neverInBlock?: TxKind[];
  /** The node answering account reads never shows the deposit's activation. */
  activationReadLags?: boolean;
  simulationThrows?: boolean;
  delegateThrows?: boolean;
  undelegateThrows?: boolean;
  sendTrxThrows?: boolean;
  transferThrows?: boolean;
  transferBroadcastDeadlineMs?: number;
}

function buildChain(o: ChainOptions = {}) {
  const ratio = o.ratio === undefined ? MAINNET_RATIO : o.ratio;
  const deposit = {
    exists: o.depositExists ?? true,
    balanceSun: o.depositBalanceSun ?? 0,
    energy: o.depositEnergy ?? 0,
    bandwidth: o.depositBandwidth ?? 600,
  };
  const sweeperResources = {
    energyAvailable: 5_000_000,
    bandwidthAvailable: 5_000,
    energyPerTrx: ratio,
  };
  const sweeperState = { exists: true, balanceSun: 2_000_000_000 };

  let height = 71_019_000;
  let clockMs = 0;
  let sequence = 0;
  const blockOf = new Map<string, number>();
  let pending: string[] = [];
  const broadcasts: { hash: string; kind: TxKind; amountSun?: number; atMs: number }[] = [];

  function broadcast(kind: TxKind, amountSun?: number): string {
    const hash = `${kind}-${++sequence}`;
    pending.push(hash);
    broadcasts.push({ hash, kind, amountSun, atMs: clockMs });
    return hash;
  }

  function produceBlock(): void {
    height += 1;
    pending = pending.filter((hash) => {
      const kind = broadcasts.find((b) => b.hash === hash)!.kind;
      if (o.neverInBlock?.includes(kind)) return true;
      blockOf.set(hash, height);
      return false;
    });
  }

  const hashesOf = (kind: TxKind) => broadcasts.filter((b) => b.kind === kind).map((b) => b.hash);
  const blockOfLatest = (kind: TxKind) => {
    const hash = hashesOf(kind).at(-1);
    return hash === undefined ? null : (blockOf.get(hash) ?? null);
  };

  /** Block status of the earlier steps at the moment each later step began. */
  const seen = {
    activationBlockAtPlan: [] as (number | null)[],
    atTransfer: [] as { delegate: number | null; trx: number | null }[],
    transferBlockAtReclaim: [] as (number | null)[],
  };

  const unexpected = (address: string): never => {
    throw new Error(`fake chain: no account ${address}`);
  };

  const resources = {
    estimateTransferEnergy: vi.fn(async () => {
      seen.activationBlockAtPlan.push(blockOfLatest('activation'));
      if (o.simulationThrows) throw new Error('simulation reverted');
      return o.energyRequired ?? 64_285;
    }),
    getAccountResources: vi.fn(async (address: string) => {
      if (address === SWEEPER) return sweeperResources;
      if (address !== DEPOSIT) return unexpected(address);
      return deposit.exists
        ? {
            energyAvailable: deposit.energy,
            bandwidthAvailable: deposit.bandwidth,
            energyPerTrx: ratio,
          }
        : { energyAvailable: 0, bandwidthAvailable: 0, energyPerTrx: null };
    }),
    getAccountState: vi.fn(async (address: string) => {
      if (address === SWEEPER) return sweeperState;
      if (address !== DEPOSIT) return unexpected(address);
      return deposit.exists && !o.activationReadLags
        ? { exists: true, balanceSun: deposit.balanceSun }
        : { exists: false, balanceSun: 0 };
    }),
    getDelegatableEnergySun: vi.fn(async (address: string) => {
      if (address === SWEEPER) return o.delegatableSun ?? 0;
      if (address === DEPOSIT) return 0;
      return unexpected(address);
    }),
    getChainFeeParameters: vi.fn(async () => ({ energyFeeSun: 100, bandwidthFeeSun: 1_000 })),
    getTransactionBlockNumber: vi.fn(async (hash: string) => blockOf.get(hash) ?? null),
  };

  let delivered = 0;
  const client = {
    sendTrx: vi.fn(async (request: { toAddress: string; amountSun: number }) => {
      if (o.sendTrxThrows) throw new Error('sendTrx rejected');
      const hash = broadcast(deposit.exists ? 'trx' : 'activation', request.amountSun);
      // Pending view: the node shows it before any block holds it.
      deposit.exists = true;
      deposit.balanceSun += request.amountSun;
      return { txHash: hash };
    }),
    delegateEnergy: vi.fn(async (request: { receiverAddress: string; amountSun: number }) => {
      if (o.delegateThrows) throw new Error('delegate rejected');
      if (!deposit.exists) throw new Error(`Account[${request.receiverAddress}] not exists`);
      const hash = broadcast('delegate', request.amountSun);
      delivered = ratio
        ? Math.floor((request.amountSun / 1_000_000) * ratio * (o.delegationDelivers ?? 1))
        : 0;
      deposit.energy += delivered;
      return { txHash: hash };
    }),
    undelegateEnergy: vi.fn(async (request: { amountSun: number }) => {
      seen.transferBlockAtReclaim.push(blockOfLatest('transfer'));
      if (o.undelegateThrows) throw new Error('undelegate rejected');
      const hash = broadcast('undelegate', request.amountSun);
      deposit.energy -= delivered;
      delivered = 0;
      return { txHash: hash };
    }),
  };

  /** What TransferService / RefundService pass as the action. */
  const transfer = vi.fn(async () => {
    seen.atTransfer.push({ delegate: blockOfLatest('delegate'), trx: blockOfLatest('trx') });
    if (o.transferThrows) throw new Error('broadcast failed');
    return { txHash: broadcast('transfer') };
  });

  const sleep = vi.fn(async (ms: number) => {
    clockMs += ms;
    produceBlock();
  });

  const service = new EnergyDelegationService({
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    client: client as any,
    resources,
    sweeperAddress: SWEEPER,
    sweeperPrivateKey: DUMMY_SWEEPER_KEY,
    fallbackAmountSun: FALLBACK_SUN,
    transferBroadcastDeadlineMs: o.transferBroadcastDeadlineMs,
    sleep,
    now: () => clockMs,
  });

  const firstBroadcast = (kind: TxKind) => broadcasts.find((b) => b.kind === kind)!;
  return {
    service,
    client,
    resources,
    transfer,
    seen,
    blockOf,
    produceBlock,
    firstBroadcast,
    run: () => service.withDelegation(TRANSFER, transfer, CONTEXT),
  };
}

// ---------------------------------------------------------------------------

describe('activation — a TRC-20-only deposit is not an account', () => {
  it('creates the account with 1 SUN and plans only once the activation is in a block', async () => {
    const chain = buildChain({
      depositExists: false,
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
    });

    const outcome = await chain.run();

    expect(chain.client.sendTrx).toHaveBeenNthCalledWith(1, {
      fromAddress: SWEEPER,
      fromPrivateKey: DUMMY_SWEEPER_KEY,
      toAddress: DEPOSIT,
      amountSun: 1,
    });
    expect(outcome.activationSun).toBe(1);
    // The plan simulates a transfer FROM the deposit; a node that has not
    // applied the activation does not know that account.
    expect(chain.seen.activationBlockAtPlan[0]).not.toBeNull();
  });

  it('does not re-activate an existing account', async () => {
    const chain = buildChain({ delegatableSun: DELEGATION_64K_MAINNET_SUN });

    const outcome = await chain.run();

    expect(chain.client.sendTrx).not.toHaveBeenCalled();
    expect(outcome.activationSun).toBe(0);
  });

  it('stops (retryable) when no block takes the activation, although the node already shows the account', async () => {
    const chain = buildChain({ depositExists: false, neverInBlock: ['activation'] });

    await expect(chain.run()).rejects.toMatchObject({
      code: 'DEPOSIT_ACTIVATION_NOT_CONFIRMED',
      retryable: true,
    });
    expect(chain.resources.estimateTransferEnergy).not.toHaveBeenCalled();
    expect(chain.transfer).not.toHaveBeenCalled();
  });

  it('stops (retryable) when the activation is in a block but account reads never show it', async () => {
    const chain = buildChain({ depositExists: false, activationReadLags: true });

    await expect(chain.run()).rejects.toMatchObject({
      code: 'DEPOSIT_ACTIVATION_NOT_CONFIRMED',
      message: expect.stringContaining('does not show it'),
    });
    expect(chain.transfer).not.toHaveBeenCalled();
  });
});

describe('delegate path — the stake covers the whole transfer', () => {
  it.each([
    {
      name: 'mainnet ratio',
      ratio: MAINNET_RATIO,
      energy: 64_285,
      sun: DELEGATION_64K_MAINNET_SUN,
    },
    { name: 'nile ratio', ratio: NILE_RATIO, energy: 130_285, sun: DELEGATION_130K_NILE_SUN },
  ])(
    'delegates exactly $sun SUN, transfers, and reclaims the same amount ($name)',
    async ({ ratio, energy, sun }) => {
      // A stake of EXACTLY the planned amount must be enough (pins `>=`).
      const chain = buildChain({ ratio, energyRequired: energy, delegatableSun: sun });

      const outcome = await chain.run();

      expect(chain.client.delegateEnergy).toHaveBeenCalledWith({
        ownerAddress: SWEEPER,
        ownerPrivateKey: DUMMY_SWEEPER_KEY,
        receiverAddress: DEPOSIT,
        amountSun: sun,
      });
      expect(chain.transfer).toHaveBeenCalledOnce();
      expect(chain.client.undelegateEnergy).toHaveBeenCalledWith(
        expect.objectContaining({ amountSun: sun }),
      );
      expect(chain.client.sendTrx).not.toHaveBeenCalled();
      expect(outcome).toMatchObject({
        mode: 'delegated',
        delegationAmountSun: sun,
        fallbackAmountSun: 0,
      });
    },
  );

  it('simulates the exact transfer it was given', async () => {
    const chain = buildChain({ delegatableSun: DELEGATION_64K_MAINNET_SUN });

    await chain.run();

    expect(chain.resources.estimateTransferEnergy).toHaveBeenCalledWith(
      CONTRACT,
      DEPOSIT,
      RECIPIENT,
      '100000000',
    );
  });

  it('broadcasts the transfer only once the delegation is in a block, and reclaims only once the transfer is', async () => {
    const chain = buildChain({ delegatableSun: DELEGATION_64K_MAINNET_SUN });

    await chain.run();
    chain.produceBlock(); // let the reclaim land

    expect(chain.seen.atTransfer[0].delegate).not.toBeNull();
    expect(chain.seen.transferBlockAtReclaim[0]).not.toBeNull();
    const block = (kind: TxKind) => chain.blockOf.get(chain.firstBroadcast(kind).hash)!;
    expect(block('delegate')).toBeLessThan(block('transfer'));
    expect(block('transfer')).toBeLessThan(block('undelegate'));
  });

  it('reclaims and burns when the delegation lands but delivers less Energy than the transfer needs', async () => {
    const chain = buildChain({
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      delegationDelivers: 0.5,
    });

    const outcome = await chain.run();

    expect(chain.client.undelegateEnergy).toHaveBeenCalledOnce();
    expect(chain.client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: BURN_TOP_UP_64K_SUN }),
    );
    expect(chain.seen.atTransfer[0].trx).not.toBeNull();
    expect(outcome).toMatchObject({
      mode: 'burn',
      delegationAmountSun: 0,
      fallbackAmountSun: BURN_TOP_UP_64K_SUN,
    });
  });

  it('checks the arrived Energy against the WHOLE transfer, not the delegated shortfall', async () => {
    // The deposit already holds 30,000: shortfall 34,285 → 34,285 ÷ 9.52 × 1.1
    // = 3,961.5 → 3,962 TRX. Delivering 60% of it gives ⌊3,962 × 9.52 × 0.6⌋ =
    // 22,630, so the deposit has 52,630 — more than the shortfall, less than the
    // 64,285 the transfer consumes. Burn top-up: 34,285 × 100 SUN + 10% = 3,771,350.
    const chain = buildChain({
      depositEnergy: 30_000,
      delegatableSun: 3_962_000_000,
      delegationDelivers: 0.6,
    });

    const outcome = await chain.run();

    expect(chain.client.undelegateEnergy).toHaveBeenCalledOnce();
    expect(chain.client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: 3_771_350 }),
    );
    expect(outcome.mode).toBe('burn');
  });

  it('outlasts the expiration, then reclaims and burns, when no block takes the delegation although the node shows the Energy', async () => {
    const chain = buildChain({
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      neverInBlock: ['delegate'],
    });

    const outcome = await chain.run();

    // Giving up sooner could leave a delegation that lands later, stranded.
    const waited = chain.firstBroadcast('undelegate').atMs - chain.firstBroadcast('delegate').atMs;
    expect(waited).toBeGreaterThan(TX_EXPIRATION_MS);
    expect(chain.seen.atTransfer[0].trx).not.toBeNull();
    expect(outcome.mode).toBe('burn');
  });

  it('burns when the delegation broadcast itself is rejected', async () => {
    const chain = buildChain({
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      delegateThrows: true,
    });

    const outcome = await chain.run();

    expect(chain.client.undelegateEnergy).not.toHaveBeenCalled();
    expect(outcome.mode).toBe('burn');
  });

  it('reclaims before re-throwing when the transfer broadcast fails', async () => {
    const chain = buildChain({
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      transferThrows: true,
    });

    await expect(chain.run()).rejects.toThrow('broadcast failed');
    expect(chain.client.undelegateEnergy).toHaveBeenCalledOnce();
  });

  it('still returns the transfer when only the reclaim fails', async () => {
    const chain = buildChain({
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      undelegateThrows: true,
    });

    const outcome = await chain.run();

    expect(outcome.action.txHash).toBe(chain.firstBroadcast('transfer').hash);
    expect(outcome.mode).toBe('delegated');
  });

  it('reclaims once the transfer can no longer land, even if no block ever shows it', async () => {
    const chain = buildChain({
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      neverInBlock: ['transfer'],
    });

    const outcome = await chain.run();

    const waited = chain.firstBroadcast('undelegate').atMs - chain.firstBroadcast('transfer').atMs;
    expect(waited).toBeGreaterThan(TX_EXPIRATION_MS);
    expect(outcome.mode).toBe('delegated');
  });

  it('tops up Bandwidth when the deposit has used its free allowance', async () => {
    const chain = buildChain({
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      depositBandwidth: 100,
    });

    const outcome = await chain.run();

    expect(chain.client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: BANDWIDTH_TOP_UP_SUN }),
    );
    expect(chain.seen.atTransfer[0].trx).not.toBeNull();
    expect(outcome).toMatchObject({
      mode: 'delegated',
      delegationAmountSun: DELEGATION_64K_MAINNET_SUN,
      fallbackAmountSun: BANDWIDTH_TOP_UP_SUN,
    });
  });

  it('reclaims the delegation when the Bandwidth top-up never lands', async () => {
    const chain = buildChain({
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      depositBandwidth: 100,
      neverInBlock: ['trx'],
    });

    await expect(chain.run()).rejects.toMatchObject({ code: 'TRX_TOP_UP_NOT_CONFIRMED' });
    expect(chain.client.undelegateEnergy).toHaveBeenCalledOnce();
    expect(chain.transfer).not.toHaveBeenCalled();
  });
});

describe('burn path — the stake cannot cover the whole transfer', () => {
  it('sends exactly the TRX the whole transfer burns when the stake is one SUN short — never a partial delegation', async () => {
    const chain = buildChain({ delegatableSun: DELEGATION_64K_MAINNET_SUN - 1 });

    const outcome = await chain.run();

    expect(chain.client.delegateEnergy).not.toHaveBeenCalled();
    expect(chain.client.sendTrx).toHaveBeenCalledWith({
      fromAddress: SWEEPER,
      fromPrivateKey: DUMMY_SWEEPER_KEY,
      toAddress: DEPOSIT,
      amountSun: BURN_TOP_UP_64K_SUN,
    });
    expect(chain.transfer).toHaveBeenCalledOnce();
    expect(outcome).toMatchObject({
      mode: 'burn',
      delegationAmountSun: 0,
      fallbackAmountSun: BURN_TOP_UP_64K_SUN,
    });
  });

  it.each([
    // 64,285 × 100 SUN + 10%, nothing held.
    { energy: 64_285, balance: 0, expected: 7_071_350 },
    // 130,285 × 100 SUN = 13,028,500, + 10% = 14,331,350, − 4,000,000 held.
    { energy: 130_285, balance: 4_000_000, expected: 10_331_350 },
  ])(
    'sends $expected SUN for $energy Energy when the deposit holds $balance SUN',
    async ({ energy, balance, expected }) => {
      const chain = buildChain({ energyRequired: energy, depositBalanceSun: balance });

      await chain.run();

      expect(chain.client.sendTrx).toHaveBeenCalledWith(
        expect.objectContaining({ amountSun: expected }),
      );
    },
  );

  it('sends nothing when the deposit already holds enough TRX', async () => {
    const chain = buildChain({ depositBalanceSun: 20_000_000 });

    const outcome = await chain.run();

    expect(chain.client.sendTrx).not.toHaveBeenCalled();
    expect(outcome).toMatchObject({ mode: 'burn', fallbackAmountSun: 0 });
  });

  it('broadcasts the transfer only once the top-up is in a block', async () => {
    const chain = buildChain();

    await chain.run();

    expect(chain.seen.atTransfer[0].trx).not.toBeNull();
  });

  it('does not broadcast the transfer when no block takes the top-up, although the node shows the balance', async () => {
    const chain = buildChain({ neverInBlock: ['trx'] });

    await expect(chain.run()).rejects.toMatchObject({
      code: 'TRX_TOP_UP_NOT_CONFIRMED',
      retryable: true,
    });
    expect(chain.transfer).not.toHaveBeenCalled();
  });
});

describe('no-energy path — the deposit already holds the Energy', () => {
  it('sends nothing when the deposit covers the whole transfer and has Bandwidth', async () => {
    const chain = buildChain({ depositEnergy: 70_000, delegatableSun: DELEGATION_64K_MAINNET_SUN });

    const outcome = await chain.run();

    expect(chain.client.delegateEnergy).not.toHaveBeenCalled();
    expect(chain.client.sendTrx).not.toHaveBeenCalled();
    expect(outcome).toMatchObject({ mode: 'no-energy', fallbackAmountSun: 0 });
  });

  it('tops up only Bandwidth when the free allowance is used up', async () => {
    const chain = buildChain({ depositEnergy: 70_000, depositBandwidth: 100 });

    const outcome = await chain.run();

    expect(chain.client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: BANDWIDTH_TOP_UP_SUN }),
    );
    expect(outcome).toMatchObject({ mode: 'no-energy', fallbackAmountSun: BANDWIDTH_TOP_UP_SUN });
  });
});

describe('plan unavailable — fixed fallback keeps the money path open', () => {
  it('sends the configured fallback when the simulation fails, and transfers once it is in a block', async () => {
    const chain = buildChain({
      simulationThrows: true,
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
    });

    const outcome = await chain.run();

    expect(chain.client.delegateEnergy).not.toHaveBeenCalled();
    expect(chain.client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: FALLBACK_SUN }),
    );
    expect(chain.seen.atTransfer[0].trx).not.toBeNull();
    expect(outcome).toMatchObject({ mode: 'fallback', fallbackAmountSun: FALLBACK_SUN });
  });

  it('raises DELEGATION_AND_FALLBACK_FAILED (retryable) when the TRX cannot be sent', async () => {
    const chain = buildChain({ simulationThrows: true, sendTrxThrows: true });

    await expect(chain.run()).rejects.toMatchObject({
      code: 'DELEGATION_AND_FALLBACK_FAILED',
      retryable: true,
    });
  });
});

describe('broadcast deadline — the backend must still be waiting', () => {
  it('reclaims and asks for a retry instead of broadcasting past the deadline', async () => {
    const chain = buildChain({
      depositExists: false,
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      transferBroadcastDeadlineMs: 3_000,
    });

    await expect(chain.run()).rejects.toMatchObject({
      code: 'TRANSFER_WINDOW_ELAPSED',
      retryable: true,
    });
    expect(chain.transfer).not.toHaveBeenCalled();
    expect(chain.client.undelegateEnergy).toHaveBeenCalledOnce();
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
