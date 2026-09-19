import { describe, it, expect, vi } from 'vitest';
import { EnergyDelegationService } from './EnergyDelegationService.js';
import { SimulationRevertedError } from '../errors/SidecarError.js';

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
/** A real address: the service refuses a malformed stake account at
 * construction. The Nile stake account measured 2026-09-18. */
const STAKE_ACCOUNT = 'TVqvXQhRcmcCQudYx6uhq1WWuNHVmEEyQ6';
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

interface SignedRequest {
  ownerAddress: string;
  ownerPermissionId?: number;
  amountSun: number;
}

interface TrxSend {
  fromAddress: string;
  fromPrivateKey: string;
  toAddress: string;
  amountSun: number;
}

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
  /**
   * How many account reads after a TRX send still answer with the balance the
   * deposit had BEFORE it — TronGrid load-balances reads, so the node
   * answering can be behind the one that reported the block.
   */
  balanceReadLagPolls?: number;
  /** Milliseconds the plan probes take (the call's own clock, not block time). */
  probeDelayMs?: number;
  /** The simulation probe itself fails (no answer about the transfer). */
  simulationThrows?: boolean;
  /** The node runs the transfer and it reverts (an answer: it would fail on-chain). */
  simulationReverts?: boolean;
  feeParametersThrow?: boolean;
  delegateThrows?: boolean;
  undelegateThrows?: boolean;
  sendTrxThrows?: boolean;
  transferThrows?: boolean;
  transferBroadcastDeadlineMs?: number;
  /** Dedicated stake account (owner decision 2026-09-17). Unset = the hot
   * wallet holds its own stake, the pre-split arrangement. */
  stakeAddress?: string;
  stakePermissionId?: number;
}

function buildChain(o: ChainOptions = {}) {
  const ratio = o.ratio === undefined ? MAINNET_RATIO : o.ratio;
  // Who actually holds the frozen TRX in this scenario.
  const stakeHolder = o.stakeAddress ?? SWEEPER;
  const expectedPermissionId = o.stakeAddress ? (o.stakePermissionId ?? 2) : undefined;
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

  let balanceLagLeft = 0;
  let balanceBeforeLastSend = 0;

  /**
   * The chain checks the signature against the permission named in the
   * transaction, and rejects everything else — measured on Nile 2026-09-18:
   * offering the hot key without the permission id returns
   * <c>SIGERROR … is not contained of permission</c>, and a key that does not
   * own the account cannot sign for it at all. The fake refuses the same way,
   * so delegating from the wrong account, or with the wrong id, fails here
   * instead of quietly succeeding against a stub that ignores both.
   */
  function assertSignedForTheStake(request: SignedRequest): void {
    if (request.ownerAddress !== stakeHolder) {
      throw new Error(
        `fake chain: SIGERROR — ${request.ownerAddress} holds no stake (it is ${stakeHolder}).`,
      );
    }
    if (request.ownerPermissionId !== expectedPermissionId) {
      throw new Error(
        `fake chain: SIGERROR — signature is not contained of permission ` +
          `${request.ownerPermissionId ?? '(none)'} on ${request.ownerAddress}.`,
      );
    }
  }

  /**
   * Every TRX this flow sends leaves the hot wallet, signed by its own key.
   * The stake account's permission covers delegation and reclaim only — a TRX
   * transfer signed for it is rejected (measured on Nile 2026-09-17,
   * <c>SIGERROR "Permission denied"</c>) — so a flow that funded a deposit
   * from the stake account fails here as it would on-chain (#325 validation,
   * B3: a stub that ignored the sender let exactly that pass 321/321).
   */
  function assertSentByTheHotWallet(request: TrxSend): void {
    if (request.fromAddress !== SWEEPER || request.fromPrivateKey !== DUMMY_SWEEPER_KEY) {
      throw new Error(
        `fake chain: SIGERROR "Permission denied" — the hot wallet key cannot send TRX from ${request.fromAddress}.`,
      );
    }
  }

  const resources = {
    estimateTransferEnergy: vi.fn(async () => {
      seen.activationBlockAtPlan.push(blockOfLatest('activation'));
      clockMs += o.probeDelayMs ?? 0;
      if (o.simulationThrows) throw new Error('simulation probe answered HTTP 503');
      if (o.simulationReverts) {
        // Settle after every other probe, so a flow that raised the first
        // failure it saw would never see this one.
        await new Promise((resolve) => setTimeout(resolve, 0));
        throw new SimulationRevertedError(
          'triggerconstantcontract simulation failed: REVERT opcode executed',
          'REVERT opcode executed',
        );
      }
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
      if (!deposit.exists || o.activationReadLags) return { exists: false, balanceSun: 0 };
      if (balanceLagLeft > 0) {
        balanceLagLeft -= 1;
        return { exists: true, balanceSun: balanceBeforeLastSend };
      }
      return { exists: true, balanceSun: deposit.balanceSun };
    }),
    getDelegatableEnergySun: vi.fn(async (address: string) => {
      // The stake lives in exactly ONE account. Asking the other platform
      // account answers 0, the way the chain answers an account with nothing
      // frozen — so a read pointed at the signer instead of the stake holder
      // plans a burn where it should have planned a delegation.
      if (address === stakeHolder) return o.delegatableSun ?? 0;
      if (address === SWEEPER || address === STAKE_ACCOUNT) return 0;
      if (address === DEPOSIT) return 0;
      return unexpected(address);
    }),
    getChainFeeParameters: vi.fn(async () => {
      if (o.feeParametersThrow) throw new Error('getchainparameters answered HTTP 429');
      return { energyFeeSun: 100, bandwidthFeeSun: 1_000 };
    }),
    getTransactionBlockNumber: vi.fn(async (hash: string) => blockOf.get(hash) ?? null),
  };

  let delivered = 0;
  const client = {
    sendTrx: vi.fn(async (request: TrxSend) => {
      assertSentByTheHotWallet(request);
      if (o.sendTrxThrows) throw new Error('sendTrx rejected');
      const hash = broadcast(deposit.exists ? 'trx' : 'activation', request.amountSun);
      // Pending view: the node shows it before any block holds it.
      balanceBeforeLastSend = deposit.balanceSun;
      balanceLagLeft = o.balanceReadLagPolls ?? 0;
      deposit.exists = true;
      deposit.balanceSun += request.amountSun;
      return { txHash: hash };
    }),
    delegateEnergy: vi.fn(async (request: SignedRequest & { receiverAddress: string }) => {
      assertSignedForTheStake(request);
      if (o.delegateThrows) throw new Error('delegate rejected');
      if (!deposit.exists) throw new Error(`Account[${request.receiverAddress}] not exists`);
      const hash = broadcast('delegate', request.amountSun);
      delivered = ratio
        ? Math.floor((request.amountSun / 1_000_000) * ratio * (o.delegationDelivers ?? 1))
        : 0;
      deposit.energy += delivered;
      return { txHash: hash };
    }),
    undelegateEnergy: vi.fn(async (request: SignedRequest) => {
      assertSignedForTheStake(request);
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
    stakeAddress: o.stakeAddress,
    stakePermissionId: o.stakePermissionId,
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
    /**
     * Energy still delegated to the deposit. The flow swallows a failed
     * reclaim (the transfer is already on-chain), so a reclaim the chain
     * refused — wrong owner, wrong permission id — shows only here.
     */
    strandedEnergy: () => delivered,
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

  it('waits for the account read to catch up with the top-up block before broadcasting', async () => {
    // The block is only half the answer: TronGrid load-balances reads, so the
    // node answering the balance can be behind the one that reported the
    // block. Two lagging reads must not end the wait (default: 3 polls).
    const chain = buildChain({ balanceReadLagPolls: 2 });

    const outcome = await chain.run();

    expect(chain.seen.atTransfer[0].trx).not.toBeNull();
    expect(outcome).toMatchObject({ mode: 'burn', fallbackAmountSun: BURN_TOP_UP_64K_SUN });
  });

  it('does not broadcast when the top-up is in a block the balance read never shows', async () => {
    // A transfer broadcast against a node that has not applied the top-up is
    // rejected or fails on Energy; the retry finds the TRX already there.
    const chain = buildChain({ balanceReadLagPolls: 99 });

    await expect(chain.run()).rejects.toMatchObject({
      code: 'TRX_TOP_UP_NOT_CONFIRMED',
      retryable: true,
      message: expect.stringContaining('does not show it'),
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

  it('sends the configured fallback when another probe fails and the simulation succeeds', async () => {
    const chain = buildChain({
      feeParametersThrow: true,
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
    });

    const outcome = await chain.run();

    expect(chain.client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ amountSun: FALLBACK_SUN }),
    );
    expect(outcome.mode).toBe('fallback');
  });

  it('raises DELEGATION_AND_FALLBACK_FAILED (retryable) when the TRX cannot be sent', async () => {
    const chain = buildChain({ simulationThrows: true, sendTrxThrows: true });

    await expect(chain.run()).rejects.toMatchObject({
      code: 'DELEGATION_AND_FALLBACK_FAILED',
      retryable: true,
    });
  });
});

describe('the transfer itself would revert — nothing is sent (owner decision 2026-09-17)', () => {
  it('sends no fallback and broadcasts nothing, and asks for a retry', async () => {
    // The likeliest cause: an earlier attempt moved the tokens and the backend
    // never recorded it. The fixed fallback used to strand 15 TRX in the
    // deposit and put a transfer on-chain that could only revert.
    const chain = buildChain({
      simulationReverts: true,
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
    });

    await expect(chain.run()).rejects.toMatchObject({
      code: 'DEPOSIT_TRANSFER_WOULD_REVERT',
      retryable: true,
      message: expect.stringContaining('nothing was sent or broadcast'),
    });
    expect(chain.client.sendTrx).not.toHaveBeenCalled();
    expect(chain.client.delegateEnergy).not.toHaveBeenCalled();
    expect(chain.transfer).not.toHaveBeenCalled();
  });

  it('is not lost to another probe that fails first', async () => {
    // The fee probe rejects at once, the revert only after it; raising the
    // first failure would take the fixed-fallback path.
    const chain = buildChain({
      simulationReverts: true,
      feeParametersThrow: true,
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
    });

    await expect(chain.run()).rejects.toMatchObject({ code: 'DEPOSIT_TRANSFER_WOULD_REVERT' });
    expect(chain.client.sendTrx).not.toHaveBeenCalled();
    expect(chain.transfer).not.toHaveBeenCalled();
  });
});

describe('broadcast deadline — the backend must still be waiting', () => {
  it.each([
    { name: 'exactly at the deadline', elapsedMs: 150_000, broadcasts: true },
    { name: 'one millisecond past it', elapsedMs: 150_001, broadcasts: false },
  ])(
    'the DEFAULT deadline is 150 s — $name broadcasts: $broadcasts',
    async ({ elapsedMs, broadcasts }) => {
      // Pinned on both sides, because nothing else pins it: the backend's
      // transfer budget (BlockchainSidecarOptions.DefaultTransferTimeoutSeconds
      // = 300 s) is sized as this window plus the ~75 s block wait that follows
      // a broadcast. Raising this default alone would let the sidecar broadcast
      // into a call the backend has already abandoned and retried.
      // No delegation, no top-up on this path, so the call's whole elapsed time
      // is the probe delay.
      const chain = buildChain({ depositEnergy: 70_000, probeDelayMs: elapsedMs });

      if (broadcasts) {
        const outcome = await chain.run();
        expect(outcome.mode).toBe('no-energy');
        expect(chain.transfer).toHaveBeenCalledOnce();
      } else {
        await expect(chain.run()).rejects.toMatchObject({ code: 'TRANSFER_WINDOW_ELAPSED' });
        expect(chain.transfer).not.toHaveBeenCalled();
      }
    },
  );

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

/**
 * #325 validation, B3. With a stake account configured, every path that SENDS
 * TRX must still send it from the hot wallet — the stake account's permission
 * denies transfers — and every delegation must come back. Before the fake
 * checked the sender, pointing the burn top-up and the fixed fallback at the
 * stake account left the suite green; on-chain each of those transfers would
 * have stopped at DELEGATION_AND_FALLBACK_FAILED.
 */
describe('stake account configured — TRX leaves the hot wallet on every path', () => {
  it.each([
    {
      path: 'activation, then delegation',
      options: { depositExists: false, delegatableSun: DELEGATION_64K_MAINNET_SUN },
      mode: 'delegated',
      sent: [1],
    },
    {
      path: 'delegation with a Bandwidth top-up',
      options: { delegatableSun: DELEGATION_64K_MAINNET_SUN, depositBandwidth: 100 },
      mode: 'delegated',
      sent: [BANDWIDTH_TOP_UP_SUN],
    },
    {
      path: 'burn — the stake is one SUN short',
      options: { delegatableSun: DELEGATION_64K_MAINNET_SUN - 1 },
      mode: 'burn',
      sent: [BURN_TOP_UP_64K_SUN],
    },
    {
      path: 'burn after a delegation that delivered too little',
      options: { delegatableSun: DELEGATION_64K_MAINNET_SUN, delegationDelivers: 0.5 },
      mode: 'burn',
      sent: [BURN_TOP_UP_64K_SUN],
    },
    {
      path: 'no Energy needed, Bandwidth top-up only',
      options: { depositEnergy: 70_000, depositBandwidth: 100 },
      mode: 'no-energy',
      sent: [BANDWIDTH_TOP_UP_SUN],
    },
    {
      path: 'fixed fallback — the plan could not be computed',
      options: { simulationThrows: true, delegatableSun: DELEGATION_64K_MAINNET_SUN },
      mode: 'fallback',
      sent: [FALLBACK_SUN],
    },
  ])('$path', async ({ options, mode, sent }) => {
    const chain = buildChain({ stakeAddress: STAKE_ACCOUNT, ...options });

    const outcome = await chain.run();

    expect(outcome.mode).toBe(mode);
    expect(chain.transfer).toHaveBeenCalledOnce();
    expect(chain.client.sendTrx.mock.calls.map(([request]) => request)).toEqual(
      sent.map((amountSun) => ({
        fromAddress: SWEEPER,
        fromPrivateKey: DUMMY_SWEEPER_KEY,
        toAddress: DEPOSIT,
        amountSun,
      })),
    );
    expect(chain.strandedEnergy()).toBe(0);
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

  /**
   * The stake split (owner decision 2026-09-17). Two halves have to move
   * together and only those two: the delegation is owned by the stake account
   * and signed against its permission, while every TRX this flow SENDS still
   * leaves the hot wallet — the stake account's permission denies transfers, so
   * a flow that tried to fund the deposit from it would be rejected on-chain.
   */
  it('delegates from the stake account while the TRX still leaves the hot wallet', async () => {
    const chain = buildChain({
      stakeAddress: STAKE_ACCOUNT,
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
      depositExists: false,
    });

    const outcome = await chain.run();

    expect(outcome.mode).toBe('delegated');
    expect(chain.client.delegateEnergy).toHaveBeenCalledWith(
      expect.objectContaining({ ownerAddress: STAKE_ACCOUNT, ownerPermissionId: 2 }),
    );
    expect(chain.client.undelegateEnergy).toHaveBeenCalledWith(
      expect.objectContaining({ ownerAddress: STAKE_ACCOUNT, ownerPermissionId: 2 }),
    );
    // The activation TRX: hot wallet, signing for itself.
    expect(chain.client.sendTrx).toHaveBeenCalledWith(
      expect.objectContaining({ fromAddress: SWEEPER, fromPrivateKey: DUMMY_SWEEPER_KEY }),
    );
    expect(chain.strandedEnergy()).toBe(0);
  });

  it('reads the delegatable stake from the account that holds it', async () => {
    const chain = buildChain({
      stakeAddress: STAKE_ACCOUNT,
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
    });

    await chain.run();

    // Pointed at the hot wallet this answers 0 and the flow burns instead.
    expect(chain.resources.getDelegatableEnergySun).toHaveBeenCalledWith(STAKE_ACCOUNT);
  });

  it('carries a non-default permission id rather than assuming 2 — for the reclaim too', async () => {
    const chain = buildChain({
      stakeAddress: STAKE_ACCOUNT,
      stakePermissionId: 5,
      delegatableSun: DELEGATION_64K_MAINNET_SUN,
    });

    const outcome = await chain.run();

    expect(outcome.mode).toBe('delegated');
    expect(chain.client.delegateEnergy).toHaveBeenCalledWith(
      expect.objectContaining({ ownerAddress: STAKE_ACCOUNT, ownerPermissionId: 5 }),
    );
    // A reclaim offered against a fixed 2 is refused on-chain and the flow
    // swallows it: every delegation would stay with its deposit (#325
    // validation — only the delegation was checked).
    expect(chain.client.undelegateEnergy).toHaveBeenCalledWith(
      expect.objectContaining({ ownerAddress: STAKE_ACCOUNT, ownerPermissionId: 5 }),
    );
    expect(chain.strandedEnergy()).toBe(0);
  });

  it('keeps the pre-split arrangement when no stake account is configured', async () => {
    const chain = buildChain({ delegatableSun: DELEGATION_64K_MAINNET_SUN });

    const outcome = await chain.run();

    expect(outcome.mode).toBe('delegated');
    // No permission id at all — the hot wallet owns the stake and signs for
    // itself, which is the only case trx.sign accepts.
    expect(chain.client.delegateEnergy).toHaveBeenCalledWith(
      expect.objectContaining({ ownerAddress: SWEEPER, ownerPermissionId: undefined }),
    );
    expect(chain.resources.getDelegatableEnergySun).toHaveBeenCalledWith(SWEEPER);
  });

  /**
   * Owner decision 2026-09-19, as TransferGuard does for a pinned destination.
   * A malformed stake configuration never fails loudly by itself: every
   * delegation is rejected on-chain, the flow burns instead, and the only
   * trace is one WARN line per transfer (#325 validation — a blank id outside
   * compose became NaN and nothing checked it).
   */
  describe('a malformed stake configuration stops construction', () => {
    const construct = (stakeAddress: string | undefined, stakePermissionId: number) =>
      new EnergyDelegationService({
        client: {} as never,
        resources: {} as never,
        sweeperAddress: SWEEPER,
        sweeperPrivateKey: DUMMY_SWEEPER_KEY,
        stakeAddress,
        stakePermissionId,
        fallbackAmountSun: FALLBACK_SUN,
      });
    const constructionError = (stakeAddress: string, stakePermissionId: number): unknown => {
      try {
        construct(stakeAddress, stakePermissionId);
      } catch (err) {
        return err;
      }
      return undefined;
    };

    it.each([
      { name: 'a blank id read as NaN', id: Number.NaN },
      { name: 'the owner permission (0)', id: 0 },
      { name: 'the witness permission (1)', id: 1 },
      { name: 'a fraction', id: 2.5 },
    ])('refuses $name', ({ id }) => {
      expect(constructionError(STAKE_ACCOUNT, id)).toMatchObject({
        code: 'STAKE_ACCOUNT_MISCONFIGURED',
        retryable: false,
        message: expect.stringContaining('STAKE_ACCOUNT_PERMISSION_ID'),
      });
    });

    it('refuses a stake address that is not a Tron address', () => {
      expect(constructionError('TStakeAccountHoldingTheFrozenTrx', 2)).toMatchObject({
        code: 'STAKE_ACCOUNT_MISCONFIGURED',
        message: expect.stringContaining('STAKE_ACCOUNT_ADDRESS'),
      });
    });

    it('accepts the first active permission id', () => {
      expect(construct(STAKE_ACCOUNT, 2).delegationOwner).toBe(STAKE_ACCOUNT);
    });

    it('ignores the id while no stake account is configured', () => {
      expect(construct(undefined, Number.NaN).delegationOwner).toBe(SWEEPER);
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
