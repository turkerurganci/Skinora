// T171b — scripts/db/mssql-entrypoint.sh against a fake launch script and a
// fake sqlcmd (DEFERRED_BACKLOG ThirdPartyCredentialsInPlainEnv). The real
// image run — SQL Server reading MSSQL_SA_PASSWORD_FILE itself, and starting
// WITHOUT a usable password when the guard is not there — is measured in
// Docs/TASK_REPORTS/T171b_REPORT.md. This suite pins the guard's decisions:
//   * no start on a variable, a missing / empty / unreadable file, a CR or
//     surrounding blanks (the fail-closed rule; SQL Server itself starts),
//   * no start on a password SQL Server's policy refuses (CREDENTIAL_WEAK —
//     SQL Server itself exits 255 on an empty volume, S4-1),
//   * the password never reaches argv or the output,
//   * the healthcheck hands it to sqlcmd through SQLCMDPASSWORD only.
// Needs bash (CI: ubuntu; locally Git Bash).

import { test, describe, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT = resolve(dirname(fileURLToPath(import.meta.url)), "mssql-entrypoint.sh");
const PASSWORD = "Fake-T171b-Sa!Pass0123";
const slash = (p) => p.replace(/\\/g, "/");

const HAS_BASH = spawnSync("bash", ["-c", "true"]).status === 0;
if (!HAS_BASH && process.env.CI) throw new Error("mssql-entrypoint test: bash is required in CI");

let work;

// Records its argv and the environment it was started with (only the
// password variables), then exits 0 — stands in for launch_sqlservr.sh / sqlcmd.
function fake(name) {
  const path = join(work, name);
  writeFileSync(
    path,
    [
      "#!/usr/bin/env bash",
      `printf 'ARGS %s\\n' "$*" >> "${slash(join(work, name + ".log"))}"`,
      `printf 'ENV MSSQL_SA_PASSWORD=%s SQLCMDPASSWORD=%s FILE=%s\\n' "\${MSSQL_SA_PASSWORD:-}" "\${SQLCMDPASSWORD:-}" "\${MSSQL_SA_PASSWORD_FILE:-}" >> "${slash(join(work, name + ".log"))}"`,
      "exit 0",
      "",
    ].join("\n"),
  );
  chmodSync(path, 0o755);
  return path;
}

const logOf = (name) => (existsSync(join(work, name + ".log")) ? readFileSync(join(work, name + ".log"), "utf8") : "");

function run(args, env = {}) {
  const res = spawnSync("bash", [slash(SCRIPT), ...args], {
    encoding: "utf8",
    env: {
      PATH: process.env.PATH,
      MSSQL_LAUNCH: slash(fake("launch")),
      SQLCMD: slash(fake("sqlcmd")),
      MSSQL_SA_PASSWORD_FILE: slash(join(work, "mssql_sa_password")),
      ...env,
    },
  });
  return { code: res.status, out: `${res.stdout}${res.stderr}` };
}

describe("mssql-entrypoint.sh (T171b)", { skip: !HAS_BASH && "bash not available" }, () => {
  beforeEach(() => {
    work = mkdtempSync(join(tmpdir(), "skinora-mssql-entry-"));
    writeFileSync(join(work, "mssql_sa_password"), PASSWORD + "\n");
  });
  afterEach(() => rmSync(work, { recursive: true, force: true }));

  test("a good file: starts the image's launch script with the given command, the password in no argv and no variable", () => {
    const r = run(["/opt/mssql/bin/sqlservr"]);

    assert.equal(r.code, 0, r.out);
    const log = logOf("launch");
    assert.match(log, /^ARGS \/opt\/mssql\/bin\/sqlservr$/m);
    assert.match(log, /ENV MSSQL_SA_PASSWORD= SQLCMDPASSWORD= FILE=/, "SQL Server reads the FILE itself");
    assert.ok(!log.includes(PASSWORD), "the password reaches no argv / environment of the server");
    assert.ok(!r.out.includes(PASSWORD));
  });

  test("a file without a trailing newline is fine too", () => {
    writeFileSync(join(work, "mssql_sa_password"), PASSWORD);
    assert.equal(run(["--check"]).code, 0);
  });

  test("a trailing CRLF (Git Bash openssl on Windows) is accepted and dropped, as SQL Server does (measured)", () => {
    writeFileSync(join(work, "mssql_sa_password"), PASSWORD + "\r\n");
    const r = run(["--healthcheck"]);
    assert.equal(r.code, 0, r.out);
    assert.ok(logOf("sqlcmd").includes(`SQLCMDPASSWORD=${PASSWORD} `), "the CR does not reach sqlcmd");
  });

  test("the sa password policy: exactly three classes and 8 characters pass, each three-of-four set passes", () => {
    for (const pw of ["Abcdefg1", "abcdefg1!", "ABCDEFG1!", "Abcdefg!"]) {
      writeFileSync(join(work, "mssql_sa_password"), pw + "\n");
      const r = run(["--check"]);
      assert.equal(r.code, 0, `${pw}: ${r.out}`);
    }
  });

  test("--healthcheck: sqlcmd gets the password through SQLCMDPASSWORD, never argv", () => {
    const r = run(["--healthcheck"]);

    assert.equal(r.code, 0, r.out);
    const log = logOf("sqlcmd");
    assert.ok(log.includes(`SQLCMDPASSWORD=${PASSWORD} `), log);
    for (const line of log.split("\n").filter((l) => l.startsWith("ARGS "))) {
      assert.ok(!line.includes(PASSWORD), `password in argv: ${line}`);
      assert.match(line, /-U sa/);
    }
  });

  const refusals = [
    ["the password as MSSQL_SA_PASSWORD", () => ({ MSSQL_SA_PASSWORD: PASSWORD }), /CREDENTIAL_IN_ENV/],
    ["the password as SA_PASSWORD", () => ({ SA_PASSWORD: PASSWORD }), /CREDENTIAL_IN_ENV/],
    ["no MSSQL_SA_PASSWORD_FILE", () => ({ MSSQL_SA_PASSWORD_FILE: "" }), /CREDENTIAL_MISSING/],
    ["a missing file", () => ({ MSSQL_SA_PASSWORD_FILE: slash(join(work, "nope")) }), /CREDENTIAL_MISSING/],
    [
      "an empty file",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), "\n");
        return {};
      },
      /CREDENTIAL_MISSING/,
    ],
    [
      "a blank file",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), "   \n");
        return {};
      },
      /CREDENTIAL_MISSING/,
    ],
    [
      "a directory (Docker's stand-in for a missing source)",
      () => {
        const dir = join(work, "dir_pw");
        mkdirSync(dir);
        return { MSSQL_SA_PASSWORD_FILE: slash(dir) };
      },
      /CREDENTIAL_UNREADABLE/,
    ],
    [
      "a CR inside the password",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), "Fake-T171b\rSa!Pass0123\n");
        return {};
      },
      /CREDENTIAL_INVALID/,
    ],
    [
      "two trailing CRLFs",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), PASSWORD + "\r\n\r\n");
        return {};
      },
      /CREDENTIAL_INVALID/,
    ],
    [
      "surrounding blanks",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), ` ${PASSWORD}\n`);
        return {};
      },
      /CREDENTIAL_INVALID/,
    ],
    [
      "two lines",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), `${PASSWORD}\nsecond\n`);
        return {};
      },
      /CREDENTIAL_INVALID/,
    ],
    [
      "an `openssl rand -hex` value (lower case + digits only, SQL Server exits 255 on it — S4-1)",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), "3f9a0c7e5b1d2468ace013579bdf2468ace013579bdf2468ace013579bdf24\n");
        return {};
      },
      /CREDENTIAL_WEAK/,
    ],
    [
      "two character classes only (upper + lower)",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), "FakeOnlyLettersHere\n");
        return {};
      },
      /CREDENTIAL_WEAK/,
    ],
    [
      "three classes but shorter than 8",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), "Ab1!xyz\n");
        return {};
      },
      /CREDENTIAL_WEAK/,
    ],
    [
      "longer than 128",
      () => {
        writeFileSync(join(work, "mssql_sa_password"), "Aa1!xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n");
        return {};
      },
      /CREDENTIAL_WEAK/,
    ],
    [
      "a file that passes -f / -r but cannot be read (Docker Desktop mount point, measured — S4-4 D8)",
      () => {
        const probe = join(work, "failing-cat");
        writeFileSync(probe, "#!/usr/bin/env bash\nexit 1\n");
        chmodSync(probe, 0o755);
        return { MSSQL_READ_PROBE: slash(probe) };
      },
      /CREDENTIAL_UNREADABLE/,
    ],
  ];

  for (const [label, envOf, code] of refusals) {
    for (const mode of ["start", "--healthcheck"]) {
      test(`${label} (${mode}): exits 1, nothing started, the value is not printed`, () => {
        const env = envOf();
        const r = run(mode === "start" ? ["/opt/mssql/bin/sqlservr"] : [mode], env);

        assert.equal(r.code, 1, r.out);
        assert.match(r.out, code);
        assert.equal(logOf("launch"), "", "SQL Server must not be started");
        assert.equal(logOf("sqlcmd"), "", "sqlcmd must not run");
        assert.ok(!r.out.includes(PASSWORD), "the password is never printed");
      });
    }
  }
});
