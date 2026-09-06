import { describe, it, expect, vi } from 'vitest';
import { TronResourceClient } from './TronResourceClient.js';
import { SidecarError } from '../errors/SidecarError.js';

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

    expect(error).toBeInstanceOf(SidecarError);
    expect((error as SidecarError).code).toBe('FEE_ESTIMATE_SIMULATION_FAILED');
    // Retryable → the handler answers 502 → the backend charges the static
    // fallback. Undercharging by 15x is the outcome this prevents.
    expect((error as SidecarError).retryable).toBe(true);
    expect((error as SidecarError).message).toContain('REVERT opcode executed');
  });

  it('rejects a failed ret even when the node volunteers no message', async () => {
    const fetchFn = fetchReturning({
      result: { result: true },
      energy_used: 1984,
      transaction: { ret: [{ ret: 'OUT_OF_ENERGY' }] },
    });

    await expect(
      client().estimateTransferEnergy(CONTRACT, SENDER, RECIPIENT, '10200000', fetchFn),
    ).rejects.toThrow(/OUT_OF_ENERGY/);
  });

  it('rejects a response with no energy_used at all', async () => {
    const fetchFn = fetchReturning({ result: { result: true } });

    await expect(
      client().estimateTransferEnergy(CONTRACT, SENDER, RECIPIENT, '10200000', fetchFn),
    ).rejects.toBeInstanceOf(SidecarError);
  });
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
