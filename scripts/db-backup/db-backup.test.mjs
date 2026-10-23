// T164 — scripts/db-backup/db-backup.sh against a fake sqlcmd. The real
// database run (backup → drop → restore on SQL Server Express) is the drill
// in Docs/TASK_REPORTS/T164_REPORT.md; this suite pins the scheduler's
// decisions so a later edit cannot quietly change them:
//   * when a full / a log backup is due (file names carry the time, so a
//     restart catches up instead of skipping),
//   * what the retention deletes — and that the newest two fulls survive it,
//   * which files a (point-in-time) restore replays, and that it never
//     overwrites the live database,
//   * the password never travels in argv.
// Needs bash with GNU date/find (CI: ubuntu; locally Git Bash).

import { test, describe, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const SCRIPT = resolve(HERE, "db-backup.sh");
const PASSWORD = "Fake-Sa-Password-T164!";

const slash = (p) => p.replace(/\\/g, "/");

// The fake answers the handful of statements the script sends; every call
// (argv + -Q text) is appended to calls.log. State lives in files under FAKE.
const FAKE_SQLCMD = `#!/usr/bin/env bash
q=""; args="$*"
while [ $# -gt 0 ]; do case "$1" in -Q) q="$2"; shift 2;; *) shift;; esac; done
printf '%s\\n' "ARGS $args" >> "$FAKE/calls.log"
printf '%s\\n' "Q $q" >> "$FAKE/calls.log"
printf '%s\\n' "PW $SQLCMDPASSWORD" >> "$FAKE/calls.log"
[ -f "$FAKE/unreachable" ] && { echo "Sqlcmd: Error: Login timeout expired"; exit 1; }
case "$q" in
  *"SELECT state_desc FROM sys.databases"*) cat "$FAKE/state" 2>/dev/null; exit 0;;
  *"SELECT recovery_model_desc"*) cat "$FAKE/recovery"; exit 0;;
  *"SET RECOVERY FULL"*) echo FULL > "$FAKE/recovery"; exit 0;;
  *"SET RECOVERY SIMPLE"*) echo SIMPLE > "$FAKE/recovery"; exit 0;;
  *"BACKUP DATABASE"*)
    if [ -f "$FAKE/fullfail" ]; then echo "Msg 3202, Level 16, State 1: Write on the backup device failed: There is not enough space on the disk."; exit 1; fi;;
  *"BACKUP LOG"*)
    if [ -f "$FAKE/log4214" ]; then echo "Msg 4214, Level 16, State 1: BACKUP LOG cannot be performed because there is no current database backup."; exit 1; fi;;
esac
case "$q" in
  *"BACKUP DATABASE"*|*"BACKUP LOG"*)
    p="$(printf '%s' "$q" | sed -n "s/.*TO DISK = N'\\([^']*\\)'.*/\\1/p")"
    echo "backup" > "$p"; exit 0;;
  *"RESTORE VERIFYONLY"*) [ -f "$FAKE/verifyfail" ] && { echo "Msg 3013, verify failed"; exit 1; }; exit 0;;
  *"RESTORE FILELISTONLY"*)
    echo "Skinora|/var/opt/mssql/data/Skinora.mdf|D|PRIMARY|1|2|3"
    echo "Skinora_log|/var/opt/mssql/data/Skinora_log.ldf|L|NULL|1|2|3"; exit 0;;
  *"SELECT COUNT(*) FROM sys.databases WHERE name = N'"*)
    n="$(printf '%s' "$q" | sed -n "s/.*WHERE name = N'\\([^']*\\)'.*/\\1/p")"
    [ -f "$FAKE/db_$n" ] && echo 1 || echo 0; exit 0;;
  *) exit 0;;
esac
`;

let work, dir, fake;

function stamp(msAgo) {
  const d = new Date(Date.now() - msAgo);
  return d.toISOString().replace(/[-:]/g, "").replace(/\.\d{3}Z$/, "Z");
}
const H = 3600_000;
const D = 24 * H;

function put(name) {
  writeFileSync(join(dir, name), "old backup");
}

function run(args, env = {}) {
  const res = spawnSync("bash", [slash(SCRIPT), ...args], {
    encoding: "utf8",
    env: {
      ...process.env,
      MSSQL_SA_PASSWORD: PASSWORD, // a fake- placeholder, not a secret
      DB_BACKUP_DIR: slash(dir),
      DB_BACKUP_LOCK: slash(join(work, "lock")),
      SQLCMD: slash(join(work, "sqlcmd")),
      FAKE: slash(fake),
      DB_BACKUP_FULL_INTERVAL_HOURS: "24",
      DB_BACKUP_LOG_INTERVAL_MINUTES: "15",
      DB_BACKUP_RETENTION_DAYS: "14",
      ...env,
    },
  });
  return { code: res.status, out: `${res.stdout}${res.stderr}` };
}

const files = () => readdirSync(dir).sort();
const calls = () => (existsSync(join(fake, "calls.log")) ? readFileSync(join(fake, "calls.log"), "utf8") : "");
const queries = () => calls().split("\n").filter((l) => l.startsWith("Q ")).map((l) => l.slice(2));

describe("db-backup.sh (T164)", () => {
  beforeEach(() => {
    work = mkdtempSync(join(tmpdir(), "skinora-dbbackup-"));
    dir = join(work, "backup");
    fake = join(work, "fake");
    mkdirSync(dir);
    mkdirSync(fake);
    writeFileSync(join(work, "sqlcmd"), FAKE_SQLCMD.replace(/\r\n/g, "\n"));
    chmodSync(join(work, "sqlcmd"), 0o755);
    writeFileSync(join(fake, "state"), "ONLINE\n");
    writeFileSync(join(fake, "recovery"), "FULL\n");
  });

  afterEach(() => rmSync(work, { recursive: true, force: true }));

  describe("configuration", () => {
    for (const [name, env, message] of [
      ["no password", { MSSQL_SA_PASSWORD: "" }, /MSSQL_SA_PASSWORD is empty/],
      ["retention shorter than the full interval", { DB_BACKUP_RETENTION_DAYS: "1", DB_BACKUP_FULL_INTERVAL_HOURS: "48" }, /shorter than/],
      ["zero retention", { DB_BACKUP_RETENTION_DAYS: "0" }, /RETENTION_DAYS/],
      ["a database name that is not an identifier", { DB_BACKUP_DATABASE: "Sk];DROP" }, /DB_BACKUP_DATABASE/],
    ]) {
      test(`refuses to start: ${name}`, () => {
        const r = run(["tick"], env);
        assert.equal(r.code, 1, r.out);
        assert.match(r.out, message);
        assert.equal(files().length, 0);
      });
    }

    test("the password reaches sqlcmd through the environment, never argv", () => {
      run(["tick"]);
      const log = calls();
      assert.ok(log.includes(`PW ${PASSWORD}`), "SQLCMDPASSWORD must carry the password");
      for (const line of log.split("\n").filter((l) => l.startsWith("ARGS "))) {
        assert.ok(!line.includes(PASSWORD), `password in argv: ${line}`);
      }
    });
  });

  describe("scheduler tick", () => {
    test("first tick on a fresh folder takes a verified full backup", () => {
      const r = run(["tick"]);
      assert.equal(r.code, 0, r.out);
      const f = files();
      assert.equal(f.length, 1);
      assert.match(f[0], /^Skinora_FULL_\d{8}T\d{6}Z\.bak$/);
      assert.ok(queries().some((q) => q.includes("RESTORE VERIFYONLY")), "the full must be verified");
      assert.ok(queries().some((q) => q.includes("WITH CHECKSUM")));
    });

    test("waits — no backup — while the database does not exist or is not ONLINE", () => {
      writeFileSync(join(fake, "state"), "");
      assert.match(run(["tick"]).out, /does not exist yet/);
      writeFileSync(join(fake, "state"), "RESTORING\n");
      assert.match(run(["tick"]).out, /is RESTORING/);
      writeFileSync(join(fake, "unreachable"), "");
      assert.match(run(["tick"]).out, /not reachable/);
      assert.deepEqual(files(), []);
    });

    test("puts a SIMPLE database into FULL recovery and starts the chain with a full", () => {
      writeFileSync(join(fake, "recovery"), "SIMPLE\n");
      put(`Skinora_FULL_${stamp(1 * H)}.bak`); // recent, but the chain restarts
      run(["tick"]);
      assert.equal(readFileSync(join(fake, "recovery"), "utf8").trim(), "FULL");
      assert.equal(files().filter((f) => f.includes("_FULL_")).length, 2);
    });

    test("log interval 0 keeps the database in SIMPLE recovery and takes no log backups", () => {
      put(`Skinora_FULL_${stamp(2 * H)}.bak`);
      run(["tick"], { DB_BACKUP_LOG_INTERVAL_MINUTES: "0" });
      assert.equal(readFileSync(join(fake, "recovery"), "utf8").trim(), "SIMPLE");
      run(["tick"], { DB_BACKUP_LOG_INTERVAL_MINUTES: "0" });
      assert.equal(files().filter((f) => f.includes("_LOG_")).length, 0);
    });

    test("a recent full and log: nothing is due", () => {
      put(`Skinora_FULL_${stamp(2 * H)}.bak`);
      put(`Skinora_LOG_${stamp(5 * 60_000)}.trn`);
      run(["tick"]);
      assert.equal(files().length, 2);
    });

    test("log due 15 minutes after the newest log (or full)", () => {
      put(`Skinora_FULL_${stamp(2 * H)}.bak`);
      put(`Skinora_LOG_${stamp(20 * 60_000)}.trn`);
      run(["tick"]);
      assert.equal(files().filter((f) => f.includes("_LOG_")).length, 2);
    });

    test("catch-up after a restart: a full older than 24 h is taken on the first tick", () => {
      put(`Skinora_FULL_${stamp(30 * H)}.bak`);
      run(["tick"]);
      assert.equal(files().filter((f) => f.includes("_FULL_")).length, 2);
    });

    test("a log backup refused for lack of a full (Msg 4214) takes a full instead", () => {
      put(`Skinora_FULL_${stamp(2 * H)}.bak`);
      writeFileSync(join(fake, "log4214"), "");
      const r = run(["tick"]);
      assert.match(r.out, /FAILED LOG/);
      assert.equal(files().filter((f) => f.includes("_FULL_")).length, 2);
    });

    test("a failing full does not stop the log backups, and is retried an hour later — not every tick", () => {
      // T164 düzeltme turu (S3 F3): disk full / verify failure on the full
      // used to return before the log step, so RPO grew to "until fixed".
      put(`Skinora_FULL_${stamp(25 * H)}.bak`);
      put(`Skinora_LOG_${stamp(20 * 60_000)}.trn`);
      writeFileSync(join(fake, "fullfail"), "");
      const r = run(["tick"]);
      assert.equal(r.code, 0, r.out);
      assert.match(r.out, /FAILED FULL .*Msg 3202/);
      assert.match(r.out, /retried in 60 min; log backups continue/);
      assert.equal(files().filter((f) => f.includes("_LOG_")).length, 2, "the log backup still ran");
      assert.equal(files().filter((f) => f.endsWith(".inprogress")).length, 0);
      const mark = join(work, "lock.full-failed");
      assert.ok(existsSync(mark));

      // Next tick inside the hour: no second attempt at the full.
      const before = queries().filter((q) => q.includes("BACKUP DATABASE")).length;
      run(["tick"]);
      assert.equal(queries().filter((q) => q.includes("BACKUP DATABASE")).length, before);

      // An hour later it is tried again; once it succeeds the mark is gone.
      writeFileSync(mark, String(Math.floor(Date.now() / 1000) - 61 * 60));
      rmSync(join(fake, "fullfail"));
      run(["tick"]);
      assert.equal(queries().filter((q) => q.includes("BACKUP DATABASE")).length, before + 1);
      assert.equal(files().filter((f) => f.includes("_FULL_")).length, 2);
      assert.equal(existsSync(mark), false);
    });

    test("a failing first full takes no log backup (there is no chain to extend)", () => {
      writeFileSync(join(fake, "fullfail"), "");
      run(["tick"]);
      assert.ok(!queries().some((q) => q.includes("BACKUP LOG")));
      assert.deepEqual(files(), []);
    });

    test("two backups in the same second never share a file name (the second waits)", () => {
      put(`Skinora_FULL_${stamp(2 * H)}.bak`);
      const taken = [0, 1000, 2000].map((ms) => `Skinora_LOG_${stamp(-ms)}.trn`);
      for (const f of taken) put(f);
      const r = run(["log"]);
      assert.equal(r.code, 0, r.out);
      const logs = files().filter((f) => f.includes("_LOG_"));
      assert.equal(logs.length, 4, logs.join(" "));
      for (const f of taken) assert.equal(readFileSync(join(dir, f), "utf8"), "old backup", `${f} was overwritten`);
    });

    test("a backup that fails verification leaves no file behind", () => {
      writeFileSync(join(fake, "verifyfail"), "");
      const r = run(["tick"]);
      assert.match(r.out, /FAILED FULL .*verify/);
      assert.deepEqual(files(), []);
    });
  });

  describe("retention", () => {
    test("deletes fulls past the retention and the logs older than the oldest kept full", () => {
      // The newest full BEFORE the 14-day cutoff stays (with its logs): a
      // point in time just after the cutoff restores from it, so the window
      // is the full 14 days (T164 düzeltme turu, S4 F6).
      const bridgeFull = `Skinora_FULL_${stamp(16 * D)}.bak`;
      const bridgeLog = `Skinora_LOG_${stamp(13.5 * D)}.trn`;
      const keepFull = `Skinora_FULL_${stamp(10 * D)}.bak`;
      const keepLog = `Skinora_LOG_${stamp(10 * D - H)}.trn`;
      put(`Skinora_FULL_${stamp(20 * D)}.bak`);
      put(`Skinora_LOG_${stamp(20 * D - H)}.trn`);
      put(`Skinora_FULL_${stamp(18 * D)}.bak`);
      put(`Skinora_LOG_${stamp(17 * D)}.trn`);
      put(bridgeFull);
      put(bridgeLog);
      put(keepFull);
      put(keepLog);
      const newest = `Skinora_FULL_${stamp(1 * D)}.bak`;
      put(newest);
      const r = run(["prune"]);
      assert.equal(r.code, 0, r.out);
      assert.deepEqual(files(), [bridgeFull, keepFull, newest, bridgeLog, keepLog].sort());
      assert.ok(queries().some((q) => q.includes("sp_delete_backuphistory")), "msdb history shrinks with the files");
    });

    test("never deletes the newest two fulls, however old (a stalled schedule keeps its last backups)", () => {
      put(`Skinora_FULL_${stamp(40 * D)}.bak`);
      const a = `Skinora_FULL_${stamp(30 * D)}.bak`;
      const b = `Skinora_FULL_${stamp(20 * D)}.bak`;
      put(a);
      put(b);
      run(["prune"]);
      assert.deepEqual(files(), [a, b]);
    });

    test("removes a day-old half-written file", () => {
      put(`Skinora_FULL_${stamp(1 * D)}.bak`);
      put(`Skinora_FULL_${stamp(2 * D)}.bak`);
      const half = join(dir, "Skinora_FULL_20200101T000000Z.bak.inprogress");
      writeFileSync(half, "half");
      const old = new Date(Date.now() - 2 * D);
      // utimes through the shell: touch -d is the same tool the server has.
      spawnSync("bash", ["-c", `touch -d '${old.toISOString()}' '${slash(half)}'`]);
      run(["prune"]);
      assert.equal(existsSync(half), false);
    });
  });

  describe("manual backups", () => {
    test("full <tag> names the file; a bad tag is refused", () => {
      assert.equal(run(["full", "predeploy"]).code, 0);
      assert.match(files()[0], /^Skinora_FULL_\d{8}T\d{6}Z_predeploy\.bak$/);
      assert.equal(run(["full", "Bad Tag"]).code, 1);
    });
  });

  describe("restore", () => {
    test("replays the newest full and every later log, into a new name, then recovers", () => {
      const f1 = `Skinora_FULL_${stamp(2 * D)}.bak`;
      const f2 = `Skinora_FULL_${stamp(1 * D)}.bak`;
      const l0 = `Skinora_LOG_${stamp(1 * D + H)}.trn`; // before f2: not needed
      const l1 = `Skinora_LOG_${stamp(1 * D - H)}.trn`;
      const l2 = `Skinora_LOG_${stamp(1 * D - 2 * H)}.trn`;
      for (const f of [f1, f2, l0, l1, l2]) put(f);
      const r = run(["restore", "Skinora_restored"]);
      assert.equal(r.code, 0, r.out);
      const restores = queries().filter((q) => /^RESTORE (DATABASE|LOG) \[Skinora_restored\]/.test(q.replace(/^SET NOCOUNT ON; /, "")));
      assert.equal(restores.length, 4, restores.join("\n"));
      assert.match(restores[0], new RegExp(`RESTORE DATABASE \\[Skinora_restored\\] FROM DISK = N'[^']*${f2}' WITH NORECOVERY, CHECKSUM, MOVE N'Skinora' TO N'/var/opt/mssql/data/Skinora_restored_\\d{8}T\\d{6}Z\\.mdf', MOVE N'Skinora_log' TO N'/var/opt/mssql/data/Skinora_restored_\\d{8}T\\d{6}Z_log\\.ldf'`));
      assert.match(restores[1], new RegExp(`RESTORE LOG \\[Skinora_restored\\] FROM DISK = N'[^']*${l1}'`));
      assert.match(restores[2], new RegExp(`RESTORE LOG \\[Skinora_restored\\] FROM DISK = N'[^']*${l2}'`));
      assert.match(restores[3], /RESTORE DATABASE \[Skinora_restored\] WITH RECOVERY/);
    });

    test("point in time: the full before stopat, logs up to the first one at or after it, STOPAT on each", () => {
      const now = Date.now();
      const iso = (ms) => new Date(ms).toISOString().replace(/\.\d{3}Z$/, "Z");
      const full = `Skinora_FULL_${stamp(5 * H)}.bak`;
      const after = `Skinora_FULL_${stamp(1 * H)}.bak`; // after stopat: not used
      const l1 = `Skinora_LOG_${stamp(4 * H)}.trn`;
      const l2 = `Skinora_LOG_${stamp(3 * H)}.trn`; // first at/after stopat
      const l3 = `Skinora_LOG_${stamp(2 * H)}.trn`; // past it: not used
      for (const f of [full, after, l1, l2, l3]) put(f);
      const stopat = iso(now - 3.5 * H);
      const r = run(["restore", "Skinora_pit", stopat]);
      assert.equal(r.code, 0, r.out);
      const q = queries().filter((x) => x.includes("[Skinora_pit] FROM DISK"));
      assert.equal(q.length, 3, q.join("\n"));
      assert.ok(q[0].includes(full));
      assert.ok(q[1].includes(l1) && q[1].includes(`STOPAT = N'${stopat.slice(0, 10)} ${stopat.slice(11, 19)}'`));
      assert.ok(q[2].includes(l2));
    });

    test("refuses a stopat later than the newest backup instead of silently ending earlier", () => {
      // T164 düzeltme turu (S3 F2 ⚠ PARA): the last log was taken 1 h ago;
      // asking for 10 min ago would quietly restore the state of 1 h ago.
      const iso = (ms) => new Date(Date.now() - ms).toISOString().replace(/\.\d{3}Z$/, "Z");
      put(`Skinora_FULL_${stamp(5 * H)}.bak`);
      put(`Skinora_LOG_${stamp(2 * H)}.trn`);
      const last = `Skinora_LOG_${stamp(1 * H)}.trn`;
      put(last);
      const r = run(["restore", "Skinora_late", iso(10 * 60_000)]);
      assert.equal(r.code, 1, r.out);
      assert.match(r.out, new RegExp(`later than the newest backup \\(${last}`));
      assert.match(r.out, /Take a log backup first/);
      assert.ok(!queries().some((q) => q.includes("RESTORE DATABASE")), "nothing restored");
    });

    test("says which point it reached: the end of the last file, or the stopat", () => {
      put(`Skinora_FULL_${stamp(5 * H)}.bak`);
      const last = `Skinora_LOG_${stamp(1 * H)}.trn`;
      put(last);
      const r = run(["restore", "Skinora_r"]);
      assert.equal(r.code, 0, r.out);
      assert.match(r.out, new RegExp(`OK restored Skinora_r \\(2 file\\(s\\)\\) to the end of ${last} \\(taken \\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\dZ\\)`));
      const stopat = new Date(Date.now() - 2 * H).toISOString().replace(/\.\d{3}Z$/, "Z");
      const p = run(["restore", "Skinora_p", stopat]);
      assert.equal(p.code, 0, p.out);
      assert.match(p.out, new RegExp(`to the ${stopat} \\(STOPAT\\)`));
    });

    test("a failed restore step says the half-restored target must be dropped", () => {
      put(`Skinora_FULL_${stamp(5 * H)}.bak`);
      put(`Skinora_LOG_${stamp(1 * H)}.trn`);
      writeFileSync(join(work, "sqlcmd"), FAKE_SQLCMD.replace(/\r\n/g, "\n").replace(
        `case "$q" in\n  *"SELECT state_desc`,
        `case "$q" in\n  *"RESTORE LOG"*) echo "Msg 3186, The backup set has been damaged"; exit 1;;\n  *"SELECT state_desc`));
      const r = run(["restore", "Skinora_bad"]);
      assert.equal(r.code, 1, r.out);
      assert.match(r.out, /Msg 3186.*left RESTORING: DROP DATABASE \[Skinora_bad\]/);
    });

    test("never restores over the live database or an existing one", () => {
      put(`Skinora_FULL_${stamp(1 * H)}.bak`);
      assert.match(run(["restore", "Skinora"]).out, /never overwrites/);
      writeFileSync(join(fake, "db_Skinora_restored"), "");
      assert.match(run(["restore", "Skinora_restored"]).out, /already exists/);
      assert.ok(!queries().some((q) => q.includes("RESTORE DATABASE")));
    });

    test("swap keeps the old database under a new name and starts a fresh chain", () => {
      writeFileSync(join(fake, "db_Skinora"), "");
      writeFileSync(join(fake, "db_Skinora_restored"), "");
      const r = run(["swap", "Skinora_restored"]);
      assert.equal(r.code, 0, r.out);
      const q = queries().join("\n");
      assert.match(q, /ALTER DATABASE \[Skinora\] MODIFY NAME = \[Skinora_before_restore_\d{8}T\d{6}Z\]/);
      assert.match(q, /ALTER DATABASE \[Skinora_restored\] MODIFY NAME = \[Skinora\]/);
      assert.match(files().join(" "), /Skinora_FULL_\d{8}T\d{6}Z_postrestore\.bak/);
    });
  });
});
