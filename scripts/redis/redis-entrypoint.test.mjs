// T171b — scripts/redis/redis-entrypoint.sh against a fake image entrypoint
// and a fake redis-cli (DEFERRED_BACKLOG ThirdPartyCredentialsInPlainEnv).
// The real image run (redis:7-alpine: the password from the file is the one
// Redis asks for; nothing in `docker inspect` / `ps`) is measured in
// Docs/TASK_REPORTS/T171b_REPORT.md. This suite pins the script's decisions:
//   * the password goes into a config FILE (requirepass), never argv,
//   * no start on a variable, a missing / empty / unreadable file or a
//     character the backend and the Steam service could not carry,
//   * the healthcheck hands it to redis-cli through REDISCLI_AUTH only.
// Needs a POSIX sh (CI: ubuntu dash; locally Git Bash).

import { test, describe, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT = resolve(dirname(fileURLToPath(import.meta.url)), "redis-entrypoint.sh");
const PASSWORD = "fake-t171b-redis-pass_0123456789.~+=";
const slash = (p) => p.replace(/\\/g, "/");

const HAS_SH = spawnSync("sh", ["-c", "true"]).status === 0;
if (!HAS_SH && process.env.CI) throw new Error("redis-entrypoint test: sh is required in CI");

let work;

function fakeEntrypoint() {
  const path = join(work, "docker-entrypoint.sh");
  writeFileSync(
    path,
    [
      "#!/bin/sh",
      `printf 'ARGS %s\\n' "$*" >> "${slash(join(work, "entry.log"))}"`,
      `printf 'ENV REDIS_PASSWORD=%s REDISCLI_AUTH=%s\\n' "\${REDIS_PASSWORD:-}" "\${REDISCLI_AUTH:-}" >> "${slash(join(work, "entry.log"))}"`,
      "exit 0",
      "",
    ].join("\n"),
  );
  chmodSync(path, 0o755);
  return path;
}

// reply: what `redis-cli ping` prints.
function fakeCli(reply) {
  const path = join(work, "redis-cli");
  writeFileSync(
    path,
    [
      "#!/bin/sh",
      `printf 'ARGS %s\\n' "$*" >> "${slash(join(work, "cli.log"))}"`,
      `printf 'AUTH %s\\n' "\${REDISCLI_AUTH:-}" >> "${slash(join(work, "cli.log"))}"`,
      `echo '${reply}'`,
      "",
    ].join("\n"),
  );
  chmodSync(path, 0o755);
  return path;
}

const read = (name) => (existsSync(join(work, name)) ? readFileSync(join(work, name), "utf8") : "");

function run(args, env = {}, reply = "PONG") {
  const res = spawnSync("sh", [slash(SCRIPT), ...args], {
    encoding: "utf8",
    env: {
      PATH: process.env.PATH,
      REDIS_PASSWORD_FILE: slash(join(work, "redis_password")),
      REDIS_AUTH_CONF_DIR: slash(join(work, "conf")),
      REDIS_DOCKER_ENTRYPOINT: slash(fakeEntrypoint()),
      REDIS_CLI: slash(fakeCli(reply)),
      ...env,
    },
  });
  return { code: res.status, out: `${res.stdout}${res.stderr}` };
}

describe("redis-entrypoint.sh (T171b)", { skip: !HAS_SH && "sh not available" }, () => {
  beforeEach(() => {
    work = mkdtempSync(join(tmpdir(), "skinora-redis-entry-"));
    writeFileSync(join(work, "redis_password"), PASSWORD + "\n");
  });
  afterEach(() => rmSync(work, { recursive: true, force: true }));

  test("a good file: requirepass goes into the config file, redis-server gets the file — the password in no argv", () => {
    const r = run(["--appendonly", "yes"]);

    assert.equal(r.code, 0, r.out);
    const conf = join(work, "conf", "redis-auth.conf");
    assert.equal(read(join("conf", "redis-auth.conf")), `requirepass "${PASSWORD}"\n`);
    if (process.platform !== "win32") {
      assert.equal(statSync(conf).mode & 0o077, 0, "the config file is readable by its owner only");
    }
    const log = read("entry.log");
    assert.match(log, new RegExp(`^ARGS redis-server ${slash(conf).replace(/[.]/g, "\\.")} --appendonly yes$`, "m"));
    assert.ok(!log.includes(PASSWORD), "the password reaches no argv / environment of redis-server");
    assert.ok(!r.out.includes(PASSWORD));
  });

  test("--healthcheck: PONG passes; the password travels in REDISCLI_AUTH, never argv", () => {
    const r = run(["--healthcheck"]);

    assert.equal(r.code, 0, r.out);
    const log = read("cli.log");
    assert.ok(log.includes(`AUTH ${PASSWORD}\n`), log);
    for (const line of log.split("\n").filter((l) => l.startsWith("ARGS "))) {
      assert.ok(!line.includes(PASSWORD), `password in argv: ${line}`);
    }
  });

  test("a trailing CRLF (Git Bash openssl on Windows) is accepted and dropped", () => {
    writeFileSync(join(work, "redis_password"), PASSWORD + "\r\n");
    const r = run(["--appendonly", "yes"]);
    assert.equal(r.code, 0, r.out);
    assert.equal(read(join("conf", "redis-auth.conf")), `requirepass "${PASSWORD}"\n`);
  });

  test("--healthcheck: an error reply (wrong password) fails", () => {
    const r = run(["--healthcheck"], {}, "NOAUTH Authentication required.");
    assert.equal(r.code, 1, r.out);
  });

  const refusals = [
    ["the password as REDIS_PASSWORD", () => ({ REDIS_PASSWORD: PASSWORD }), /CREDENTIAL_IN_ENV/],
    ["a missing file", () => ({ REDIS_PASSWORD_FILE: slash(join(work, "nope")) }), /CREDENTIAL_MISSING/],
    [
      "an empty file",
      () => {
        writeFileSync(join(work, "redis_password"), "\n");
        return {};
      },
      /CREDENTIAL_MISSING/,
    ],
    [
      "a directory (Docker's stand-in for a missing source)",
      () => {
        const dir = join(work, "dir_pw");
        mkdirSync(dir);
        return { REDIS_PASSWORD_FILE: slash(dir) };
      },
      /CREDENTIAL_UNREADABLE/,
    ],
    ...[
      ["a comma", "has,comma"],
      ["a quote", 'has"quote'],
      ["a backslash", "has\\backslash"],
      ["a blank", "has blank"],
      ["a slash", "has/slash"],
      ["a CR inside", "pa\rss"],
      ["two trailing CRLFs", "pass\r\n\r"],
      ["two lines", "one\ntwo"],
    ].map(([label, value]) => [
      `a password with ${label}`,
      () => {
        writeFileSync(join(work, "redis_password"), value + "\n");
        return {};
      },
      /CREDENTIAL_INVALID/,
    ]),
  ];

  for (const [label, envOf, code] of refusals) {
    for (const mode of ["start", "--healthcheck"]) {
      test(`${label} (${mode}): exits 1, nothing started, the value is not printed`, () => {
        const env = envOf();
        const r = run(mode === "start" ? ["--appendonly", "yes"] : [mode], env);

        assert.equal(r.code, 1, r.out);
        assert.match(r.out, code);
        assert.equal(read("entry.log"), "", "redis-server must not be started");
        assert.equal(read("cli.log"), "", "redis-cli must not run");
        assert.ok(!existsSync(join(work, "conf", "redis-auth.conf")), "no config file is written");
        assert.ok(!r.out.includes(PASSWORD), "the password is never printed");
      });
    }
  }
});
