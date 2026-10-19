import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  monitorScanFailuresTotal,
  rpcRequestDuration,
  transferStatusUnverifiedTotal,
  webhookRejectedTotal,
  finalizedBlockAgeSeconds,
  finalizedStallThresholdSeconds,
  gasEstimateAbnormalTotal,
  journalAckAheadOfChain,
  journalBehindChain,
  nativeEthReceivedTotal,
  nonceConflicts,
  reconciliationIntervalSeconds,
  reconciliationLastSuccess,
  reconciliationMismatchTotal,
  transferWaitAlarmSeconds,
  transferWaitSeconds,
  usdtStatusOk,
  walletEthBalance,
  walletEthFloor,
} from './metrics.js';

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

  it('Prometheus scrapes the service the rules read', () => {
    const prometheus = readFileSync(PROMETHEUS_FILE, 'utf8');
    expect(prometheus).toContain("- targets: ['skinora-ethereum-sidecar:5300']");
  });
});

describe('T152 send alerts — wired to the series the service exports', () => {
  it('ethereum-transfer-wait divides the fee_cap / unmined wait by the threshold the service publishes', async () => {
    const block = ruleBlock('ethereum-transfer-wait');
    const wait = (await transferWaitSeconds.get()).name;
    const threshold = (await transferWaitAlarmSeconds.get()).name;
    expect(block).toContain(`max(${wait}{kind=~"fee_cap|unmined"}) / max(${threshold})`);
    expect(block).toMatch(/type: gt\s+params: \[1\]/);
  });

  it('ethereum-nonce-conflict fires on any conflict, as critical', async () => {
    const block = ruleBlock('ethereum-nonce-conflict');
    expect(block).toContain(`max(${(await nonceConflicts.get()).name})`);
    expect(block).toMatch(/type: gt\s+params: \[0\]/);
    expect(block).toMatch(/severity: critical/);
  });

  it('ethereum-journal-behind-chain fires on any stopped account, as critical, at once', async () => {
    const block = ruleBlock('ethereum-journal-behind-chain');
    expect(block).toContain(`max(${(await journalBehindChain.get()).name})`);
    expect(block).toMatch(/type: gt\s+params: \[0\]/);
    expect(block).toMatch(/for: 0m/);
    expect(block).toMatch(/severity: critical/);
  });

  it('ethereum-journal-ack-ahead-of-chain fires on any ignored acknowledgement, as critical, at once', async () => {
    const block = ruleBlock('ethereum-journal-ack-ahead-of-chain');
    expect(block).toContain(`max(${(await journalAckAheadOfChain.get()).name})`);
    expect(block).toMatch(/type: gt\s+params: \[0\]/);
    expect(block).toMatch(/for: 0m/);
    expect(block).toMatch(/severity: critical/);
  });
});

/**
 * T151 rules (08 §3.7.1, §3.7.4, §3.7.8) — each expression names series this
 * service exports, read from the metric objects themselves.
 */
describe('T151 alarms — wired to the series the service exports', () => {
  async function name(metric: { get(): Promise<{ name: string }> }): Promise<string> {
    return (await metric.get()).name;
  }

  it.each([
    ['ethereum-deposit-balance-mismatch', reconciliationMismatchTotal, /severity: critical/],
    ['ethereum-deposit-native-eth', nativeEthReceivedTotal, /severity: warning/],
    ['ethereum-usdt-status', usdtStatusOk, /severity: critical/],
    ['ethereum-reconciliation-stale', reconciliationLastSuccess, /severity: warning/],
  ] as const)('%s reads its series', async (uid, metric, severity) => {
    const block = ruleBlock(uid);
    const expr = /^\s+expr: (.+)$/m.exec(block)?.[1] ?? '';
    expect(expr).toContain(await name(metric));
    expect(block).toMatch(severity);
  });

  it('the stale alarm divides by the interval the service publishes', async () => {
    const block = ruleBlock('ethereum-reconciliation-stale');
    const expr = /^\s+expr: (.+)$/m.exec(block)?.[1] ?? '';
    expect(expr).toContain(`max(${await name(reconciliationIntervalSeconds)})`);
    expect(block).toMatch(/type: gt\s+params: \[2\.5\]/);
  });

  it('the USDT alarm fires below 1 and stays quiet while no USDT contract is configured (no series)', () => {
    const block = ruleBlock('ethereum-usdt-status');
    expect(block).toContain('or vector(1)');
    expect(block).toMatch(/type: lt\s+params: \[1\]/);
  });
});

describe('T153 — ethereum-wallet-eth-low reads the series the service exports', () => {
  it('divides each wallet’s ETH balance by the floor the service publishes, firing below 1', async () => {
    const block = ruleBlock('ethereum-wallet-eth-low');
    const balance = (await walletEthBalance.get()).name;
    const floor = (await walletEthFloor.get()).name;
    expect(block).toContain(`min by (wallet) (${balance}) / min by (wallet) (${floor})`);
    expect(block).toContain('or vector(1)');
    expect(block).toMatch(/type: lt\s+params: \[1\]/);
    expect(block).toMatch(/severity: warning/);
  });
});

describe('T158 — the new Ethereum alarms read the series the service exports', () => {
  it('(k) ethereum-monitor-scan-failing counts the scan failures over 15 min, critical above 20', async () => {
    const block = ruleBlock('ethereum-monitor-scan-failing');
    expect(block).toContain(`sum(increase(${(await monitorScanFailuresTotal.get()).name}[15m]))`);
    expect(block).toMatch(/type: gt\s+params: \[20\]/);
    expect(block).toMatch(/severity: critical/);
  });

  it('(j) ethereum-webhook-signature-refused fires on any 401, critical, at once', async () => {
    const block = ruleBlock('ethereum-webhook-signature-refused');
    expect(block).toContain(`${(await webhookRejectedTotal.get()).name}{status="401"}`);
    expect(block).toMatch(/type: gt\s+params: \[0\]/);
    expect(block).toMatch(/for: 0m/);
    expect(block).toMatch(/severity: critical/);
  });

  it('(g) ethereum-transfer-status-unverified fires on any UNKNOWN answer, critical, at once', async () => {
    const block = ruleBlock('ethereum-transfer-status-unverified');
    expect(block).toContain(`increase(${(await transferStatusUnverifiedTotal.get()).name}[1h])`);
    expect(block).toMatch(/type: gt\s+params: \[0\]/);
    expect(block).toMatch(/for: 0m/);
    expect(block).toMatch(/severity: critical/);
  });

  it('T159a — ethereum-gas-estimate-abnormal fires on any implausible estimate, critical, at once', async () => {
    const block = ruleBlock('ethereum-gas-estimate-abnormal');
    expect(block).toContain(`increase(${(await gasEstimateAbnormalTotal.get()).name}[1h])`);
    expect(block).toMatch(/type: gt\s+params: \[0\]/);
    expect(block).toMatch(/for: 0m/);
    expect(block).toMatch(/severity: critical/);
  });

  it('RPC latency: p95 of the request histogram above 3 s for 15 min', async () => {
    const block = ruleBlock('ethereum-rpc-latency-high');
    const name = (await rpcRequestDuration.get()).name;
    expect(block).toContain(`histogram_quantile(0.95, sum by (le) (rate(${name}_bucket[10m])))`);
    expect(block).toMatch(/type: gt\s+params: \[3\]/);
    expect(block).toMatch(/for: 15m/);
  });
});
