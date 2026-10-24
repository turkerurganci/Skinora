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

// sh condition: true when Grafana 11.3 would print an info-level text line of
// the provisioning.alerting logger to the console.
const LOG_ALLOWS_INFO_TEXT = [
  'case "${GF_LOG_LEVEL:-info}" in debug|info) true ;; *) false ;; esac',
  'case "${GF_LOG_CONSOLE_LEVEL:-info}" in debug|info) true ;; *) false ;; esac',
  'case "${GF_LOG_MODE:-console file}" in *console*) true ;; *) false ;; esac',
  '[ "${GF_LOG_CONSOLE_FORMAT:-text}" = text ]',
  'case "${GF_LOG_FILTERS:-}" in *provisioning.alerting:warn*|*provisioning.alerting:error*|*provisioning.alerting:crit*) false ;; *) true ;; esac',
].join(" && ");

// Operator logging that hides that line (T165 yeniden doğrulama Y1: warn
// measured live — Grafana never started).
const QUIET_LOGGING = {
  GF_LOG_LEVEL: "warn",
  GF_LOG_MODE: "file",
  GF_LOG_CONSOLE_LEVEL: "error",
  GF_LOG_CONSOLE_FORMAT: "json",
  GF_LOG_FILTERS: "provisioning.alerting:error",
};

let work;

// mode: "ok" — guarded run provisions then waits for TERM; "die" — guarded
// run exits before provisioning; "hang" — guarded run never finishes.
function fakeGrafana(dir, mode) {
  const record = join(dir, "starts.log");
  const logRecord = join(dir, "log-settings.log");
  const fake = join(dir, "run.sh");
  writeFileSync(
    fake,
    [
      "#!/bin/sh",
      `echo "start execute_alerts=\${GF_UNIFIED_ALERTING_EXECUTE_ALERTS:-default} addr=\${GF_SERVER_HTTP_ADDR:-any} args=$*" >> "${record.replace(/\\/g, "/")}"`,
      `echo "log level=\${GF_LOG_LEVEL:-} mode=\${GF_LOG_MODE:-} format=\${GF_LOG_CONSOLE_FORMAT:-}" >> "${logRecord.replace(/\\/g, "/")}"`,
      'if [ "${GF_UNIFIED_ALERTING_EXECUTE_ALERTS:-}" = "false" ]; then',
      mode === "die" ? '  echo "logger=provisioning level=error msg=\\"failure\\""; exit 1' : "  :",
      // Like Grafana: the info line reaches stdout only from a console logger
      // at info (or lower), in text format, that no filter mutes.
      mode === "ok" ? `  if ${LOG_ALLOWS_INFO_TEXT}; then echo '${FINISHED}'; fi` : "  :",
      "  trap 'echo stopped >> \"" + record.replace(/\\/g, "/") + "\"; exit 0' TERM",
      "  while :; do sleep 1; done",
      "fi",
      'echo "real grafana"',
      "",
    ].join("\n"),
    // Executed directly, like the image's /run.sh (Linux needs the x bit).
    { mode: 0o755 },
  );
  return { fake, record, logRecord };
}

// T171b — every run has a valid admin password file unless a test says
// otherwise (first-boot.sh refuses to start without one).
const ADMIN_PASSWORD = "t171b-test-grafana-admin-password";

function adminPasswordFile(dir) {
  const path = join(dir, "grafana_admin_password");
  if (!existsSync(path)) writeFileSync(path, ADMIN_PASSWORD + "\n");
  return path;
}

function run(dir, fake, extraEnv = {}) {
  return new Promise((resolvePromise) => {
    const env = {
      PATH: process.env.PATH,
      GRAFANA_RUN: fake,
      GF_PATHS_DATA: join(dir, "data"),
      TMPDIR: dir,
      GF_SECURITY_ADMIN_PASSWORD__FILE: adminPasswordFile(dir),
      ...extraEnv,
    };
    for (const [k, v] of Object.entries(env)) if (v === undefined) delete env[k];
    const child = spawn("sh", [SCRIPT, "--homepath=/usr/share/grafana"], {
      env,
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

  test("operator logging that hides info lines (warn/file/json/filter): the guarded run pins its own, the real start keeps the operator's", async () => {
    // T165 kapanış (yeniden doğrulama Y1, ⚠ PARA): the guard waited for an info
    // line; GF_LOG_LEVEL=warn left Grafana never starting.
    const dir = freshDir("quiet");
    const { fake, record, logRecord } = fakeGrafana(dir, "ok");

    const { code, out } = await run(dir, fake, { ...QUIET_LOGGING, GRAFANA_FIRST_BOOT_TIMEOUT_SECONDS: "5" });

    assert.equal(code, 0, out);
    assert.deepEqual(starts(record), [
      "start execute_alerts=false addr=127.0.0.1 args=--homepath=/usr/share/grafana",
      "stopped",
      "start execute_alerts=default addr=any args=--homepath=/usr/share/grafana",
    ]);
    assert.deepEqual(starts(logRecord), [
      "log level=info mode=console format=text",
      "log level=warn mode=file format=json",
    ]);
    assert.ok(existsSync(join(dir, "data", ".skinora-alerting-provisioned")), "marker written");
  });

  test("control: under that logging the fake Grafana hides the line (so the pin is what passes the test above)", () => {
    const dir = freshDir("quiet-control");
    const { fake } = fakeGrafana(dir, "ok");
    const guarded = (extra) =>
      spawnSync("sh", ["-c", `"${fake.replace(/\\/g, "/")}" & sleep 1; kill $!`], {
        env: { PATH: process.env.PATH, GF_UNIFIED_ALERTING_EXECUTE_ALERTS: "false", ...extra },
        encoding: "utf8",
      }).stdout ?? "";
    assert.doesNotMatch(guarded(QUIET_LOGGING), /finished to provision alerting/);
    assert.match(guarded({}), /finished to provision alerting/);
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

// T171a doğrulama S4-2 (⚠ GÜVENLİK — alarm yolu): with SMTP on, a password
// mount that is not a readable file (Docker's directory for a missing source)
// stops the start instead of Grafana running with an empty SMTP password.
describe("grafana first-boot.sh — SMTP password file (T171a S4-2)", { skip: !HAS_SH && "sh not available" }, () => {
  before(() => {
    work = mkdtempSync(join(tmpdir(), "grafana-smtp-file-"));
  });
  after(() => rmSync(work, { recursive: true, force: true }));

  // A provisioned volume, so only the SMTP check stands between the script and the real start.
  function provisioned(name) {
    const dir = join(work, name);
    mkdirSync(join(dir, "data"), { recursive: true });
    writeFileSync(join(dir, "data", ".skinora-alerting-provisioned"), "");
    return dir;
  }

  for (const [i, enabled] of ["true", "TRUE", "1", "yes"].entries()) {
    test(`SMTP on (${enabled}), password path is a directory: exits non-zero, Grafana is not started`, async () => {
      const dir = provisioned(`dir-${i}`);
      const secret = join(dir, "grafana_smtp_password");
      mkdirSync(secret);
      const { fake, record } = fakeGrafana(dir, "ok");

      const { code, out } = await run(dir, fake, { GF_SMTP_ENABLED: enabled, GF_SMTP_PASSWORD__FILE: secret });

      assert.notEqual(code, 0, out);
      assert.match(out, /GRAFANA_SMTP_PASSWORD_UNREADABLE/);
      assert.deepEqual(starts(record), []);
    });
  }

  test("SMTP on, password path missing: exits non-zero, Grafana is not started", async () => {
    const dir = provisioned("missing");
    const { fake, record } = fakeGrafana(dir, "ok");

    const { code, out } = await run(dir, fake, {
      GF_SMTP_ENABLED: "true",
      GF_SMTP_PASSWORD__FILE: join(dir, "nope"),
    });

    assert.notEqual(code, 0, out);
    assert.match(out, /GRAFANA_SMTP_PASSWORD_UNREADABLE/);
    assert.deepEqual(starts(record), []);
  });

  test("SMTP on, password is a file (also an empty one): Grafana starts, the value is not printed", async () => {
    for (const [name, content] of [["filled", "re_t171a_smtp_password_value\n"], ["empty", ""]]) {
      const dir = provisioned(name);
      const secret = join(dir, "grafana_smtp_password");
      writeFileSync(secret, content);
      const { fake, record } = fakeGrafana(dir, "ok");

      const { code, out } = await run(dir, fake, { GF_SMTP_ENABLED: "true", GF_SMTP_PASSWORD__FILE: secret });

      assert.equal(code, 0, out);
      assert.deepEqual(starts(record), ["start execute_alerts=default addr=any args=--homepath=/usr/share/grafana"]);
      assert.doesNotMatch(out, /re_t171a_smtp_password_value/);
    }
  });

  test("SMTP off (default): a directory at the password path does not block the start", async () => {
    for (const [i, enabled] of [undefined, "false", "0"].entries()) {
      const dir = provisioned(`off-${i}`);
      const secret = join(dir, "grafana_smtp_password");
      mkdirSync(secret);
      const { fake, record } = fakeGrafana(dir, "ok");

      const extra = { GF_SMTP_PASSWORD__FILE: secret };
      if (enabled !== undefined) extra.GF_SMTP_ENABLED = enabled;
      const { code, out } = await run(dir, fake, extra);

      assert.equal(code, 0, out);
      assert.deepEqual(starts(record), ["start execute_alerts=default addr=any args=--homepath=/usr/share/grafana"]);
    }
  });
});

// T171b (⚠ GÜVENLİK): the admin password is the Docker secret file
// GF_SECURITY_ADMIN_PASSWORD__FILE. Grafana itself starts with admin/admin
// when that file is missing or empty — first-boot.sh refuses instead.
describe("grafana first-boot.sh — admin password file (T171b)", { skip: !HAS_SH && "sh not available" }, () => {
  before(() => {
    work = mkdtempSync(join(tmpdir(), "grafana-admin-file-"));
  });
  after(() => rmSync(work, { recursive: true, force: true }));

  function provisioned(name) {
    const dir = join(work, name);
    mkdirSync(join(dir, "data"), { recursive: true });
    writeFileSync(join(dir, "data", ".skinora-alerting-provisioned"), "");
    return dir;
  }

  const refusals = [
    ["the password as a variable", (dir) => ({ GF_SECURITY_ADMIN_PASSWORD: ADMIN_PASSWORD }), /CREDENTIAL_IN_ENV/],
    ["no __FILE variable", () => ({ GF_SECURITY_ADMIN_PASSWORD__FILE: undefined }), /CREDENTIAL_MISSING/],
    ["a missing file", (dir) => ({ GF_SECURITY_ADMIN_PASSWORD__FILE: join(dir, "nope") }), /CREDENTIAL_MISSING/],
    [
      "an empty file",
      (dir) => {
        const path = join(dir, "empty_admin");
        writeFileSync(path, " \n");
        return { GF_SECURITY_ADMIN_PASSWORD__FILE: path };
      },
      /CREDENTIAL_MISSING/,
    ],
    [
      "a directory (Docker's stand-in for a missing source)",
      (dir) => {
        const path = join(dir, "dir_admin");
        mkdirSync(path);
        return { GF_SECURITY_ADMIN_PASSWORD__FILE: path };
      },
      /CREDENTIAL_UNREADABLE/,
    ],
    [
      // Measured on grafana/grafana:11.3.0: /run.sh keeps the CR, so the
      // visible password would be refused (Git Bash openssl writes CRLF).
      "a CRLF file",
      (dir) => {
        const path = join(dir, "crlf_admin");
        writeFileSync(path, ADMIN_PASSWORD + "\r\n");
        return { GF_SECURITY_ADMIN_PASSWORD__FILE: path };
      },
      /CREDENTIAL_INVALID/,
    ],
    [
      "SMTP on and a CRLF SMTP password file",
      (dir) => {
        const path = join(dir, "crlf_smtp");
        writeFileSync(path, "re_t171b_smtp_value\r\n");
        return { GF_SMTP_ENABLED: "true", GF_SMTP_PASSWORD__FILE: path };
      },
      /CREDENTIAL_INVALID/,
    ],
  ];

  for (const [i, [label, envOf, code]] of refusals.entries()) {
    test(`${label}: exits non-zero, Grafana is not started, the value is not printed`, async () => {
      const dir = provisioned(`refuse-${i}`);
      const { fake, record } = fakeGrafana(dir, "ok");

      const { code: exit, out } = await run(dir, fake, envOf(dir));

      assert.notEqual(exit, 0, out);
      assert.match(out, code);
      assert.deepEqual(starts(record), []);
      assert.ok(!out.includes(ADMIN_PASSWORD), "the admin password is never printed");
    });
  }

  test("a readable, non-empty file: Grafana starts, the value is not printed", async () => {
    const dir = provisioned("ok");
    const { fake, record } = fakeGrafana(dir, "ok");

    const { code, out } = await run(dir, fake);

    assert.equal(code, 0, out);
    assert.deepEqual(starts(record), ["start execute_alerts=default addr=any args=--homepath=/usr/share/grafana"]);
    assert.ok(!out.includes(ADMIN_PASSWORD));
  });
});
