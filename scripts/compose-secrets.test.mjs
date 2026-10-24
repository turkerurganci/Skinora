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
//   * T171b — the third-party credentials too (database / Redis / Grafana
//     admin passwords, Steam / TronGrid keys, Ethereum RPC URLs): each only
//     where it is used, no service gets one as a variable, no connection
//     string or redis:// URL carries a password, and no command line does.
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
    "mssql_sa_password",
    "redis_password",
    "resend_api_key",
    "resend_webhook_signing_secret",
    "steam_api_key",
    "steam_sidecar_internal_key",
    "telegram_bot_token",
    "telegram_webhook_secret_token",
  ],
  "skinora-steam-sidecar": ["redis_password", "steam_api_key", "steam_sidecar_internal_key"],
  "skinora-blockchain-sidecar": [
    "blockchain_sidecar_internal_key",
    "blockchain_webhook_secret",
    "tron_api_key",
    "tron_api_key_secondary",
    "tron_hd_mnemonic",
    "tron_hot_wallet_private_key",
  ],
  "skinora-ethereum-sidecar": [
    "ethereum_fuel_wallet_private_key",
    "ethereum_hd_mnemonic",
    "ethereum_hot_wallet_private_key",
    "ethereum_rpc_backup_url",
    "ethereum_rpc_primary_url",
    "ethereum_sidecar_internal_key",
    "ethereum_webhook_secret",
  ],
  // T171b — the database, its backup container and Redis.
  "skinora-db": ["mssql_sa_password"],
  "skinora-db-backup": ["mssql_sa_password"],
  "skinora-redis": ["redis_password"],
  "skinora-grafana-provisioning": ["telegram_bot_token"],
  "skinora-grafana": ["grafana_admin_password", "grafana_smtp_password"],
};

/** T171b — every service that reads a third-party credential, by secret. */
const CREDENTIAL_READERS = {
  mssql_sa_password: ["skinora-backend", "skinora-db", "skinora-db-backup"],
  redis_password: ["skinora-backend", "skinora-redis", "skinora-steam-sidecar"],
  grafana_admin_password: ["skinora-grafana"],
  steam_api_key: ["skinora-backend", "skinora-steam-sidecar"],
  tron_api_key: ["skinora-blockchain-sidecar"],
  tron_api_key_secondary: ["skinora-blockchain-sidecar"],
  ethereum_rpc_primary_url: ["skinora-ethereum-sidecar"],
  ethereum_rpc_backup_url: ["skinora-ethereum-sidecar"],
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
// .NET reads its configuration keys case-insensitively, with "__" or ":" as
// the separator, and also under the ASPNETCORE_ / DOTNET_ prefixes (T171a
// doğrulama S4-3: ASPNETCORE_Jwt__Secret reached the backend unseen by the
// earlier, exact-name pattern; the backend refuses it at run time too).
const DOTNET_SECRET_KEY =
  /^(?:(?:ASPNETCORE|DOTNET)_)?(?:Jwt(?:__|:)(?:Secret|PreviousSecret)|Webhook(?:__|:)(?:Blockchain|Ethereum)SharedSecret|(?:Steam|Blockchain|Ethereum)Sidecar(?:__|:)InternalKey|Resend(?:__|:)(?:ApiKey|WebhookSigningSecret)|Telegram(?:__|:)(?:BotToken|WebhookSecretToken))$/i;
const SIDECAR_SECRET_KEY = /^(INTERNAL_KEY|WEBHOOK_SECRET|TELEGRAM_BOT_TOKEN|GF_SMTP_PASSWORD|HEARTBEAT_URL)$/;
// T171b — the third-party credentials: the .NET keys whose WHOLE value is a
// secret, and the variables the images / sidecars / scripts would read.
// (Connection strings are allowed as variables — without a password, checked
// on their values below.)
const DOTNET_CREDENTIAL_KEY =
  /^(?:(?:ASPNETCORE|DOTNET)_)?(?:SteamOpenId(?:__|:)WebApiKey|Discord(?:__|:)(?:ClientSecret|BotToken))$/i;
const CREDENTIAL_VARIABLE =
  /^(MSSQL_SA_PASSWORD|SA_PASSWORD|REDIS_PASSWORD|GF_SECURITY_ADMIN_PASSWORD|STEAM_API_KEY|TRON_API_KEY|TRON_API_KEY_SECONDARY|ETHEREUM_RPC_PRIMARY_URL|ETHEREUM_RPC_BACKUP_URL|DB_PASSWORD)$/;
const FORBIDDEN_ENV = {
  test: (key) =>
    DOTNET_SECRET_KEY.test(key) || SIDECAR_SECRET_KEY.test(key) || DOTNET_CREDENTIAL_KEY.test(key) || CREDENTIAL_VARIABLE.test(key),
};

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

/** T171b — the file names ThirdPartyCredentialFiles.cs reads. */
function backendCredentialFiles() {
  const cs = readFileSync(join(ROOT, "backend/src/Skinora.API/Configuration/ThirdPartyCredentialFiles.cs"), "utf8");
  return [...cs.matchAll(/new\("([A-Za-z:]+)", "([a-z_]+)", CredentialKind\./g)].map((m) => m[2]).sort();
}

/** T171b — default paths a sidecar reads its provider credentials from (config/providerCredentials.ts). */
function sidecarCredentialPaths(dir) {
  const ts = readFileSync(join(ROOT, dir, "src/config/providerCredentials.ts"), "utf8");
  return [...ts.matchAll(/'(\/run\/secrets\/[a-z_]+)'/g)].map((m) => m[1]).sort();
}

/** Every value a service receives, plus its command and entrypoint, as one text per service. */
function exposedText(def) {
  return [
    ...Object.entries(def.environment ?? {}).map(([k, v]) => `${k}=${v ?? ""}`),
    ...[def.command ?? []].flat(),
    ...[def.entrypoint ?? []].flat(),
    ...[def.healthcheck?.test ?? []].flat(),
  ].join("\n");
}

/** No password in any connection string / redis URL / command line (T171b). */
function assertNoPasswordInValues(model) {
  for (const [name, def] of Object.entries(model.services)) {
    const text = exposedText(def);
    assert.doesNotMatch(text, /(?:^|;)\s*(?:Password|Pwd)\s*=/im, `${name}: a SQL connection string carries a password`);
    assert.doesNotMatch(text, /,\s*password\s*=/i, `${name}: a Redis connection string carries a password`);
    assert.doesNotMatch(text, /rediss?:\/\/[^@\s/]*:[^@\s/]+@/i, `${name}: a redis:// URL carries a password`);
    assert.doesNotMatch(text, /requirepass|redis-cli\b.*\s-a\s|sqlcmd\b.*\s-P\s/i, `${name}: a password on a command line`);
  }
}

/** Default paths a sidecar reads, from its config/sharedSecrets.ts. */
function sidecarPaths(dir) {
  const ts = readFileSync(join(ROOT, dir, "src/config/sharedSecrets.ts"), "utf8");
  return [...ts.matchAll(/'(\/run\/secrets\/[a-z_]+)'/g)].map((m) => m[1]).sort();
}

test("the forbidden-variable pattern also sees the third-party credential variables (T171b)", () => {
  for (const key of [
    "MSSQL_SA_PASSWORD", "SA_PASSWORD", "REDIS_PASSWORD", "GF_SECURITY_ADMIN_PASSWORD", "STEAM_API_KEY",
    "TRON_API_KEY", "TRON_API_KEY_SECONDARY", "ETHEREUM_RPC_PRIMARY_URL", "ETHEREUM_RPC_BACKUP_URL",
    "SteamOpenId__WebApiKey", "ASPNETCORE_SteamOpenId__WebApiKey", "Discord__ClientSecret", "discord:bottoken",
  ]) {
    assert.ok(FORBIDDEN_ENV.test(key), `${key} must count as a credential variable`);
  }
  for (const key of [
    "MSSQL_SA_PASSWORD_FILE", "GF_SECURITY_ADMIN_PASSWORD__FILE", "ETHEREUM_RPC_PRIMARY_NAME", "SteamOpenId__Realm",
    "Discord__Provider", "ConnectionStrings__DefaultConnection", "Redis__ConnectionString", "REDIS_URL",
  ]) {
    assert.ok(!FORBIDDEN_ENV.test(key), `${key} is not a credential variable`);
  }
});

test("the password-in-value check sees every shape a password could take (T171b)", () => {
  const leaks = [
    { environment: { ConnectionStrings__DefaultConnection: "Server=x;User Id=sa;Password=p;" } },
    { environment: { ConnectionStrings__DefaultConnection: "Server=x;pwd=p" } },
    { environment: { Redis__ConnectionString: "skinora-redis:6379,password=p" } },
    { environment: { REDIS_URL: "redis://:p@skinora-redis:6379" } },
    { command: ["redis-server", "--requirepass", "p"] },
    { healthcheck: { test: ["CMD", "redis-cli", "-a", "p", "ping"] } },
    { healthcheck: { test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P p -Q 'SELECT 1'"] } },
  ];
  for (const def of leaks) {
    assert.throws(() => assertNoPasswordInValues({ services: { x: def } }), undefined, JSON.stringify(def));
  }
  assertNoPasswordInValues({
    services: {
      x: {
        environment: {
          ConnectionStrings__DefaultConnection: "Server=skinora-db,1433;Database=Skinora;User Id=sa;TrustServerCertificate=True;",
          Redis__ConnectionString: "skinora-redis:6379",
          REDIS_URL: "redis://skinora-redis:6379",
          MSSQL_SA_PASSWORD_FILE: "/run/secrets/mssql_sa_password",
        },
      },
    },
  });
});

test("the forbidden-variable pattern sees every spelling .NET would read (T171a S4-3)", () => {
  for (const key of [
    "Jwt__Secret", "ASPNETCORE_Jwt__Secret", "DOTNET_Jwt__Secret", "JWT__SECRET", "Jwt:Secret",
    "ASPNETCORE_Telegram__BotToken", "aspnetcore_steamsidecar__internalkey", "Webhook__EthereumSharedSecret",
    "INTERNAL_KEY", "WEBHOOK_SECRET", "TELEGRAM_BOT_TOKEN", "GF_SMTP_PASSWORD", "HEARTBEAT_URL",
  ]) {
    assert.ok(FORBIDDEN_ENV.test(key), `${key} must count as a shared secret variable`);
  }
  for (const key of [
    "Jwt__Issuer", "ASPNETCORE_ENVIRONMENT", "GF_SMTP_PASSWORD__FILE", "Telegram__ChatId",
    "SteamSidecar__BaseUrl", "Resend__FromAddress", "Jwt__SecretX", "XJwt__Secret",
  ]) {
    assert.ok(!FORBIDDEN_ENV.test(key), `${key} is not a shared secret variable`);
  }
});

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
      // T171b — the backend's third-party credential files are pinned by their own test below.
      const credentials = new Set(backendCredentialFiles());
      const mounted = model.services["skinora-backend"].secrets
        .map((s) => s.source)
        .filter((f) => !credentials.has(f))
        .sort();
      assert.deepEqual(backendSpecFiles().filter((f) => f !== "jwt_previous_secret"), mounted);
    });

    test("every third-party credential reaches exactly the services that use it, from one file under ./secrets (T171b)", () => {
      for (const [secret, readers] of Object.entries(CREDENTIAL_READERS)) {
        const mounting = Object.entries(model.services)
          .filter(([, def]) => (def.secrets ?? []).some((s) => s.source === secret))
          .map(([name]) => name)
          .sort();
        assert.deepEqual(mounting, readers, `${secret} is mounted by the wrong services`);
        assert.equal(resolve(model.secrets[secret].file), join(ROOT, "secrets", `${secret}.txt`));
      }
    });

    test("no connection string, redis:// URL, command line or healthcheck carries a password (T171b)", () => {
      assertNoPasswordInValues(model);
      const be = model.services["skinora-backend"].environment;
      assert.match(be.ConnectionStrings__DefaultConnection, /^Server=skinora-db,1433;/);
      assert.equal(be.Redis__ConnectionString, "skinora-redis:6379");
      assert.equal(model.services["skinora-steam-sidecar"].environment.REDIS_URL, "redis://skinora-redis:6379");
    });

    test("SQL Server, its backup container, Redis and Grafana read their passwords from the files (T171b)", () => {
      const db = model.services["skinora-db"];
      assert.deepEqual(db.entrypoint, ["/bin/bash", "/opt/skinora/mssql-entrypoint.sh"]);
      assert.deepEqual(db.command, ["/opt/mssql/bin/sqlservr"]);
      assert.equal(db.environment.MSSQL_SA_PASSWORD_FILE, "/run/secrets/mssql_sa_password");
      assert.deepEqual(db.healthcheck.test, ["CMD", "/bin/bash", "/opt/skinora/mssql-entrypoint.sh", "--healthcheck"]);
      const dbScript = db.volumes.find((v) => v.target === "/opt/skinora/mssql-entrypoint.sh");
      assert.ok(dbScript?.read_only);
      assert.equal(resolve(dbScript.source), join(ROOT, "scripts", "db", "mssql-entrypoint.sh"));
      assert.equal(model.services["skinora-db-backup"].environment.MSSQL_SA_PASSWORD_FILE, "/run/secrets/mssql_sa_password");

      const redis = model.services["skinora-redis"];
      assert.deepEqual(redis.entrypoint, ["/bin/sh", "/opt/skinora/redis-entrypoint.sh"]);
      assert.deepEqual(redis.command, ["--appendonly", "yes"]);
      assert.deepEqual(redis.healthcheck.test, ["CMD", "/bin/sh", "/opt/skinora/redis-entrypoint.sh", "--healthcheck"]);
      assert.ok((redis.tmpfs ?? []).includes("/run/skinora-redis"), "the requirepass file lives on a tmpfs");
      const redisScript = redis.volumes.find((v) => v.target === "/opt/skinora/redis-entrypoint.sh");
      assert.ok(redisScript?.read_only);
      assert.equal(resolve(redisScript.source), join(ROOT, "scripts", "redis", "redis-entrypoint.sh"));

      const grafana = model.services["skinora-grafana"].environment;
      assert.equal(grafana.GF_SECURITY_ADMIN_PASSWORD__FILE, "/run/secrets/grafana_admin_password");
    });

    test("the backend mounts every credential file ThirdPartyCredentialFiles reads, except Discord's (channel off)", () => {
      const mounted = model.services["skinora-backend"].secrets.map((s) => s.source);
      const expected = backendCredentialFiles().filter((f) => !f.startsWith("discord_"));
      assert.deepEqual(expected, ["mssql_sa_password", "redis_password", "steam_api_key"]);
      for (const f of expected) assert.ok(mounted.includes(f), `backend lacks ${f}`);
      for (const f of backendCredentialFiles().filter((f) => f.startsWith("discord_"))) {
        assert.ok(!mounted.includes(f), `${f} is mounted although the Discord channel is off`);
      }
    });

    test("each sidecar reads its provider credentials at the paths they are mounted at (T171b)", () => {
      assert.deepEqual(sidecarCredentialPaths("sidecar-steam"), ["/run/secrets/redis_password", "/run/secrets/steam_api_key"]);
      assert.deepEqual(sidecarCredentialPaths("sidecar-blockchain"), [
        "/run/secrets/tron_api_key",
        "/run/secrets/tron_api_key_secondary",
      ]);
      assert.deepEqual(sidecarCredentialPaths("sidecar-ethereum"), [
        "/run/secrets/ethereum_rpc_backup_url",
        "/run/secrets/ethereum_rpc_primary_url",
      ]);
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
      for (const secret of [...EXPECTED["skinora-backend"], "grafana_smtp_password", "heartbeat_url", ...Object.keys(CREDENTIAL_READERS)]) {
        assert.ok(model.secrets[secret], `${secret} is not declared in the trial`);
      }
    });

    test("no connection string, URL or command line carries a password; Grafana has no admin variable (T171b)", () => {
      assertNoPasswordInValues(model);
      assert.equal(model.services["skinora-grafana"].environment.GF_SECURITY_ADMIN_PASSWORD, undefined);
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

    test("the database and Redis run the real entrypoint scripts with their passwords as files (T171b)", () => {
      assertNoPasswordInValues({ services: { db: model.services["skinora-db"], redis: model.services["skinora-redis"], be: model.services["skinora-backend"] } });
      const target = (svc, path) => {
        const c = (model.services[svc].configs ?? []).find((x) => x.target === path);
        assert.ok(c, `${svc} lacks ${path}`);
        return model.configs[c.source].content;
      };
      // The backend reads the very values the two containers read.
      assert.equal(target("skinora-backend", "/run/secrets/mssql_sa_password"), target("skinora-db", "/run/secrets/mssql_sa_password"));
      assert.equal(target("skinora-backend", "/run/secrets/redis_password"), target("skinora-redis", "/run/secrets/redis_password"));
      // ... and the fake sidecar (a test double, environment) the same DB password.
      assert.equal(model.services["skinora-fake-sidecar"].environment.DB_PASSWORD, target("skinora-db", "/run/secrets/mssql_sa_password"));
      assert.deepEqual(model.services["skinora-db"].entrypoint, ["/bin/bash", "/opt/skinora/mssql-entrypoint.sh"]);
      assert.deepEqual(model.services["skinora-redis"].entrypoint, ["/bin/sh", "/opt/skinora/redis-entrypoint.sh"]);
      for (const svc of ["skinora-db", "skinora-redis", "skinora-backend"]) {
        for (const k of Object.keys(model.services[svc].environment ?? {})) {
          assert.ok(!FORBIDDEN_ENV.test(k), `${svc} receives ${k}`);
        }
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
