import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, it, expect } from 'vitest';
import { tronApiRequestDuration, tronGridDailyRequestBudget } from './metrics.js';

/**
 * T139-ActiveMonitorQuotaAlarm — the tron-quota-projection rule divides two
 * series this sidecar publishes. A rule whose expression names a metric
 * nobody exports never fires and never errors: Grafana evaluates it to an
 * empty vector, `or vector(0)` turns that into a quiet 0, and the watchman is
 * dead the day it is installed (the GrafanaAlertRulesNeverEvaluated family).
 * So the expression is read from the provisioning file and checked against the
 * names the metric objects actually carry.
 */
const RULES_FILE = resolve(__dirname, '../../infra/grafana/provisioning/alerting/rules.yml');

function ruleBlock(uid: string): string {
  const text = readFileSync(RULES_FILE, 'utf8').replace(/\r\n/g, '\n');
  const start = text.indexOf(`- uid: ${uid}\n`);
  if (start < 0) throw new Error(`rules.yml has no rule ${uid}`);
  const next = text.indexOf('\n      - uid: ', start + 1);
  return text.slice(start, next < 0 ? undefined : next);
}

describe('tron-quota-projection — wired to the series the sidecar exports', () => {
  const block = ruleBlock('tron-quota-projection');
  const expr = /^\s+expr: (.+)$/m.exec(block)?.[1] ?? '';

  // The name as the registry reports it — prom-client's typings do not expose
  // `.name` on the metric object, and the sidecar's tsconfig compiles tests too.
  it('projects the measured TronGrid request count, not some other series', async () => {
    const { name } = await tronApiRequestDuration.get();
    expect(expr).toContain(`rate(${name}_count[1h])`);
    expect(expr).toContain('* 86400');
  });

  it('divides by the budget gauge the sidecar publishes at startup', async () => {
    const { name } = await tronGridDailyRequestBudget.get();
    expect(expr).toContain(`/ max(${name})`);
  });

  it('warns at 80% of the budget', () => {
    expect(block).toMatch(/type: gt\s+params: \[0\.8\]/);
    expect(block).toMatch(/severity: warning/);
  });
});
