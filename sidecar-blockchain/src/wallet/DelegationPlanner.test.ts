import { describe, it, expect } from 'vitest';
import {
  BURN_MARGIN,
  DELEGATION_MARGIN,
  MIN_DELEGATION_SUN,
  burnSun,
  planDelegation,
  topUpSun,
} from './DelegationPlanner.js';

/**
 * 2026-09-16 hybrid energy decision. Two measured ratios are used on purpose
 * (mainnet ~9.52, Nile ~73.7 Energy per staked TRX): a planner that ignored its
 * input ratio and hard-coded either one would pass a suite built on a single
 * value (#321/#322 — a fixture equal to the broken constant pins nothing).
 */
const MAINNET_RATIO = 180e9 / 18_904_111_430; // measured 2026-09-16
const NILE_RATIO = 180e9 / 2_440_967_347; // measured 2026-09-16

function requiredSun(shortfall: number, ratio: number): number {
  return Math.max(
    MIN_DELEGATION_SUN,
    Math.ceil((shortfall / ratio) * DELEGATION_MARGIN) * 1_000_000,
  );
}

describe('planDelegation — who pays and how much', () => {
  it('needs nothing when the contract owner pays the whole call', () => {
    // Nile test USDT: consume_user_resource_percent = 0 (measured 2026-09-04).
    const plan = planDelegation({
      energyRequired: 14_584,
      callerPercent: 0,
      senderEnergyAvailable: 0,
      energyPerTrx: NILE_RATIO,
      delegatableSun: 0,
    });
    expect(plan).toEqual({ kind: 'no-energy-needed', callerEnergy: 0, energyShortfall: 0 });
  });

  it.each([
    { callerPercent: 100, expected: 64_285 },
    { callerPercent: 30, expected: Math.ceil(64_285 * 0.3) },
  ])('charges the caller its share ($callerPercent%)', ({ callerPercent, expected }) => {
    const plan = planDelegation({
      energyRequired: 64_285,
      callerPercent,
      senderEnergyAvailable: 0,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: 0,
    });
    expect(plan.callerEnergy).toBe(expected);
    expect(plan.energyShortfall).toBe(expected);
  });

  it('subtracts Energy the deposit already holds before sizing anything', () => {
    const plan = planDelegation({
      energyRequired: 64_285,
      callerPercent: 100,
      senderEnergyAvailable: 4_285,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    expect(plan.kind).toBe('delegate');
    expect(plan.energyShortfall).toBe(60_000);
    expect(plan.kind === 'delegate' && plan.delegationSun).toBe(requiredSun(60_000, MAINNET_RATIO));
  });
});

describe('planDelegation — delegate the whole shortfall or nothing', () => {
  it.each([
    { name: 'mainnet', ratio: MAINNET_RATIO, energy: 64_285 },
    { name: 'mainnet, recipient new to the token', ratio: MAINNET_RATIO, energy: 130_285 },
    { name: 'nile', ratio: NILE_RATIO, energy: 64_285 },
  ])('sizes the delegation from the ratio it is given ($name)', ({ ratio, energy }) => {
    const plan = planDelegation({
      energyRequired: energy,
      callerPercent: 100,
      senderEnergyAvailable: 0,
      energyPerTrx: ratio,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    expect(plan).toEqual({
      kind: 'delegate',
      callerEnergy: energy,
      energyShortfall: energy,
      delegationSun: requiredSun(energy, ratio),
    });
    // The margin must actually deliver the shortfall at that ratio.
    if (plan.kind === 'delegate') {
      expect((plan.delegationSun / 1_000_000) * ratio).toBeGreaterThanOrEqual(energy);
    }
  });

  it('delegates when the stake is EXACTLY the required amount', () => {
    // Pins `>=`: an `>` would push the one sufficient stake down the burn path.
    const exact = requiredSun(64_285, MAINNET_RATIO);
    const plan = planDelegation({
      energyRequired: 64_285,
      callerPercent: 100,
      senderEnergyAvailable: 0,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: exact,
    });
    expect(plan.kind).toBe('delegate');
  });

  it('burns the WHOLE shortfall when the stake is one SUN short — no partial delegation', () => {
    // The trap measured 2026-09-16: a partial delegation "succeeds", the
    // deposit holds no TRX for the rest, the transfer fails on Energy and no
    // fallback runs. So a stake that covers 99.99% must plan like no stake.
    const exact = requiredSun(64_285, MAINNET_RATIO);
    const plan = planDelegation({
      energyRequired: 64_285,
      callerPercent: 100,
      senderEnergyAvailable: 0,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: exact - 1,
    });
    expect(plan).toEqual({
      kind: 'burn',
      callerEnergy: 64_285,
      energyShortfall: 64_285,
      reason: 'insufficient-stake',
    });
  });

  it('burns when nothing is staked (the hot wallet before launch)', () => {
    const plan = planDelegation({
      energyRequired: 64_285,
      callerPercent: 100,
      senderEnergyAvailable: 0,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: 0,
    });
    expect(plan.kind).toBe('burn');
  });

  it.each([null, 0, -1, Number.NaN])('burns when the ratio is unusable (%s)', (ratio) => {
    const plan = planDelegation({
      energyRequired: 64_285,
      callerPercent: 100,
      senderEnergyAvailable: 0,
      energyPerTrx: ratio,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    expect(plan).toMatchObject({ kind: 'burn', reason: 'no-ratio' });
  });

  it('never plans a delegation below the chain minimum of 1 TRX', () => {
    const plan = planDelegation({
      energyRequired: 10,
      callerPercent: 100,
      senderEnergyAvailable: 0,
      energyPerTrx: NILE_RATIO,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    expect(plan.kind === 'delegate' && plan.delegationSun).toBe(MIN_DELEGATION_SUN);
  });
});

describe('burnSun / topUpSun', () => {
  it('prices Energy at the chain fee and skips Bandwidth that is available', () => {
    expect(
      burnSun({
        energyToBurn: 64_285,
        energyFeeSun: 100,
        bandwidthAvailable: 600,
        txBytes: 350,
        bandwidthFeeSun: 1000,
      }),
    ).toBe(6_428_500);
  });

  it('burns the whole transaction when Bandwidth is short by one byte', () => {
    expect(
      burnSun({
        energyToBurn: 0,
        energyFeeSun: 100,
        bandwidthAvailable: 349,
        txBytes: 350,
        bandwidthFeeSun: 1000,
      }),
    ).toBe(350_000);
  });

  it('adds the margin and subtracts what the deposit already holds', () => {
    const required = 13_028_500; // 130,285 Energy × 100 SUN
    expect(topUpSun(required, 0)).toBe(Math.ceil(required * BURN_MARGIN));
    expect(topUpSun(required, 5_000_000)).toBe(Math.ceil(required * BURN_MARGIN) - 5_000_000);
  });

  it('sends nothing when the balance already covers it, or nothing burns', () => {
    expect(topUpSun(6_428_500, 15_000_000)).toBe(0);
    expect(topUpSun(0, 0)).toBe(0);
  });
});
