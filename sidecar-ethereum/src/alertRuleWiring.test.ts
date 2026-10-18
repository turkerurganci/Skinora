import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { finalizedBlockAgeSeconds, finalizedStallThresholdSeconds } from './metrics.js';

/**
 * The ethereum-finalized-stalled rule (08 §3.7.8) divides two series this
 * service publishes. A rule naming a metric nobody exports never fires and
 * never errors (`or vector(0)` turns the empty vector into a quiet 0), so the
 * expression is read from the provisioning file and checked against the names
 * the metric objects actually carry — same guard as the TRON service's.
 */
const RULES_FILE = resolve(__dirname, '../../infra/grafana/provisioning/alerting/rules.yml');
const PROMETHEUS_FILE = resolve(__dirname, '../../infra/prometheus/prometheus.yml');

function ruleBlock(uid: string): string {
  const text = readFileSync(RULES_FILE, 'utf8').replace(/\r\n/g, '\n');
  const start = text.indexOf(`- uid: ${uid}\n`);
  if (start < 0) throw new Error(`rules.yml has no rule ${uid}`);
  const next = text.indexOf('\n      - uid: ', start + 1);
  return text.slice(start, next < 0 ? undefined : next);
}

describe('ethereum-finalized-stalled — wired to the series the service exports', () => {
  const block = ruleBlock('ethereum-finalized-stalled');
  const expr = /^\s+expr: (.+)$/m.exec(block)?.[1] ?? '';

  it('divides the finalized-block age by the threshold the service itself applies', async () => {
    const age = (await finalizedBlockAgeSeconds.get()).name;
    const threshold = (await finalizedStallThresholdSeconds.get()).name;
    expect(expr).toContain(`max(${age}) / max(${threshold})`);
  });

  it('fires above a ratio of 1, as critical', () => {
    expect(block).toMatch(/type: gt\s+params: \[1\]/);
    expect(block).toMatch(/severity: critical/);
  });

  it('Prometheus scrapes the service the rule reads', () => {
    const prometheus = readFileSync(PROMETHEUS_FILE, 'utf8');
    expect(prometheus).toContain("- targets: ['skinora-ethereum-sidecar:5300']");
  });
});
