// T171a — which service sees which Docker secret, read from what Docker Compose
// itself renders (`docker compose config`), not from the YAML text
// (DEFERRED_BACKLOG SharedSecretsInPlainEnv; T163 pattern, 05 §3.5).
//
// Rules:
//   * Every secret is mounted ONLY into the services that use it. A shared
//     secret (JWT, webhook HMAC, internal key, notification channel secret)
//     reaches the backend and the one service at the other end, from ONE
//     top-level secret — so the two ends read the same file and cannot drift.
//   * No service receives any of them as an environment variable (the backend
//     and the sidecars refuse to start on one outside development anyway).
//   * The file names the backend reads (SharedSecretFiles.cs) and the paths
//     the sidecars read (config/sharedSecrets.ts) are the mounted names.
//   * The trial overlay takes every secret from secrets/trial/; the e2e stack
//     (Production backend) mounts its fixed test values at the same paths.
//
// Needs the docker CLI with Compose >= 2.24. Without it the suite is skipped
// locally and FAILS in CI (process.env.CI) — same rule as compose-exposure.

import { test, describe, before, after } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { appendFileSync, copyFileSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname, sep } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const BASE = join(ROOT, "docker-compose.yml");
const TRIAL = join(ROOT, "docker-compose.trial.yml");
const E2E = join(ROOT, "docker-compose.e2e.yml");

const HAS_DOCKER = spawnSync("docker", ["compose", "version"], { encoding: "utf8" }).status === 0;
if (!HAS_DOCKER && process.env.CI) {
  throw new Error("compose-secrets: docker compose is not available in CI — the secret map cannot be checked.");
}

/** The whole map: service → the Docker secrets it mounts (sorted). Nothing else mounts any. */
const EXPECTED = {
  "skinora-backend": [
    "blockchain_sidecar_internal_key",
    "blockchain_webhook_secret",
    "ethereum_sidecar_internal_key",
    "ethereum_webhook_secret",
    "jwt_secret",
    "resend_api_key",
    "resend_webhook_signing_secret",
    "steam_sidecar_internal_key",
    "telegram_bot_token",
    "telegram_webhook_secret_token",
  ],
  "skinora-steam-sidecar": ["steam_sidecar_internal_key"],
  "skinora-blockchain-sidecar": [
    "blockchain_sidecar_internal_key",
    "blockchain_webhook_secret",
    "tron_hd_mnemonic",
    "tron_hot_wallet_private_key",
  ],
  "skinora-ethereum-sidecar": [
    "ethereum_fuel_wallet_private_key",
    "ethereum_hd_mnemonic",
    "ethereum_hot_wallet_private_key",
    "ethereum_sidecar_internal_key",
    "ethereum_webhook_secret",
  ],
  "skinora-grafana-provisioning": ["telegram_bot_token"],
  "skinora-grafana": ["grafana_smtp_password"],
};

/** Trial-only services on top of the base map. */
const EXPECTED_TRIAL_EXTRA = {
  "skinora-heartbeat": ["heartbeat_url"],
  "skinora-tunnel-preflight": ["cloudflare_tunnel_token"],
  "skinora-cloudflared": ["cloudflare_tunnel_token"],
};

/** The two ends of every shared secret (backend + the other service). */
const PAIRS = [
  ["steam_sidecar_internal_key", "skinora-steam-sidecar"],
  ["blockchain_sidecar_internal_key", "skinora-blockchain-sidecar"],
  ["blockchain_webhook_secret", "skinora-blockchain-sidecar"],
  ["ethereum_sidecar_internal_key", "skinora-ethereum-sidecar"],
  ["ethereum_webhook_secret", "skinora-ethereum-sidecar"],
  ["telegram_bot_token", "skinora-grafana-provisioning"],
];

/** Environment names that would carry a shared secret (backend keys and sidecar variables). */
const FORBIDDEN_ENV =
  /^(Jwt__Secret|Jwt__PreviousSecret|Webhook__(Blockchain|Ethereum)SharedSecret|(Steam|Blockchain|Ethereum)Sidecar__InternalKey|Resend__(ApiKey|WebhookSigningSecret)|Telegram__(BotToken|WebhookSecretToken)|INTERNAL_KEY|WEBHOOK_SECRET|TELEGRAM_BOT_TOKEN|GF_SMTP_PASSWORD|HEARTBEAT_URL)$/;

let workDir;
let stubEnv;

function render(files, { envFile = stubEnv, profiles = [] } = {}) {
  const args = ["compose", "--project-directory", ROOT];
  if (envFile) args.push("--env-file", envFile);
  for (const f of files) args.push("-f", f);
  for (const p of profiles) args.push("--profile", p);
  args.push("config", "--format", "json");
  const env = { ...process.env };
  for (const name of Object.keys(env)) {
    if (/^(COMPOSE_|NGINX_|TRIAL_|SKINORA_|GRAFANA_|DOCKER_DEFAULT_PLATFORM)/.test(name)) delete env[name];
  }
  return JSON.parse(execFileSync("docker", args, { cwd: ROOT, encoding: "utf8", env, stdio: ["ignore", "pipe", "pipe"] }));
}

/** service → sorted secret sources it mounts (services without any are left out). */
function secretMap(model) {
  const map = {};
  for (const [name, def] of Object.entries(model.services)) {
    const sources = (def.secrets ?? []).map((s) => s.source).sort();
    if (sources.length > 0) map[name] = sources;
  }
  return map;
}

/** Every service mounts each secret at the default path /run/secrets/<name>. */
function assertDefaultTargets(model) {
  for (const [name, def] of Object.entries(model.services)) {
    for (const s of def.secrets ?? []) {
      assert.ok(s.target === undefined || s.target === `/run/secrets/${s.source}`,
        `${name} mounts ${s.source} at ${s.target} — the services read /run/secrets/${s.source}`);
    }
  }
}

function assertNoSecretInEnvironment(model) {
  for (const [name, def] of Object.entries(model.services)) {
    const leaked = Object.keys(def.environment ?? {}).filter((k) => FORBIDDEN_ENV.test(k));
    assert.deepEqual(leaked, [], `${name} receives a shared secret as an environment variable`);
  }
}

/** Mounted file names the backend reads, from SharedSecretFiles.cs itself. */
function backendSpecFiles() {
  const cs = readFileSync(join(ROOT, "backend/src/Skinora.API/Configuration/SharedSecretFiles.cs"), "utf8");
  return [...cs.matchAll(/new\("([A-Za-z:]+)", "([a-z_]+)", RequiredOutsideDevelopment:/g)].map((m) => m[2]).sort();
}

/** Default paths a sidecar reads, from its config/sharedSecrets.ts. */
function sidecarPaths(dir) {
  const ts = readFileSync(join(ROOT, dir, "src/config/sharedSecrets.ts"), "utf8");
  return [...ts.matchAll(/'(\/run\/secrets\/[a-z_]+)'/g)].map((m) => m[1]).sort();
}

describe("compose secret map (T171a)", { skip: !HAS_DOCKER && "docker compose not available" }, () => {
  before(() => {
    workDir = mkdtempSync(join(tmpdir(), "skinora-secrets-"));
    stubEnv = join(workDir, "stub.env");
    copyFileSync(join(ROOT, ".env.example"), stubEnv);
    appendFileSync(stubEnv, "\nGRAFANA_ADMIN_PASSWORD=secrets-test-stub\nSKINORA_TRIAL_STACK=1\n");
  });

  after(() => {
    if (workDir) rmSync(workDir, { recursive: true, force: true });
  });

  describe("docker-compose.yml (all profiles)", () => {
    let model;
    before(() => {
      model = render([BASE], { profiles: ["ethereum"] });
    });

    test("each service mounts exactly the secrets it uses — nothing more, nobody else", () => {
      assert.deepEqual(secretMap(model), EXPECTED);
    });

    test("no service receives a shared secret as an environment variable", () => {
      assertNoSecretInEnvironment(model);
    });

    test("every secret is mounted at /run/secrets/<name>, the path the services read", () => {
      assertDefaultTargets(model);
    });

    test("both ends of a shared secret mount the SAME top-level secret, one file under ./secrets", () => {
      for (const [secret, other] of PAIRS) {
        assert.ok(model.services["skinora-backend"].secrets.some((s) => s.source === secret), `backend lacks ${secret}`);
        assert.ok(model.services[other].secrets.some((s) => s.source === secret), `${other} lacks ${secret}`);
        assert.equal(resolve(model.secrets[secret].file), join(ROOT, "secrets", `${secret}.txt`));
      }
      assert.equal(resolve(model.secrets.grafana_smtp_password.file), join(ROOT, "secrets", "grafana_smtp_password.txt"));
    });

    test("the backend mounts every file SharedSecretFiles reads (only jwt_previous_secret is rotation-only)", () => {
      const mounted = model.services["skinora-backend"].secrets.map((s) => s.source).sort();
      assert.deepEqual(backendSpecFiles().filter((f) => f !== "jwt_previous_secret"), mounted);
    });

    test("each sidecar reads the paths its secrets are mounted at", () => {
      assert.deepEqual(sidecarPaths("sidecar-steam"), ["/run/secrets/steam_sidecar_internal_key"]);
      assert.deepEqual(sidecarPaths("sidecar-blockchain"), [
        "/run/secrets/blockchain_sidecar_internal_key",
        "/run/secrets/blockchain_webhook_secret",
      ]);
      assert.deepEqual(sidecarPaths("sidecar-ethereum"), [
        "/run/secrets/ethereum_sidecar_internal_key",
        "/run/secrets/ethereum_webhook_secret",
      ]);
    });

    test("Grafana reads its SMTP password from the secret file; the render step reads the bot token from its file", () => {
      const grafana = model.services["skinora-grafana"].environment;
      assert.equal(grafana.GF_SMTP_PASSWORD__FILE, "/run/secrets/grafana_smtp_password");
      const render = model.services["skinora-grafana-provisioning"].command.join(" ");
      assert.match(render, /\/run\/secrets\/telegram_bot_token/);
      assert.match(render, /TELEGRAM_BOT_TOKEN_UNREADABLE/);
    });
  });

  describe("trial overlay", () => {
    let model;
    before(() => {
      model = render([BASE, TRIAL], { profiles: ["ethereum", "tunnel"] });
    });

    test("same map plus the trial-only services", () => {
      assert.deepEqual(secretMap(model), { ...EXPECTED, ...EXPECTED_TRIAL_EXTRA });
      assertNoSecretInEnvironment(model);
      assertDefaultTargets(model);
    });

    test("every secret, the shared ones included, comes from secrets/trial/", () => {
      const trialDir = join(ROOT, "secrets", "trial") + sep;
      for (const [name, def] of Object.entries(model.secrets)) {
        assert.ok(resolve(def.file).startsWith(trialDir), `${name} must come from secrets/trial/, got ${def.file}`);
      }
      for (const secret of [...EXPECTED["skinora-backend"], "grafana_smtp_password", "heartbeat_url"]) {
        assert.ok(model.secrets[secret], `${secret} is not declared in the trial`);
      }
    });

    test("the heartbeat reads its ping URL from the secret file", () => {
      const hb = model.services["skinora-heartbeat"].environment;
      assert.equal(hb.HEARTBEAT_URL_FILE, "/run/secrets/heartbeat_url");
      assert.equal(hb.HEARTBEAT_URL, undefined);
    });
  });

  describe("e2e stack (Production backend)", () => {
    let model;
    before(() => {
      model = render([E2E], { envFile: null });
    });

    test("the backend runs as Production with its shared secrets as files, not variables", () => {
      const be = model.services["skinora-backend"];
      assert.equal(be.environment.ASPNETCORE_ENVIRONMENT, "Production");
      assertNoSecretInEnvironment({ services: { "skinora-backend": be } });
      const targets = Object.fromEntries((be.configs ?? []).map((c) => [c.target, c.source]));
      for (const file of [
        "jwt_secret",
        "blockchain_webhook_secret",
        "ethereum_webhook_secret",
        "steam_sidecar_internal_key",
        "blockchain_sidecar_internal_key",
        "ethereum_sidecar_internal_key",
      ]) {
        const source = targets[`/run/secrets/${file}`];
        assert.ok(source, `${file} is not mounted into the e2e backend`);
        assert.ok(model.configs[source].content.length >= 32, `${file} is shorter than the backend's 32-character floor`);
      }
    });

    test("the fixed test values match the harness and the fake sidecar", () => {
      const be = model.services["skinora-backend"];
      const content = (file) => model.configs[be.configs.find((c) => c.target === `/run/secrets/${file}`).source].content;
      const harness = readFileSync(join(ROOT, "e2e/src/config.ts"), "utf8");
      assert.ok(harness.includes(`'${content("jwt_secret")}'`), "e2e/src/config.ts jwtSecret differs from the backend's");
      const fake = model.services["skinora-fake-sidecar"].environment;
      assert.equal(fake.BLOCKCHAIN_WEBHOOK_SECRET, content("blockchain_webhook_secret"));
      assert.equal(fake.ETHEREUM_WEBHOOK_SECRET, content("ethereum_webhook_secret"));
      assert.notEqual(content("blockchain_webhook_secret"), content("ethereum_webhook_secret"));
    });
  });
});
