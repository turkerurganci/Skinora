import { describe, it, expect, vi } from 'vitest';
import { TronResourceClient } from './TronResourceClient.js';
import { SidecarError, SimulationRevertedError } from '../errors/SidecarError.js';

/**
 * These probes back the money-path fee estimate, and both branches covered
 * here are ones where the chain answers HTTP 200 with something that LOOKS
 * like an answer and is not:
 *
 * <list type="bullet">
 *   <item>a reverting `triggerconstantcontract` still reports
 *     `result.result: true` (measured on Nile 2026-09-06: energy_used 1984 for
 *     a revert vs 29650 for the same transfer succeeding);</item>
 *   <item>`getcontract` on a non-contract address returns `{}`, which the
 *     "omitted percent means 0" rule would read as "the owner pays".</item>
 * </list>
 *
 * Accepted, either one silently drives the charge to 0.00 and the platform
 * absorbs the real cost.
 */

const CONTRACT = 'TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf';
const SENDER = 'TP6e9Yqa1wFFDbJzKaSgTwBq2LHax9YSFD';
const RECIPIENT = 'TWrbG7F38xPMty4jRhgnBxrfAPtS84KmHK';

function fetchReturning(body: unknown, ok = true, status = 200) {
  return vi.fn(async () =>
    Promise.resolve({
      ok,
      status,
      json: async () => body,
    } as Response),
  );
}

function client() {
  return new TronResourceClient('https://nile.example', 'test-key');
}

describe('TronResourceClient.estimateTransferEnergy — a reverted simulation is not a cost', () => {
  it('returns the energy of a successful simulation', async () => {
    // Shape measured on Nile: no message, ret entries empty.
    const fetchFn = fetchReturning({
      result: { result: true },
      energy_used: 29_650,
      transaction: { ret: [{}] },
    });

    await expect(
      client().estimateTransferEnergy(CONTRACT, SENDER, RECIPIENT, '10200000', fetchFn),
    ).resolves.toBe(29_650);
  });

  it('rejects a revert even though the node answers result.result = true', async () => {
    // Verbatim Nile response for a transfer the sender cannot cover.
    const fetchFn = fetchReturning({
      result: { result: true, message: 'REVERT opcode executed' },
      energy_used: 1984,
      transaction: { ret: [{ ret: 'FAILED' }] },
    });

    const error = await client()
      .estimateTransferEnergy(CONTRACT, SENDER, RECIPIENT, '10200000', fetchFn)
      .catch((err: unknown) => err);

    // The node RAN the transfer: an answer about it, typed so the deposit flow
    // does not mistake it for a probe outage.
    expect(error).toBeInstanceOf(SimulationRevertedError);
    expect((error as SimulationRevertedError).reason).toBe('REVERT opcode executed');
    expect((error as SidecarError).code).toBe('FEE_ESTIMATE_SIMULATION_FAILED');
    // Retryable → the handler answers 502 → the backend charges the static
    // fallback. Undercharging by 15x is the outcome this prevents.
    expect((error as SidecarError).retryable).toBe(true);
    expect((error as SidecarError).message).toContain('REVERT opcode executed');
  });

  it("types mainnet Tether's measured revert shape as a revert", async () => {
    // Verbatim fields, mainnet 2026-09-17: a 1 USDT transfer from an address
    // holding none.
    const fetchFn = fetchReturning({
      result: { result: true, message: 'REVERT opcode executed' },
      energy_used: 8624,
      constant_result: [''],
      transaction: { ret: [{ ret: 'FAILED' }] },
    });

    await expect(
      client().estimateTransferEnergy(CONTRACT, SENDER, RECIPIENT, '1000000', fetchFn),
    ).rejects.toBeInstanceOf(SimulationRevertedError);
  });

  it('rejects a failed ret even when the node volunteers no message', async () => {
    const fetchFn = fetchReturning({
      result: { result: true },
      energy_used: 1984,
      transaction: { ret: [{ ret: 'OUT_OF_ENERGY' }] },
    });

    const error = await client()
      .estimateTransferEnergy(CONTRACT, SENDER, RECIPIENT, '10200000', fetchFn)
      .catch((err: unknown) => err);

    expect(error).toBeInstanceOf(SimulationRevertedError);
    expect((error as Error).message).toMatch(/OUT_OF_ENERGY/);
  });

  it.each([
    // Measured on mainnet 2026-09-17: the node refuses the call itself.
    {
      name: 'a malformed address (OTHER_ERROR)',
      body: {
        result: {
          code: 'OTHER_ERROR',
          message:
            'class org.tron.core.services.http.JsonFormat$ParseException : 1:18: invalid address for field: protocol.TriggerSmartContract.owner_address',
        },
      },
    },
    {
      name: 'a missing contract (CONTRACT_VALIDATE_ERROR)',
      body: {
        result: { code: 'CONTRACT_VALIDATE_ERROR', message: 'Smart contract is not exist.' },
      },
    },
    { name: 'a response without energy_used', body: { result: { result: true } } },
  ])(
    'does not call $name a revert — the probe failed, the transfer was never run',
    async ({ body }) => {
      const error = await client()
        .estimateTransferEnergy(CONTRACT, SENDER, RECIPIENT, '10200000', fetchReturning(body))
        .catch((err: unknown) => err);

      expect(error).toBeInstanceOf(SidecarError);
      expect(error).not.toBeInstanceOf(SimulationRevertedError);
      expect((error as SidecarError).code).toBe('FEE_ESTIMATE_SIMULATION_FAILED');
    },
  );
});

describe('TronResourceClient.getContractEnergyPolicy — who pays the energy', () => {
  it('reads an absent percent as "the owner pays" on a real contract', async () => {
    // The Nile test USDT: consume_user_resource_percent is omitted, meaning 0.
    const fetchFn = fetchReturning({
      contract_address: CONTRACT,
      name: 'TetherToken',
      origin_energy_limit: 50_000,
    });

    await expect(client().getContractEnergyPolicy(CONTRACT, fetchFn)).resolves.toEqual({
      callerPercent: 0,
      originEnergyLimit: 50_000,
      originAddress: null,
    });
  });

  it("reads mainnet Tether's measured shape, including the owner whose Energy pays its share", async () => {
    // Verbatim fields of mainnet getcontract TR7NHq… on 2026-09-17 (bytecode/abi omitted).
    const fetchFn = fetchReturning({
      origin_address: 'THPvaUhoh2Qn2y9THCZML3H815hhFhn5YC',
      contract_address: 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t',
      consume_user_resource_percent: 30,
      name: 'TetherToken',
      origin_energy_limit: 10_000_000,
    });

    await expect(client().getContractEnergyPolicy(CONTRACT, fetchFn)).resolves.toEqual({
      callerPercent: 30,
      originEnergyLimit: 10_000_000,
      originAddress: 'THPvaUhoh2Qn2y9THCZML3H815hhFhn5YC',
    });
  });

  it('reads an explicit percent, clamped to 0-100', async () => {
    const fetchFn = fetchReturning({
      contract_address: CONTRACT,
      consume_user_resource_percent: 100,
    });

    await expect(client().getContractEnergyPolicy(CONTRACT, fetchFn)).resolves.toMatchObject({
      callerPercent: 100,
    });
  });

  it('treats an empty body as a failed probe, not as "the owner pays"', async () => {
    // What Nile returns for an address that is not a contract at all — i.e. a
    // mistyped or unmigrated contract setting. Indistinguishable from the
    // legitimate omitted-percent case above except for the missing identity.
    const fetchFn = fetchReturning({});

    const error = await client()
      .getContractEnergyPolicy(CONTRACT, fetchFn)
      .catch((err: unknown) => err);

    expect(error).toBeInstanceOf(SidecarError);
    expect((error as SidecarError).code).toBe('FEE_ESTIMATE_CONTRACT_NOT_FOUND');
  });
});

describe('TronResourceClient.getTransactionBlockNumber — in a block, or not yet', () => {
  it('returns the block of an included transaction (measured shape)', async () => {
    // Keys measured on mainnet 2026-09-17 for a transaction of the latest block
    // (tx 08cfbdc2…; the id is shortened — a 64-hex literal trips the secret scan).
    const fetchFn = fetchReturning({
      id: '08cfbdc250037382…',
      blockNumber: 86_324_100,
      blockTimeStamp: 1_789_645_398_000,
      contractResult: [''],
      receipt: { net_usage: 281 },
    });

    await expect(client().getTransactionBlockNumber('08cf…', fetchFn)).resolves.toBe(86_324_100);
  });

  it('reads the empty body as "no block holds it yet" — the measured answer for an unknown hash', async () => {
    const fetchFn = fetchReturning({});

    await expect(client().getTransactionBlockNumber('dead…', fetchFn)).resolves.toBeNull();
  });

  it.each([
    { name: 'zero', blockNumber: 0 },
    { name: 'a fraction', blockNumber: 1.5 },
    { name: 'a string', blockNumber: '86324100' },
  ])('never reads $name as a block', async ({ blockNumber }) => {
    const fetchFn = fetchReturning({ id: 'abc', blockNumber });

    await expect(client().getTransactionBlockNumber('abc', fetchFn)).resolves.toBeNull();
  });

  it('asks the full node, not the solidity node, by transaction id', async () => {
    // The solidity node lags ~19 blocks; the flow needs "in a block", not "final".
    const fetchFn = fetchReturning({});

    await client().getTransactionBlockNumber('abc123', fetchFn);

    const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('https://nile.example/wallet/gettransactioninfobyid');
    expect(JSON.parse(init.body as string)).toEqual({ value: 'abc123' });
  });
});

describe('TronResourceClient.getAccountResources', () => {
  it('nets usage out of both bandwidth allowances and derives the network ratio', async () => {
    const fetchFn = fetchReturning({
      freeNetLimit: 600,
      freeNetUsed: 100,
      NetLimit: 400,
      NetUsed: 400,
      EnergyLimit: 70_000,
      EnergyUsed: 5_000,
      TotalEnergyLimit: 180_000_000_000,
      TotalEnergyWeight: 18_810_000_000,
    });

    const resources = await client().getAccountResources(SENDER, fetchFn);

    expect(resources.energyAvailable).toBe(65_000);
    expect(resources.bandwidthAvailable).toBe(500);
    expect(resources.energyPerTrx).toBeCloseTo(9.57, 2);
  });

  it('reads an unactivated account as zero of everything, ratio unknown', async () => {
    const fetchFn = fetchReturning({});

    await expect(client().getAccountResources(SENDER, fetchFn)).resolves.toEqual({
      energyAvailable: 0,
      bandwidthAvailable: 0,
      energyPerTrx: null,
    });
  });
});

describe('TronResourceClient.getAccountState — a TRC-20-only address is not an account', () => {
  it('reads the measured non-existent shape (HTTP 200, empty body) as not existing', async () => {
    // Measured on Nile 2026-09-16 for a derived deposit that had never
    // received TRX: `{}`. Delegation to it and a transfer from it were both
    // rejected at validation.
    const fetchFn = fetchReturning({});

    await expect(client().getAccountState(SENDER, fetchFn)).resolves.toEqual({
      exists: false,
      balanceSun: 0,
    });
  });

  it('reads an existing account with its balance', async () => {
    // Shape measured on Nile 2026-09-16 right after activation with 1 SUN.
    const fetchFn = fetchReturning({ address: SENDER, balance: 1, create_time: 1789579602000 });

    await expect(client().getAccountState(SENDER, fetchFn)).resolves.toEqual({
      exists: true,
      balanceSun: 1,
    });
  });

  it('reads an existing account without a balance field as 0 SUN', async () => {
    const fetchFn = fetchReturning({ address: SENDER });

    await expect(client().getAccountState(SENDER, fetchFn)).resolves.toEqual({
      exists: true,
      balanceSun: 0,
    });
  });

  it('queries getaccount for the given address', async () => {
    const fetchFn = fetchReturning({});

    await client().getAccountState(SENDER, fetchFn);

    const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('https://nile.example/wallet/getaccount');
    expect(JSON.parse(init.body as string)).toEqual({ address: SENDER, visible: true });
  });
});

describe('TronResourceClient.getDelegatableEnergySun', () => {
  it('returns max_size as measured with 100 TRX staked', async () => {
    const fetchFn = fetchReturning({ max_size: 100_000_000 });

    await expect(client().getDelegatableEnergySun(SENDER, fetchFn)).resolves.toBe(100_000_000);
  });

  it('reads the measured no-stake shape (empty body) as 0 — the burn path', async () => {
    const fetchFn = fetchReturning({});

    await expect(client().getDelegatableEnergySun(SENDER, fetchFn)).resolves.toBe(0);
  });

  it('asks for ENERGY (type 1) delegation capacity of the owner', async () => {
    const fetchFn = fetchReturning({ max_size: 5 });

    await client().getDelegatableEnergySun(SENDER, fetchFn);

    const [url, init] = fetchFn.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('https://nile.example/wallet/getcandelegatedmaxsize');
    expect(JSON.parse(init.body as string)).toEqual({
      owner_address: SENDER,
      type: 1,
      visible: true,
    });
  });
});
