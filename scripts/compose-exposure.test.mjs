// T161 — which ports the compose files publish, read from what Docker Compose
// itself renders (`docker compose config`), not from the YAML text: the trial
// overlay removes the base ports with `!reset` / `!override` tags, and only the
// rendered model says whether that worked (through a YAML anchor it silently
// did not — measured with Compose v5.1.0 while writing this test).
//
// Rules (05 §3.4 "port'lar dışarıya açılmaz", 11 T161, owner decision
// 2026-10-21 — the trial runs on the owner's PC behind a Cloudflare tunnel):
//   * docker-compose.yml (+ the dev override): every published port is bound
//     to 127.0.0.1. Only the edge has a knob (NGINX_BIND_ADDRESS) and it
//     defaults to loopback.
//   * docker-compose.yml + docker-compose.trial.yml: no internal service
//     publishes anything; the three operator ports (edge, Grafana, Uptime
//     Kuma) are loopback-only; cloudflared publishes nothing and cannot start
//     without its token.
//
// Needs the docker CLI with Compose >= 2.24. Without it the suite is skipped
// locally and FAILS in CI (process.env.CI) — a gate that skips is not a gate.

import { test, describe, before, after } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { copyFileSync, appendFileSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname, sep } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const BASE = join(ROOT, "docker-compose.yml");
const OVERRIDE = join(ROOT, "docker-compose.override.yml");
const TRIAL = join(ROOT, "docker-compose.trial.yml");

/** Services of the trial that may publish a (loopback) port — operator access only. */
const TRIAL_OPERATOR_PORTS = {
  "skinora-reverse-proxy": 80,
  "skinora-grafana": 3000,
  "skinora-uptime-kuma": 3001,
};

function dockerAvailable() {
  const probe = spawnSync("docker", ["compose", "version"], { encoding: "utf8" });
  return probe.status === 0;
}

const HAS_DOCKER = dockerAvailable();
if (!HAS_DOCKER && process.env.CI) {
  throw new Error("compose-exposure: docker compose is not available in CI — the exposure gate cannot run.");
}

let workDir;
let stubEnv;
let stubEnvWithoutGrafanaAdmin;

/** `docker compose config` as JSON. Variables only from the stub env file. */
function render(files, { envFile = stubEnv, profiles = [], extraEnv = {} } = {}) {
  const args = ["compose", "--project-directory", ROOT, "--env-file", envFile];
  for (const f of files) args.push("-f", f);
  for (const p of profiles) args.push("--profile", p);
  args.push("config", "--format", "json");
  const out = execFileSync("docker", args, {
    cwd: ROOT,
    encoding: "utf8",
    env: { ...cleanEnv(), ...extraEnv },
    stdio: ["ignore", "pipe", "pipe"],
  });
  return JSON.parse(out);
}

/** The process environment without anything compose would interpolate from it. */
function cleanEnv() {
  const env = { ...process.env };
  for (const name of Object.keys(env)) {
    if (/^(COMPOSE_|NGINX_|TRIAL_|SKINORA_|GRAFANA_|DOCKER_DEFAULT_PLATFORM)/.test(name)) delete env[name];
  }
  return env;
}

/** [{service, hostIp, published, target}] for every published port. */
function published(model) {
  const rows = [];
  for (const [service, def] of Object.entries(model.services)) {
    for (const port of def.ports ?? []) {
      rows.push({ service, hostIp: port.host_ip ?? "", published: String(port.published ?? ""), target: port.target });
    }
  }
  return rows;
}

function assertAllLoopback(rows) {
  const open = rows.filter((r) => r.hostIp !== "127.0.0.1");
  assert.deepEqual(open, [], "every published port must be bound to 127.0.0.1 (a bare HOST:CONTAINER binds 0.0.0.0)");
}

describe("compose port exposure (T161)", { skip: !HAS_DOCKER && "docker compose not available" }, () => {
  before(() => {
    workDir = mkdtempSync(join(tmpdir(), "skinora-exposure-"));
    stubEnvWithoutGrafanaAdmin = join(workDir, "no-grafana.env");
    copyFileSync(join(ROOT, ".env.example"), stubEnvWithoutGrafanaAdmin);
    stubEnv = join(workDir, "stub.env");
    copyFileSync(join(ROOT, ".env.example"), stubEnv);
    appendFileSync(stubEnv, "\nGRAFANA_ADMIN_PASSWORD=exposure-test-stub\n");
    // The marker a real .env.trial carries (runbook K.4); the "no-grafana"
    // stub has it too so that test fails for the Grafana reason, not this one.
    const marker = "SKINORA_TRIAL_STACK=1";
    appendFileSync(stubEnv, `\n${marker}\n`);
    appendFileSync(stubEnvWithoutGrafanaAdmin, `\n${marker}\n`);
  });

  after(() => {
    if (workDir) rmSync(workDir, { recursive: true, force: true });
  });

  test("docker-compose.yml publishes every port on loopback only (all profiles)", () => {
    const rows = published(render([BASE], { profiles: ["ethereum"] }));
    assert.ok(rows.length > 0, "the base file is expected to publish loopback ports for local work");
    assertAllLoopback(rows);
  });

  test("the edge is the only service NGINX_BIND_ADDRESS opens", () => {
    const rows = published(render([BASE], { profiles: ["ethereum"], extraEnv: { NGINX_BIND_ADDRESS: "0.0.0.0" } }));
    const open = rows.filter((r) => r.hostIp !== "127.0.0.1");
    assert.ok(open.length > 0, "NGINX_BIND_ADDRESS=0.0.0.0 should open the edge");
    assert.deepEqual([...new Set(open.map((r) => r.service))], ["skinora-reverse-proxy"]);
  });

  test("T165: the development stack keeps every notification channel on the logging stub", () => {
    const be = render([BASE], { profiles: ["ethereum"] }).services["skinora-backend"].environment;
    assert.equal(be.Resend__Provider, "logging");
    assert.equal(be.Telegram__Provider, "logging");
    assert.equal(be.Discord__Provider, "logging");
  });

  test("the dev override adds no LAN binding (compose appends override port lists)", () => {
    assertAllLoopback(published(render([BASE, OVERRIDE], { profiles: ["ethereum"] })));
  });

  describe("trial overlay", () => {
    let model;
    before(() => {
      model = render([BASE, TRIAL], { profiles: ["ethereum", "tunnel"] });
    });

    test("no internal service publishes a port; operator ports are loopback-only", () => {
      const rows = published(model);
      assertAllLoopback(rows);
      const byService = Object.fromEntries(rows.map((r) => [r.service, r.target]));
      assert.deepEqual(byService, TRIAL_OPERATOR_PORTS);
    });

    test("every base service is present (the overlay hides nothing it does not mean to)", () => {
      const base = render([BASE], { profiles: ["ethereum"] });
      for (const name of Object.keys(base.services)) assert.ok(model.services[name], `${name} missing from the trial`);
    });

    test("is its own project: no fixed container name, own network subnet, own secrets", () => {
      assert.equal(model.name, "skinora-trial");
      for (const [name, def] of Object.entries(model.services)) {
        assert.equal(def.container_name, undefined, `${name} keeps a fixed container_name — it would collide with the dev stack`);
      }
      assert.deepEqual(model.networks["skinora-network"].ipam?.config?.map((c) => c.subnet), ["172.30.161.0/24"]);
      // T162 — dynamic addresses only from the lower half: the connector's
      // fixed address (below) can never be handed to another container.
      assert.deepEqual(model.networks["skinora-network"].ipam?.config?.map((c) => c.ip_range), ["172.30.161.0/25"]);
      const trialDir = join(ROOT, "secrets", "trial") + sep;
      for (const [name, def] of Object.entries(model.secrets)) {
        assert.ok(resolve(def.file).startsWith(trialDir), `${name} must come from secrets/trial/, got ${def.file}`);
      }
    });

    test("runs in production mode and trusts forwarded headers from its own network only", () => {
      const be = model.services["skinora-backend"].environment;
      assert.equal(be.ASPNETCORE_ENVIRONMENT, "Production");
      assert.equal(be.ReverseProxy__KnownNetworks__0, "172.30.161.0/24");
      for (const s of ["skinora-frontend", "skinora-steam-sidecar", "skinora-blockchain-sidecar", "skinora-ethereum-sidecar"]) {
        assert.equal(model.services[s].environment.NODE_ENV, "production", s);
      }
    });

    test("cloudflared publishes nothing, reads its token from a secret file and waits for the preflight", () => {
      const tunnel = model.services["skinora-cloudflared"];
      assert.deepEqual(tunnel.profiles, ["tunnel"]);
      assert.equal(tunnel.ports, undefined);
      assert.equal(tunnel.environment.TUNNEL_TOKEN_FILE, "/run/secrets/cloudflare_tunnel_token");
      assert.equal(tunnel.environment.TUNNEL_TOKEN, undefined, "the token must not travel as a plain env var");
      assert.match(tunnel.image, /^cloudflare\/cloudflared:\d{4}\.\d+\.\d+$/, "pin an exact cloudflared release");
      assert.deepEqual(tunnel.secrets.map((s) => s.source), ["cloudflare_tunnel_token"]);
      assert.equal(tunnel.depends_on["skinora-tunnel-preflight"].condition, "service_completed_successfully");
      const preflight = model.services["skinora-tunnel-preflight"];
      assert.deepEqual(preflight.secrets.map((s) => s.source), ["cloudflare_tunnel_token"]);
      assert.match(preflight.command.join(" "), /exit 1/);
    });

    test("T162: the tunnel connector has the one fixed address nginx.conf trusts, outside the dynamic range", () => {
      const conf = readFileSync(join(ROOT, "nginx", "nginx.conf"), "utf8");
      const realIp = [...conf.matchAll(/^\s*set_real_ip_from\s+([^;\s]+)\s*;/gm)].map((m) => m[1]);
      const connector = model.services["skinora-cloudflared"].networks["skinora-network"]?.ipv4_address;
      assert.equal(connector, "172.30.161.250");
      assert.deepEqual(realIp, [connector], "nginx set_real_ip_from must name exactly the connector's compose address");
      const geoLine = new RegExp(String.raw`^\s*${connector.replaceAll(".", String.raw`\.`)}/32\s+1\s*;`, "m");
      assert.match(conf, geoLine, "the geo map trusts the same /32");
      // Outside 172.30.161.0/25 (the ip_range): last octet >= 128.
      assert.ok(Number(connector.split(".").pop()) >= 128, "the fixed address must lie outside the dynamic ip_range");
      // Nothing else on the network may hold a fixed address: one connector.
      for (const [name, def] of Object.entries(model.services)) {
        if (name === "skinora-cloudflared") continue;
        assert.equal(def.networks?.["skinora-network"]?.ipv4_address, undefined, `${name} has a fixed address`);
      }
    });

    test("T165: production notification channels — e-mail + Telegram real, Discord off, Grafana SMTP on", () => {
      const be = model.services["skinora-backend"].environment;
      assert.equal(be.Resend__Provider, "resend");
      assert.equal(be.Telegram__Provider, "telegram");
      assert.equal(be.Discord__Provider, "disabled");
      // T171a — the channel secrets are Docker secret files, never variables
      // (scripts/compose-secrets.test.mjs pins the map). Empty files boot both
      // real channels "misconfigured" (off + alarm) until the owner fills them.
      assert.equal(be.Resend__ApiKey, undefined);
      assert.equal(be.Telegram__WebhookSecretToken, undefined);
      const grafana = model.services["skinora-grafana"].environment;
      assert.equal(grafana.GF_SMTP_ENABLED, "true");
      assert.equal(grafana.GF_SMTP_HOST, "smtp.resend.com:587");
      assert.equal(grafana.GF_SMTP_STARTTLS_POLICY, "MandatoryStartTLS");
    });

    test("T165: the heartbeat publishes nothing, restarts with Docker and runs the repo script read-only", () => {
      const hb = model.services["skinora-heartbeat"];
      assert.ok(hb, "skinora-heartbeat missing from the trial");
      assert.equal(hb.ports, undefined);
      assert.equal(hb.restart, "unless-stopped");
      assert.match(hb.image, /^curlimages\/curl:\d+\.\d+\.\d+$/, "pin an exact curl image");
      const mount = hb.volumes.find((v) => v.target === "/opt/skinora/heartbeat.sh");
      assert.ok(mount?.read_only, "the script is mounted read-only");
      assert.ok(resolve(mount.source).endsWith(join("scripts", "heartbeat", "heartbeat.sh")));
      // T171a — the ping URL is the Docker secret heartbeat_url, not a variable.
      assert.equal(hb.environment.HEARTBEAT_URL, undefined, "the ping URL must not travel as a variable");
      assert.equal(hb.environment.HEARTBEAT_URL_FILE, "/run/secrets/heartbeat_url");
      assert.match(hb.environment.HEARTBEAT_CHECK_URLS, /http:\/\/skinora-reverse-proxy\/api\/health/);
      // T165 kapanış (Y1, ⚠ PARA): the money alarms leave through Grafana, so a
      // Grafana that is down or never started must stop the pings too. The
      // container-network address: Grafana publishes only on 127.0.0.1.
      const checks = hb.environment.HEARTBEAT_CHECK_URLS.split(/\s+/);
      assert.ok(checks.includes("http://skinora-grafana:3000/api/health"), "Grafana health is a heartbeat check");
      assert.ok(model.services["skinora-grafana"], "the checked Grafana service exists under that name");
    });

    test("T165: Grafana starts through the first-boot guard (no alert to the default example@email.com receiver)", () => {
      // T165 düzeltme turu (doğrulama bulgu 2): without the guard an empty
      // volume ran Grafana's default routing for up to a minute while rules fired.
      const grafana = model.services["skinora-grafana"];
      assert.deepEqual(grafana.entrypoint, ["/bin/sh", "/opt/skinora/first-boot.sh"]);
      const mount = grafana.volumes.find((v) => v.target === "/opt/skinora/first-boot.sh");
      assert.ok(mount?.read_only, "the guard is mounted read-only");
      assert.ok(resolve(mount.source).endsWith(join("scripts", "grafana", "first-boot.sh")));
      const data = grafana.volumes.find((v) => v.target === "/var/lib/grafana");
      assert.equal(data?.type, "volume", "the guard's marker lives on the persistent data volume");
      // T165 kapanış (Y1): the guard reads one Grafana log line, measured on
      // 11.3.0. An upgrade must re-measure it (first-boot.sh header) first.
      assert.equal(grafana.image, "grafana/grafana:11.3.0", "re-measure first-boot.sh's log line before upgrading Grafana");
    });

    test("refuses to render without the .env.trial marker (a forgotten --env-file must not fall back to the development .env)", () => {
      // .env.example is what the development .env is copied from: it must not
      // carry the marker, and the overlay must fail on it.
      assert.doesNotMatch(readFileSync(join(ROOT, ".env.example"), "utf8"), /SKINORA_TRIAL_STACK/);
      const devLike = join(workDir, "dev-like.env");
      copyFileSync(join(ROOT, ".env.example"), devLike);
      appendFileSync(devLike, "\nGRAFANA_ADMIN_PASSWORD=exposure-test-stub\n");
      assert.throws(
        () => render([BASE, TRIAL], { envFile: devLike }),
        (err) => /SKINORA_TRIAL_STACK/.test(String(err.stderr)),
      );
    });

    test("the Grafana admin password is the trial's secret file, never a variable (T171b)", () => {
      // T161's render-time guard (`GRAFANA_ADMIN_PASSWORD:?`) is gone with the
      // variable: the password is secrets/trial/grafana_admin_password.txt and
      // first-boot.sh refuses to start Grafana on a missing or empty file
      // (scripts/grafana/first-boot.test.mjs). A .env.trial without the old
      // variable renders, and no admin password reaches the environment.
      const model = render([BASE, TRIAL], { envFile: stubEnvWithoutGrafanaAdmin });
      const env = model.services["skinora-grafana"].environment;
      assert.equal(env.GF_SECURITY_ADMIN_PASSWORD, undefined);
      assert.equal(env.GF_SECURITY_ADMIN_PASSWORD__FILE, "/run/secrets/grafana_admin_password");
      assert.equal(
        resolve(model.secrets.grafana_admin_password.file),
        join(ROOT, "secrets", "trial", "grafana_admin_password.txt"),
      );
    });
  });
});
