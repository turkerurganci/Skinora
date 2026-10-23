// T165 — scripts/heartbeat/heartbeat.sh against a local HTTP server that plays
// both the site (health / front page) and the outside monitor (ping / fail).
// The script runs under `sh` with the runner's curl — the same HTTP path the
// container (curlimages/curl) uses.
import { test, describe, before, after } from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { spawn, spawnSync } from "node:child_process";
import { resolve, dirname, join } from "node:path";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";

const SCRIPT = resolve(dirname(fileURLToPath(import.meta.url)), "heartbeat.sh");
const HAS_TOOLS =
  spawnSync("sh", ["-c", "command -v curl"], { encoding: "utf8" }).status === 0;
if (!HAS_TOOLS && process.env.CI) {
  throw new Error("heartbeat test: sh + curl are required in CI");
}

let server;
let base;
const state = { healthStatus: 200, frontStatus: 200, hits: [] };

function run(env) {
  return new Promise((resolvePromise) => {
    const child = spawn("sh", [SCRIPT], {
      env: { PATH: process.env.PATH, ...env },
      stdio: ["ignore", "pipe", "pipe"],
    });
    let out = "";
    child.stdout.on("data", (d) => (out += d));
    child.stderr.on("data", (d) => (out += d));
    child.on("close", (code) => resolvePromise({ code, out }));
  });
}

function reset({ health = 200, front = 200 } = {}) {
  state.healthStatus = health;
  state.frontStatus = front;
  state.hits = [];
}

const pings = () => state.hits.filter((h) => h.startsWith("/ping/"));

describe("heartbeat.sh (T165)", { skip: !HAS_TOOLS && "sh/curl not available" }, () => {
  before(async () => {
    server = createServer((req, res) => {
      state.hits.push(req.url);
      if (req.url === "/api/health") {
        res.writeHead(state.healthStatus).end('{"status":"x"}');
      } else if (req.url === "/") {
        // The real front page redirects to the locale; the check must follow.
        res.writeHead(307, { location: "/tr" }).end();
      } else if (req.url === "/tr") {
        res.writeHead(state.frontStatus).end("ok");
      } else if (req.url.startsWith("/ping/")) {
        res.writeHead(200).end("OK");
      } else {
        res.writeHead(404).end();
      }
    });
    await new Promise((r) => server.listen(0, "127.0.0.1", r));
    base = `http://127.0.0.1:${server.address().port}`;
  });

  after(() => server?.close());

  const envFor = (extra = {}) => ({
    HEARTBEAT_URL: `${base}/ping/9a1e6c2e-secret-uuid`,
    HEARTBEAT_CHECK_URLS: `${base}/api/health ${base}/`,
    HEARTBEAT_INTERVAL_SECONDS: "0",
    HEARTBEAT_TIMEOUT_SECONDS: "5",
    ...extra,
  });

  test("healthy site: one ping per round, after both checks", async () => {
    reset();
    const { code, out } = await run(envFor({ HEARTBEAT_ROUNDS: "2" }));
    assert.equal(code, 0, out);
    assert.deepEqual(pings(), ["/ping/9a1e6c2e-secret-uuid", "/ping/9a1e6c2e-secret-uuid"]);
    assert.deepEqual(state.hits.slice(0, 4), ["/api/health", "/", "/tr", "/ping/9a1e6c2e-secret-uuid"]);
  });

  test("the ping URL is never printed", async () => {
    reset({ health: 503 });
    const { out } = await run(envFor({ HEARTBEAT_ROUNDS: "3", HEARTBEAT_FAILURES_BEFORE_FAIL: "2" }));
    assert.doesNotMatch(out, /secret-uuid/);
  });

  test("unhealthy backend: no ping, and exactly one /fail after N rounds in a row", async () => {
    reset({ health: 503 });
    const { code, out } = await run(envFor({ HEARTBEAT_ROUNDS: "5", HEARTBEAT_FAILURES_BEFORE_FAIL: "3" }));
    assert.equal(code, 0, out);
    assert.deepEqual(pings(), ["/ping/9a1e6c2e-secret-uuid/fail"]);
    assert.match(out, /CHECK_FAILED .*\/api\/health .*\(3 in a row\)/);
    assert.match(out, /fail signal sent/);
  });

  test("broken front page counts too (a 2xx health is not enough)", async () => {
    reset({ front: 502 });
    await run(envFor({ HEARTBEAT_ROUNDS: "1", HEARTBEAT_FAILURES_BEFORE_FAIL: "1" }));
    assert.deepEqual(pings(), ["/ping/9a1e6c2e-secret-uuid/fail"]);
  });

  test("site down (connection refused) is a failed check, not a crash", async () => {
    reset();
    const { code } = await run(envFor({
      HEARTBEAT_ROUNDS: "2",
      HEARTBEAT_FAILURES_BEFORE_FAIL: "2",
      HEARTBEAT_CHECK_URLS: "http://127.0.0.1:9/api/health",
    }));
    assert.equal(code, 0);
    assert.deepEqual(pings(), ["/ping/9a1e6c2e-secret-uuid/fail"]);
  });

  test("without HEARTBEAT_URL it says so and pings nothing", async () => {
    reset();
    const { code, out } = await run(envFor({ HEARTBEAT_URL: "", HEARTBEAT_ROUNDS: "1" }));
    assert.equal(code, 0);
    assert.match(out, /HEARTBEAT_DISABLED/);
    assert.deepEqual(state.hits, []);
  });

  test("a non-numeric setting stops it with a clear error", async () => {
    reset();
    const { code, out } = await run(envFor({ HEARTBEAT_INTERVAL_SECONDS: "1m", HEARTBEAT_ROUNDS: "1" }));
    assert.equal(code, 2);
    assert.match(out, /HEARTBEAT_CONFIG_INVALID/);
  });

  // T171a — in the trial the ping URL is the Docker secret heartbeat_url.
  describe("ping URL from a secret file (T171a)", () => {
    let dir;
    before(() => {
      dir = mkdtempSync(join(tmpdir(), "heartbeat-secret-"));
    });
    after(() => rmSync(dir, { recursive: true, force: true }));

    test("reads the URL from HEARTBEAT_URL_FILE and never prints it", async () => {
      reset();
      const file = join(dir, "heartbeat_url");
      writeFileSync(file, `${base}/ping/file-secret-uuid\n`);
      const { code, out } = await run(envFor({ HEARTBEAT_URL: "", HEARTBEAT_URL_FILE: file, HEARTBEAT_ROUNDS: "1" }));
      assert.equal(code, 0, out);
      assert.deepEqual(pings(), ["/ping/file-secret-uuid"]);
      assert.doesNotMatch(out, /file-secret-uuid/);
    });

    test("an empty or missing file idles like an empty HEARTBEAT_URL", async () => {
      reset();
      const empty = join(dir, "empty");
      writeFileSync(empty, "");
      for (const file of [empty, join(dir, "missing")]) {
        const { code, out } = await run(envFor({ HEARTBEAT_URL: "", HEARTBEAT_URL_FILE: file, HEARTBEAT_ROUNDS: "1" }));
        assert.equal(code, 0, out);
        assert.match(out, /HEARTBEAT_DISABLED/);
      }
      assert.deepEqual(state.hits, []);
    });

    test("a directory at the mount point stops it (Docker's stand-in for a missing source)", async () => {
      reset();
      const directory = join(dir, "made-by-docker");
      mkdirSync(directory);
      const { code, out } = await run(envFor({ HEARTBEAT_URL: "", HEARTBEAT_URL_FILE: directory, HEARTBEAT_ROUNDS: "1" }));
      assert.equal(code, 2);
      assert.match(out, /HEARTBEAT_URL_UNREADABLE/);
      assert.deepEqual(state.hits, []);
    });

    test("the URL in both places is refused", async () => {
      reset();
      const file = join(dir, "both");
      writeFileSync(file, `${base}/ping/file-secret-uuid`);
      const { code, out } = await run(envFor({ HEARTBEAT_URL_FILE: file, HEARTBEAT_ROUNDS: "1" }));
      assert.equal(code, 2);
      assert.match(out, /HEARTBEAT_URL_AMBIGUOUS/);
      assert.doesNotMatch(out, /secret-uuid/);
    });
  });
});
