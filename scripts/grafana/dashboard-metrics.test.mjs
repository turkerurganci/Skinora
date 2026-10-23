// T170 — every Grafana dashboard panel and alert rule reads a metric some
// service really exports. A panel bound to a wrong name is not an error in
// Grafana — it is an empty panel ("No data"), and a rule bound to one sits at
// its `or vector(0)` forever: the dead-watchman family this project keeps
// meeting (T16's dashboards read `skinora_active_transactions`, job
// "skinora-steam" and cAdvisor series nobody exported until T170).
//
// The exported set is read from the source: every metric name the services
// declare (prom-client `name: '…'` in the three Node services, prometheus-net
// `Metrics.Create*("…")` in the backend) plus the libraries' own default
// collectors listed below. The live counterpart — the same panels against a
// running Prometheus — is in T170_REPORT "Canlı ölçüm".
import { test, describe } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const DASHBOARDS = join(ROOT, "infra", "grafana", "provisioning", "dashboards", "json");
const RULES = join(ROOT, "infra", "grafana", "provisioning", "alerting", "rules.yml");
const PROMETHEUS = join(ROOT, "infra", "prometheus", "prometheus.yml");

function walk(dir, filter, out = []) {
  for (const entry of readdirSync(dir)) {
    if (entry === "node_modules" || entry === "bin" || entry === "obj" || entry === "dist") continue;
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) walk(path, filter, out);
    else if (filter(path)) out.push(path);
  }
  return out;
}

// prom-client collectDefaultMetrics() (v15) — every Node service calls it with
// its prefix (skinora_steam_, skinora_blockchain_, skinora_ethereum_).
const PROM_CLIENT_DEFAULTS = [
  "process_cpu_user_seconds_total",
  "process_cpu_system_seconds_total",
  "process_cpu_seconds_total",
  "process_start_time_seconds",
  "process_resident_memory_bytes",
  "process_virtual_memory_bytes",
  "process_heap_bytes",
  "process_open_fds",
  "process_max_fds",
  "nodejs_eventloop_lag_seconds",
  "nodejs_eventloop_lag_min_seconds",
  "nodejs_eventloop_lag_max_seconds",
  "nodejs_eventloop_lag_mean_seconds",
  "nodejs_eventloop_lag_stddev_seconds",
  "nodejs_eventloop_lag_p50_seconds",
  "nodejs_eventloop_lag_p90_seconds",
  "nodejs_eventloop_lag_p99_seconds",
  "nodejs_active_resources",
  "nodejs_active_resources_total",
  "nodejs_active_handles",
  "nodejs_active_handles_total",
  "nodejs_active_requests",
  "nodejs_active_requests_total",
  "nodejs_heap_size_total_bytes",
  "nodejs_heap_size_used_bytes",
  "nodejs_external_memory_bytes",
  "nodejs_heap_space_size_total_bytes",
  "nodejs_heap_space_size_used_bytes",
  "nodejs_heap_space_size_available_bytes",
  "nodejs_version_info",
  "nodejs_gc_duration_seconds",
];
const NODE_PREFIXES = ["skinora_steam_", "skinora_blockchain_", "skinora_ethereum_"];

// prometheus-net 8 (backend): the process / runtime collector and
// prometheus-net.AspNetCore's UseHttpMetrics() (label `code`, not `status`).
const PROMETHEUS_NET_DEFAULTS = [
  "process_cpu_seconds_total",
  "process_start_time_seconds",
  "process_working_set_bytes",
  "process_private_memory_bytes",
  "process_virtual_memory_bytes",
  "process_open_handles",
  "process_num_threads",
  "dotnet_total_memory_bytes",
  "dotnet_collection_count_total",
  "http_requests_received_total",
  "http_request_duration_seconds",
  "http_requests_in_progress",
];

function exportedMetrics() {
  const names = new Set(["up"]);
  const histograms = new Set(["http_request_duration_seconds"]);
  for (const svc of ["sidecar-steam", "sidecar-blockchain", "sidecar-ethereum"]) {
    for (const file of walk(join(ROOT, svc, "src"), (p) => p.endsWith(".ts") && !p.endsWith(".test.ts"))) {
      const text = readFileSync(file, "utf8");
      for (const m of text.matchAll(/(Histogram|Counter|Gauge)(?:<[^>]*>)?\(\{\s*name:\s*'([a-z0-9_]+)'/g)) {
        names.add(m[2]);
        if (m[1] === "Histogram") histograms.add(m[2]);
      }
      for (const m of text.matchAll(/getOrCreate(Histogram|Counter|Gauge)\(\{\s*name:\s*'([a-z0-9_]+)'/g)) {
        names.add(m[2]);
        if (m[1] === "Histogram") histograms.add(m[2]);
      }
    }
  }
  for (const file of walk(join(ROOT, "backend", "src"), (p) => p.endsWith(".cs"))) {
    const text = readFileSync(file, "utf8");
    for (const m of text.matchAll(/Metrics\.Create(Histogram|Counter|Gauge)\(\s*"([a-z0-9_]+)"/g)) {
      names.add(m[2]);
      if (m[1] === "Histogram") histograms.add(m[2]);
    }
  }
  for (const prefix of NODE_PREFIXES) for (const n of PROM_CLIENT_DEFAULTS) names.add(prefix + n);
  histograms.add("skinora_blockchain_nodejs_gc_duration_seconds");
  for (const n of PROMETHEUS_NET_DEFAULTS) names.add(n);
  return { names, histograms };
}

const METRIC_TOKEN = /(?<![A-Za-z0-9_:"])(skinora_[a-z0-9_]+|http_[a-z0-9_]+|process_[a-z0-9_]+|dotnet_[a-z0-9_]+|nodejs_[a-z0-9_]+|container_[a-z0-9_]+|up)(?![A-Za-z0-9_])/g;

/** Metric names a PromQL expression reads (string literals and label matchers stripped). */
function metricsIn(expr) {
  const stripped = expr
    .replace(/"[^"]*"/g, '""')
    .replace(/\{[^}]*\}/g, "{}")
    .replace(/\b(by|without|on|ignoring|group_left|group_right)\s*\([^)]*\)/g, " ");
  return [...stripped.matchAll(METRIC_TOKEN)].map((m) => m[1]);
}

function resolves(name, { names, histograms }) {
  if (names.has(name)) return true;
  const base = name.replace(/_(bucket|count|sum)$/, "");
  return base !== name && histograms.has(base);
}

function dashboards() {
  return readdirSync(DASHBOARDS)
    .filter((f) => f.endsWith(".json"))
    .map((f) => ({ file: f, json: JSON.parse(readFileSync(join(DASHBOARDS, f), "utf8")) }));
}

function promTargets(dashboard) {
  const out = [];
  for (const panel of dashboard.panels ?? []) {
    for (const t of panel.targets ?? []) {
      const ds = t.datasource ?? panel.datasource;
      const isLoki = (typeof ds === "string" ? ds : ds?.type ?? ds?.uid) === "loki" || ds === "Loki";
      if (!isLoki) out.push({ panel: panel.title, expr: t.expr });
    }
  }
  return out;
}

function ruleExprs() {
  const lines = readFileSync(RULES, "utf8").replace(/\r\n/g, "\n").split("\n");
  const out = [];
  let uid = "";
  for (let i = 0; i < lines.length; i++) {
    const u = /^\s+- uid: ([a-z0-9-]+)$/.exec(lines[i]);
    if (u) uid = u[1];
    const m = /^(\s+)expr: (.*)$/.exec(lines[i]);
    if (!m) continue;
    if (m[2].trim() === "|") {
      const indent = m[1].length;
      const body = [];
      while (i + 1 < lines.length && (lines[i + 1].trim() === "" || lines[i + 1].search(/\S/) > indent)) {
        body.push(lines[++i].trim());
      }
      out.push({ uid, expr: body.join(" ") });
    } else {
      out.push({ uid, expr: m[2] });
    }
  }
  return out;
}

const exported = exportedMetrics();

describe("Grafana dashboards — bound to metrics the services export", () => {
  test("the exported set is not empty (the source scan found the services)", () => {
    for (const n of [
      "skinora_backend_hot_wallet_balance",
      "skinora_blockchain_tron_solid_block_age_seconds",
      "skinora_ethereum_finalized_block_age_seconds",
      "skinora_steam_api_errors_total",
    ]) {
      assert.ok(exported.names.has(n), n);
    }
  });

  for (const { file, json } of dashboards()) {
    test(`${file}: every Prometheus panel query names an exported metric`, () => {
      const targets = promTargets(json);
      assert.ok(targets.length > 0, "no Prometheus queries");
      const unknown = [];
      for (const { panel, expr } of targets) {
        assert.ok(expr && expr.trim(), `${panel}: empty query`);
        const metrics = metricsIn(expr);
        assert.ok(metrics.length > 0, `${panel}: no metric in "${expr}"`);
        for (const m of metrics) if (!resolves(m, exported)) unknown.push(`${panel}: ${m}`);
      }
      assert.deepEqual(unknown, []);
    });
  }

  test("uids are unique and every Prometheus datasource is the provisioned uid", () => {
    const uids = dashboards().map((d) => d.json.uid);
    assert.equal(new Set(uids).size, uids.length);
    for (const { file, json } of dashboards()) {
      for (const panel of json.panels ?? []) {
        for (const ds of [panel.datasource, ...(panel.targets ?? []).map((t) => t.datasource)]) {
          if (!ds) continue;
          assert.ok(
            (ds.type === "prometheus" && ds.uid === "prometheus") || (ds.type === "loki" && ds.uid === "loki"),
            `${file} / ${panel.title}: datasource ${JSON.stringify(ds)}`,
          );
        }
      }
    }
  });

  test("job labels the panels filter on are the scrape jobs Prometheus has", () => {
    const jobs = new Set([...readFileSync(PROMETHEUS, "utf8").matchAll(/job_name: '([^']+)'/g)].map((m) => m[1]));
    for (const { file, json } of dashboards()) {
      for (const { panel, expr } of promTargets(json)) {
        for (const m of expr.matchAll(/job="([^"]+)"/g)) {
          assert.ok(jobs.has(m[1]), `${file} / ${panel}: job "${m[1]}" is not scraped`);
        }
      }
    }
  });
});

describe("T170 Networks dashboard — per network, with the alarm thresholds beside the values", () => {
  const net = dashboards().find((d) => d.file === "networks.json")?.json;

  test("exists with a TRON row, an Ethereum row and a network variable", () => {
    assert.ok(net);
    const rows = net.panels.filter((p) => p.type === "row").map((p) => p.title);
    assert.ok(rows.includes("TRON"));
    assert.ok(rows.includes("Ethereum"));
    assert.deepEqual(net.templating.list.map((v) => v.name), ["network"]);
    assert.equal(net.templating.list[0].query, "TRON,ETHEREUM");
  });

  test("draws each per-network alarm's value next to the threshold the service publishes", () => {
    const all = promTargets(net).map((t) => t.expr).join("\n");
    for (const pair of [
      ["skinora_blockchain_tron_solid_block_age_seconds", "skinora_blockchain_tron_solid_block_stall_threshold_seconds"],
      ["skinora_ethereum_finalized_block_age_seconds", "skinora_ethereum_finalized_stall_threshold_seconds"],
      ["skinora_ethereum_wallet_eth_balance", "skinora_ethereum_wallet_eth_floor"],
      ["skinora_backend_hot_wallet_balance", "skinora_backend_hot_wallet_threshold"],
      ["skinora_backend_outbound_pending_rows", "skinora_backend_outbound_pending_rows_alarm_threshold"],
      ["skinora_blockchain_tron_api_request_duration_seconds_count", "skinora_blockchain_trongrid_daily_request_budget"],
    ]) {
      for (const metric of pair) assert.ok(all.includes(metric), metric);
    }
    for (const metric of [
      "skinora_backend_blockchain_service_degraded",
      "skinora_backend_hot_wallet_last_check_timestamp_seconds",
      "skinora_backend_platform_component_up",
      "skinora_blockchain_tron_api_errors_total",
      "skinora_ethereum_rpc_request_duration_seconds_count",
    ]) {
      assert.ok(all.includes(metric), metric);
    }
  });
});

describe("Alert rules — every expression names an exported metric", () => {
  test("rules.yml", () => {
    const exprs = ruleExprs();
    assert.ok(exprs.length >= 40, `only ${exprs.length} expressions found`);
    const unknown = [];
    for (const { uid, expr } of exprs) {
      for (const m of metricsIn(expr)) if (!resolves(m, exported)) unknown.push(`${uid}: ${m}`);
    }
    assert.deepEqual(unknown, []);
  });

  test("the extractor catches a wrong name (bozma)", () => {
    assert.equal(resolves("skinora_active_transactions", exported), false);
    assert.deepEqual(metricsIn('max by (network) (skinora_x{network="TRON"}) / max(up{job="a"}) or vector(1e12)'), [
      "skinora_x",
      "up",
    ]);
  });
});
