import { activeMonitors } from '../metrics.js';

/**
 * The two registries that watch deposit addresses: the active payment monitor
 * (T71, 3-second cadence) and the post-cancel monitor (T75, gradual cadence).
 */
export type ActiveMonitorSource = 'active' | 'post_cancel';

/**
 * Per-source counts behind the single `skinora_blockchain_active_monitors`
 * gauge.
 *
 * <para>
 * Both registries used to call `activeMonitors.set(this.monitors.size)`
 * directly. The gauge carries no labels, so the two writers overwrote each
 * other and the exported value was whichever registry wrote last — never the
 * total. `shutdown()` made it worse: either registry shutting down published a
 * flat 0 while the other was still polling.
 * </para>
 *
 * <para>
 * That mattered once T139 made the gauge load-bearing: `08 §3.4` tells capacity
 * planning to watch it against the TronGrid rate limit, `DEPLOY_RUNBOOK §G.4`
 * uses it as the "monitor is armed" proof, the `integration-metrics` Grafana
 * dashboard plots it, and `T139-ActiveMonitorQuotaAlarm` is meant to alert on
 * it. Found in the T139 validation round (finding N2, round 2) — the defect
 * predates T139 (T71/T75), but T139 is what made a wrong number consequential.
 * </para>
 *
 * <para>
 * Reporting through one place keeps the metric's name and (empty) label set
 * exactly as published, so the existing dashboard panel starts being right
 * without being touched. Post-cancel monitors consume the same TronGrid budget
 * as active ones, so a single total is also the number capacity planning
 * actually wants.
 * </para>
 */
interface ActiveMonitorCounts {
  active: number;
  post_cancel: number;
}

// prom-client's registry is process-global and `metrics.ts` already guards its
// registration against module reloads (vitest's shared-process pool re-imports
// the file). The counts behind the gauge have to live in the same scope for the
// same reason: two module instances with private counters would each publish a
// partial total.
const COUNTS_FLAG = Symbol.for('skinora_blockchain_active_monitor_counts');
type GlobalCounts = { [COUNTS_FLAG]?: ActiveMonitorCounts };

function counts(): ActiveMonitorCounts {
  const scope = globalThis as GlobalCounts;
  scope[COUNTS_FLAG] ??= { active: 0, post_cancel: 0 };
  return scope[COUNTS_FLAG];
}

/**
 * Publish `source`'s current monitor count. The gauge is set to the sum across
 * both sources, so a registry can never zero out the other one.
 */
export function reportActiveMonitorCount(source: ActiveMonitorSource, count: number): void {
  const current = counts();
  current[source] = count;
  activeMonitors.set(current.active + current.post_cancel);
}

/** Test seam — clears both sources and the gauge. */
export function resetActiveMonitorCounts(): void {
  const current = counts();
  current.active = 0;
  current.post_cancel = 0;
  activeMonitors.set(0);
}
