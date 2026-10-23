// T165 düzeltme turu — scripts/grafana/first-boot.sh against a fake Grafana
// (/run.sh stand-in). The fake records every start with the rule-execution
// switch it was given; in the guarded run it prints Grafana's real
// "finished to provision alerting" log line and then waits for SIGTERM, like
// the server. The live measurement with the real image (empty volume, fake
// SMTP) is in T165_REPORT "Düzeltme turu".
import { test, describe, before, after } from "node:test";
import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { mkdtempSync, writeFileSync, readFileSync, existsSync, rmSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT = resolve(dirname(fileURLToPath(import.meta.url)), "first-boot.sh");
const HAS_SH = spawnSync("sh", ["-c", "command -v grep && command -v sleep"], { encoding: "utf8" }).status === 0;
if (!HAS_SH && process.env.CI) {
  throw new Error("first-boot test: sh is required in CI");
}

const FINISHED = 'logger=provisioning.alerting level=info msg="finished to provision alerting"';

let work;

// mode: "ok" — guarded run provisions then waits for TERM; "die" — guarded
// run exits before provisioning; "hang" — guarded run never finishes.
function fakeGrafana(dir, mode) {
  const record = join(dir, "starts.log");
  const fake = join(dir, "run.sh");
  writeFileSync(
    fake,
    [
      "#!/bin/sh",
      `echo "start execute_alerts=\${GF_UNIFIED_ALERTING_EXECUTE_ALERTS:-default} addr=\${GF_SERVER_HTTP_ADDR:-any} args=$*" >> "${record.replace(/\\/g, "/")}"`,
      'if [ "${GF_UNIFIED_ALERTING_EXECUTE_ALERTS:-}" = "false" ]; then',
      mode === "die" ? '  echo "logger=provisioning level=error msg=\\"failure\\""; exit 1' : "  :",
      mode === "ok" ? `  echo '${FINISHED}'` : "  :",
      "  trap 'echo stopped >> \"" + record.replace(/\\/g, "/") + "\"; exit 0' TERM",
      "  while :; do sleep 1; done",
      "fi",
      'echo "real grafana"',
      "",
    ].join("\n"),
  );
  return { fake, record };
}

function run(dir, fake, extraEnv = {}) {
  return new Promise((resolvePromise) => {
    const child = spawn("sh", [SCRIPT, "--homepath=/usr/share/grafana"], {
      env: {
        PATH: process.env.PATH,
        GRAFANA_RUN: fake,
        GF_PATHS_DATA: join(dir, "data"),
        TMPDIR: dir,
        ...extraEnv,
      },
      stdio: ["ignore", "pipe", "pipe"],
    });
    let out = "";
    child.stdout.on("data", (d) => (out += d));
    child.stderr.on("data", (d) => (out += d));
    child.on("close", (code) => resolvePromise({ code, out }));
  });
}

const starts = (record) => (existsSync(record) ? readFileSync(record, "utf8").trim().split("\n") : []);

describe("grafana first-boot.sh (T165)", { skip: !HAS_SH && "sh not available" }, () => {
  before(() => {
    work = mkdtempSync(join(tmpdir(), "grafana-first-boot-"));
  });
  after(() => rmSync(work, { recursive: true, force: true }));

  function freshDir(name) {
    const dir = join(work, name);
    mkdirSync(join(dir, "data"), { recursive: true });
    return dir;
  }

  test("empty volume: provisions with rule execution off on loopback, stops it, then starts Grafana normally", async () => {
    const dir = freshDir("first");
    const { fake, record } = fakeGrafana(dir, "ok");

    const { code, out } = await run(dir, fake);

    assert.equal(code, 0, out);
    assert.deepEqual(starts(record), [
      "start execute_alerts=false addr=127.0.0.1 args=--homepath=/usr/share/grafana",
      "stopped",
      "start execute_alerts=default addr=any args=--homepath=/usr/share/grafana",
    ]);
    assert.ok(existsSync(join(dir, "data", ".skinora-alerting-provisioned")), "marker written");
    assert.match(out, /finished to provision alerting/, "the guarded run's log is shown");
    assert.match(out, /real grafana/);
  });

  test("provisioned volume: no guarded run, Grafana starts directly", async () => {
    const dir = freshDir("second");
    writeFileSync(join(dir, "data", ".skinora-alerting-provisioned"), "");
    const { fake, record } = fakeGrafana(dir, "ok");

    const { code } = await run(dir, fake);

    assert.equal(code, 0);
    assert.deepEqual(starts(record), ["start execute_alerts=default addr=any args=--homepath=/usr/share/grafana"]);
  });

  test("guarded run dies before provisioning: exits non-zero, no marker, Grafana is not started", async () => {
    const dir = freshDir("die");
    const { fake, record } = fakeGrafana(dir, "die");

    const { code, out } = await run(dir, fake);

    assert.notEqual(code, 0);
    assert.match(out, /stopped before alerting was provisioned/);
    assert.deepEqual(starts(record), ["start execute_alerts=false addr=127.0.0.1 args=--homepath=/usr/share/grafana"]);
    assert.ok(!existsSync(join(dir, "data", ".skinora-alerting-provisioned")));
  });

  test("provisioning never finishes: gives up after the timeout, stops the guarded run, no marker", async () => {
    const dir = freshDir("hang");
    const { fake, record } = fakeGrafana(dir, "hang");

    const { code, out } = await run(dir, fake, { GRAFANA_FIRST_BOOT_TIMEOUT_SECONDS: "2" });

    assert.notEqual(code, 0);
    assert.match(out, /not provisioned within 2s/);
    assert.deepEqual(starts(record), [
      "start execute_alerts=false addr=127.0.0.1 args=--homepath=/usr/share/grafana",
      "stopped",
    ]);
    assert.ok(!existsSync(join(dir, "data", ".skinora-alerting-provisioned")));
  });
});
