// T164 — the backup service as Docker Compose renders it (`docker compose
// config`), base file and trial overlay. The scheduler's own logic is pinned
// by db-backup.test.mjs; this suite pins the wiring the drill relied on:
//   * the database server and the backup container see the SAME host folder
//     at the SAME path (BACKUP ... TO DISK is a server-side path),
//   * that folder is on the host (bind), not in a named volume `down -v` drops,
//   * the trial writes into its own folder, never the development one (both
//     databases are named Skinora — the files would mix), and runs Express,
//   * the folder is gitignored, the service publishes nothing and restarts
//     with Docker Desktop.
// Needs the docker CLI with Compose >= 2.24; skipped locally without it,
// FAILS in CI (process.env.CI) — same rule as compose-exposure.test.mjs.

import { test, describe, before, after } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { appendFileSync, copyFileSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname, sep } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const BASE = join(ROOT, "docker-compose.yml");
const TRIAL = join(ROOT, "docker-compose.trial.yml");
const MOUNT = "/var/opt/mssql/backup";

const HAS_DOCKER = spawnSync("docker", ["compose", "version"], { encoding: "utf8" }).status === 0;
if (!HAS_DOCKER && process.env.CI) {
  throw new Error("compose-backup: docker compose is not available in CI — the wiring check cannot run.");
}

let workDir;
let stubEnv;

function cleanEnv() {
  const env = { ...process.env };
  for (const name of Object.keys(env)) {
    if (/^(COMPOSE_|NGINX_|TRIAL_|SKINORA_|GRAFANA_|DB_BACKUP_|MSSQL_|DOCKER_DEFAULT_PLATFORM)/.test(name)) delete env[name];
  }
  return env;
}

function render(files, extraEnv = {}) {
  const args = ["compose", "--project-directory", ROOT, "--env-file", stubEnv];
  for (const f of files) args.push("-f", f);
  args.push("config", "--format", "json");
  return JSON.parse(
    execFileSync("docker", args, { cwd: ROOT, encoding: "utf8", env: { ...cleanEnv(), ...extraEnv }, stdio: ["ignore", "pipe", "pipe"] }),
  );
}

/** The bind mount of `service` at the backup path. */
function backupMount(model, service) {
  const mounts = (model.services[service].volumes ?? []).filter((v) => v.target === MOUNT);
  assert.equal(mounts.length, 1, `${service} must mount exactly one folder at ${MOUNT}`);
  assert.equal(mounts[0].type, "bind", `${service}: backups must go to a host folder, not a named volume`);
  return resolve(mounts[0].source);
}

describe("database backup wiring (T164)", { skip: !HAS_DOCKER && "docker compose not available" }, () => {
  before(() => {
    workDir = mkdtempSync(join(tmpdir(), "skinora-backup-compose-"));
    stubEnv = join(workDir, "stub.env");
    copyFileSync(join(ROOT, ".env.example"), stubEnv);
    appendFileSync(stubEnv, "\nGRAFANA_ADMIN_PASSWORD=backup-test-stub\nSKINORA_TRIAL_STACK=1\n");
  });

  after(() => {
    if (workDir) rmSync(workDir, { recursive: true, force: true });
  });

  test("base: the server and the backup container share one host folder at the same path", () => {
    const model = render([BASE]);
    const svc = model.services["skinora-db-backup"];
    assert.ok(svc, "skinora-db-backup missing");
    assert.equal(backupMount(model, "skinora-db"), backupMount(model, "skinora-db-backup"));
    assert.equal(backupMount(model, "skinora-db"), resolve(ROOT, "backups", "db"));
    assert.equal(svc.image, model.services["skinora-db"].image, "same image as the database: sqlcmd, nothing new to pull");
    assert.deepEqual(svc.entrypoint, ["/bin/bash", "/opt/skinora/db-backup.sh"]);
    const script = svc.volumes.find((v) => v.target === "/opt/skinora/db-backup.sh");
    assert.equal(resolve(script.source), resolve(ROOT, "scripts", "db-backup", "db-backup.sh"));
    assert.equal(script.read_only, true);
    assert.equal(svc.restart, "unless-stopped", "must come back with Docker Desktop after a reboot");
    assert.equal(svc.depends_on["skinora-db"].condition, "service_healthy");
    assert.equal(svc.ports, undefined, "the backup container publishes nothing");
    // T171b — the server and the backup container read the SAME secret file; no variable.
    for (const name of ["skinora-db", "skinora-db-backup"]) {
      const s = model.services[name];
      assert.equal(s.environment.MSSQL_SA_PASSWORD, undefined, `${name} must not carry the password as a variable`);
      assert.equal(s.environment.MSSQL_SA_PASSWORD_FILE, "/run/secrets/mssql_sa_password");
      assert.deepEqual((s.secrets ?? []).map((x) => x.source), ["mssql_sa_password"]);
    }
    assert.equal(svc.environment.DB_BACKUP_SERVER, "skinora-db");
    // The T164 proposal (⚠): full 24 h, log 15 min, 14 days.
    assert.equal(svc.environment.DB_BACKUP_FULL_INTERVAL_HOURS, "24");
    assert.equal(svc.environment.DB_BACKUP_LOG_INTERVAL_MINUTES, "15");
    assert.equal(svc.environment.DB_BACKUP_RETENTION_DAYS, "14");
  });

  test("trial: its own folder (never the development one) and SQL Server Express", () => {
    const base = render([BASE]);
    const trial = render([BASE, TRIAL]);
    const dir = backupMount(trial, "skinora-db");
    assert.equal(dir, backupMount(trial, "skinora-db-backup"));
    assert.equal(dir, resolve(ROOT, "backups", "trial", "db"));
    assert.notEqual(dir, backupMount(base, "skinora-db"));
    assert.equal(trial.services["skinora-db"].environment.MSSQL_PID, "Express");
    assert.equal(trial.services["skinora-db-backup"].container_name, undefined);
  });

  test("trial: TRIAL_DB_BACKUP_DIR moves both mounts together; DB_BACKUP_HOST_DIR (the development knob) does not", () => {
    const other = join(workDir, "other-disk");
    const moved = render([BASE, TRIAL], { TRIAL_DB_BACKUP_DIR: other });
    assert.equal(backupMount(moved, "skinora-db"), resolve(other));
    assert.equal(backupMount(moved, "skinora-db-backup"), resolve(other));
    const dev = render([BASE, TRIAL], { DB_BACKUP_HOST_DIR: join(workDir, "dev-dir") });
    assert.equal(backupMount(dev, "skinora-db"), resolve(ROOT, "backups", "trial", "db"));
  });

  test("the backup folders are gitignored", () => {
    for (const p of ["backups/db/Skinora_FULL_20261022T120000Z.bak", "backups/trial/db/Skinora_LOG_20261022T120000Z.trn"]) {
      const r = spawnSync("git", ["check-ignore", "-q", p], { cwd: ROOT });
      assert.equal(r.status, 0, `${p} is not ignored`);
    }
    assert.match(readFileSync(join(ROOT, ".gitignore"), "utf8"), /^backups\/$/m);
  });
});
