import { describe, it, expect } from 'vitest';
import { burnSun, callerEnergyShare, planDelegation, topUpSun } from './DelegationPlanner.js';

/**
 * 2026-09-16 hybrid energy decision, 2026-09-17 sizing/charging split.
 *
 * Two ratios on purpose (mainnet ~9.52, Nile ~73.7 Energy per staked TRX): a
 * planner that ignored its input ratio and hard-coded either one would pass a
 * suite built on a single value (#321/#322 — a fixture equal to the broken
 * constant pins nothing). Every expected amount is a literal worked out by
 * hand; none is recomputed from the planner's own margin constants.
 */
const MAINNET_RATIO = 9.52;
const NILE_RATIO = 73.7;

describe('planDelegation — plans the WHOLE transfer', () => {
  it('needs nothing when the deposit already holds the Energy for all of it', () => {
    const plan = planDelegation({
      energyRequired: 64_285,
      senderEnergyAvailable: 64_285,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: 0,
    });
    expect(plan).toEqual({ kind: 'no-energy-needed', energyRequired: 64_285, energyShortfall: 0 });
  });

  it('still plans for a single missing unit of Energy', () => {
    const plan = planDelegation({
      energyRequired: 64_285,
      senderEnergyAvailable: 64_284,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    expect(plan).toMatchObject({ kind: 'delegate', energyShortfall: 1 });
  });

  it('subtracts Energy the deposit already holds before sizing anything', () => {
    // 64,285 − 4,285 = 60,000; 60,000 ÷ 9.52 × 1.1 = 6,932.8 → 6,933 TRX.
    const plan = planDelegation({
      energyRequired: 64_285,
      senderEnergyAvailable: 4_285,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    expect(plan).toEqual({
      kind: 'delegate',
      energyRequired: 64_285,
      energyShortfall: 60_000,
      delegationSun: 6_933_000_000,
    });
  });
});

describe('planDelegation — delegate the whole shortfall or nothing', () => {
  it.each([
    // 64,285 ÷ 9.52 × 1.1 = 7,427.9 → 7,428 TRX
    { name: 'mainnet', ratio: MAINNET_RATIO, energy: 64_285, sun: 7_428_000_000 },
    // 130,285 ÷ 9.52 × 1.1 = 15,053.9 → 15,054 TRX
    {
      name: 'mainnet, recipient new to the token',
      ratio: MAINNET_RATIO,
      energy: 130_285,
      sun: 15_054_000_000,
    },
    // 64,285 ÷ 73.7 × 1.1 = 959.5 → 960 TRX
    { name: 'nile', ratio: NILE_RATIO, energy: 64_285, sun: 960_000_000 },
  ])('delegates $sun SUN at the ratio it is given ($name)', ({ ratio, energy, sun }) => {
    const plan = planDelegation({
      energyRequired: energy,
      senderEnergyAvailable: 0,
      energyPerTrx: ratio,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    expect(plan).toEqual({
      kind: 'delegate',
      energyRequired: energy,
      energyShortfall: energy,
      delegationSun: sun,
    });
    // The planned stake must actually deliver the transfer at that ratio.
    expect((sun / 1_000_000) * ratio).toBeGreaterThanOrEqual(energy);
  });

  it('delegates when the stake is EXACTLY the required amount', () => {
    // Pins `>=`: an `>` would push the one sufficient stake down the burn path.
    const plan = planDelegation({
      energyRequired: 64_285,
      senderEnergyAvailable: 0,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: 7_428_000_000,
    });
    expect(plan.kind).toBe('delegate');
  });

  it('burns the WHOLE shortfall when the stake is one SUN short — no partial delegation', () => {
    // The trap measured 2026-09-16: a partial delegation "succeeds", the
    // deposit holds no TRX for the rest, the transfer fails on Energy and no
    // fallback runs. So a stake that covers 99.99% must plan like no stake.
    const plan = planDelegation({
      energyRequired: 64_285,
      senderEnergyAvailable: 0,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: 7_427_999_999,
    });
    expect(plan).toEqual({
      kind: 'burn',
      energyRequired: 64_285,
      energyShortfall: 64_285,
      reason: 'insufficient-stake',
    });
  });

  it('burns when nothing is staked (the hot wallet before launch)', () => {
    const plan = planDelegation({
      energyRequired: 64_285,
      senderEnergyAvailable: 0,
      energyPerTrx: MAINNET_RATIO,
      delegatableSun: 0,
    });
    expect(plan.kind).toBe('burn');
  });

  it.each([null, 0, -1, Number.NaN, Number.POSITIVE_INFINITY])(
    'burns when the ratio is unusable (%s)',
    (ratio) => {
      const plan = planDelegation({
        energyRequired: 64_285,
        senderEnergyAvailable: 0,
        energyPerTrx: ratio,
        delegatableSun: Number.MAX_SAFE_INTEGER,
      });
      expect(plan).toMatchObject({ kind: 'burn', reason: 'no-ratio' });
    },
  );

  it('rounds a tiny shortfall UP to a whole TRX — the Stake 2.0 minimum', () => {
    // 10 ÷ 73.7 × 1.1 = 0.15 TRX; rounding down or to nearest would plan 0.
    const plan = planDelegation({
      energyRequired: 10,
      senderEnergyAvailable: 0,
      energyPerTrx: NILE_RATIO,
      delegatableSun: Number.MAX_SAFE_INTEGER,
    });
    expect(plan.kind === 'delegate' && plan.delegationSun).toBe(1_000_000);
  });
});

describe('callerEnergyShare — what the chain bills the caller', () => {
  it('bills the caller the whole call on mainnet Tether: 30% nominal, but its owner has no Energy left', () => {
    // Measured 2026-09-17: getcontract → percent 30, origin_energy_limit 10M;
    // owner THPvaU… EnergyLimit 9,203 / EnergyUsed 9,203; 542 of 542 calls in
    // five blocks paid 100.00%.
    expect(
      callerEnergyShare({
        energyRequired: 64_285,
        callerPercent: 30,
        originEnergyLimit: 10_000_000,
        ownerEnergyAvailable: 0,
      }),
    ).toBe(64_285);
  });

  it("caps the owner's share at its remaining Energy", () => {
    // Owner nominal ⌊64,285 × 70 / 100⌋ = 44,999, but only 9,205 left → 55,080.
    expect(
      callerEnergyShare({
        energyRequired: 64_285,
        callerPercent: 30,
        originEnergyLimit: 10_000_000,
        ownerEnergyAvailable: 9_205,
      }),
    ).toBe(55_080);
  });

  it('lets the owner pay its full nominal share when it has the Energy', () => {
    expect(
      callerEnergyShare({
        energyRequired: 64_285,
        callerPercent: 30,
        originEnergyLimit: 10_000_000,
        ownerEnergyAvailable: 1_000_000,
      }),
    ).toBe(19_286);
  });

  it('bills nothing on the Nile test USDT: percent 0 and an owner with plenty', () => {
    // Measured 2026-09-17: percent omitted (0), limit 1B, owner TKGRE6… ~197.5M left.
    expect(
      callerEnergyShare({
        energyRequired: 29_650,
        callerPercent: 0,
        originEnergyLimit: 1_000_000_000,
        ownerEnergyAvailable: 197_517_927,
      }),
    ).toBe(0);
  });

  it('never lets the owner pay past origin_energy_limit', () => {
    expect(
      callerEnergyShare({
        energyRequired: 64_285,
        callerPercent: 0,
        originEnergyLimit: 20_000,
        ownerEnergyAvailable: 1_000_000_000,
      }),
    ).toBe(44_285);
  });

  it("splits with java-tron's integer division", () => {
    // Owner ⌊10,001 × 70 / 100⌋ = ⌊7,000.7⌋ = 7,000, caller 3,001 — not 3,000.
    expect(
      callerEnergyShare({
        energyRequired: 10_001,
        callerPercent: 30,
        originEnergyLimit: 10_000_000,
        ownerEnergyAvailable: 1_000_000,
      }),
    ).toBe(3_001);
  });

  it('bills the whole call when the contract says the caller pays 100%', () => {
    expect(
      callerEnergyShare({
        energyRequired: 64_285,
        callerPercent: 100,
        originEnergyLimit: 10_000_000,
        ownerEnergyAvailable: 1_000_000_000,
      }),
    ).toBe(64_285);
  });

  it.each([
    { name: 'percent', callerPercent: Number.NaN, owner: 1_000_000_000, limit: 1_000_000_000 },
    { name: 'owner Energy', callerPercent: 0, owner: Number.NaN, limit: 1_000_000_000 },
    { name: 'limit', callerPercent: 0, owner: 1_000_000_000, limit: -1 },
  ])(
    'treats an unreadable $name as "the owner pays nothing"',
    ({ callerPercent, owner, limit }) => {
      expect(
        callerEnergyShare({
          energyRequired: 64_285,
          callerPercent,
          originEnergyLimit: limit,
          ownerEnergyAvailable: owner,
        }),
      ).toBe(64_285);
    },
  );
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

  it('adds exactly 10% and subtracts what the deposit already holds', () => {
    // 130,285 Energy × 100 SUN = 13,028,500; + 10% = 14,331,350 — no stray SUN
    // from floating point (13,028,500 × 1.1 = 14,331,350.000000002).
    expect(topUpSun(13_028_500, 0)).toBe(14_331_350);
    expect(topUpSun(13_028_500, 5_000_000)).toBe(9_331_350);
  });

  it('sends nothing when the balance already covers it, or nothing burns', () => {
    expect(topUpSun(6_428_500, 15_000_000)).toBe(0);
    expect(topUpSun(0, 0)).toBe(0);
  });
});
