// T165 düzeltme turu (doğrulama bulgu 7) — the edge's public /health, run on
// the real nginx image with the repo's nginx/nginx.conf and two fake upstreams
// that echo the URI they received. The backend's detailed /health (check
// names, exception text) must never be reachable from outside: the exact
// /health is answered by the minimal /api/health; everything else that only
// looks like it falls through to the frontend. Raw request paths (no client
// normalisation) so nginx's own normalisation is what is tested.
//
// Needs the docker CLI and daemon. Skipped locally without them; FAILS in CI
// (process.env.CI) — a gate that skips is not a gate.
import { test, describe, before, after } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { request } from "node:http";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const NGINX_CONF = join(ROOT, "nginx", "nginx.conf");
// The edge's own image (docker-compose.yml skinora-reverse-proxy).
const NGINX_IMAGE = "nginx:alpine";

const HAS_DOCKER = spawnSync("docker", ["info"], { encoding: "utf8" }).status === 0;
if (!HAS_DOCKER && process.env.CI) {
  throw new Error("edge-health test: docker is not available in CI — the /health gate cannot run.");
}

const id = `t165edge${process.pid}`;
const network = `${id}-net`;
let work;
let port;

const docker = (...args) => execFileSync("docker", args, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();

function fakeUpstream(name, listen) {
  const conf = join(work, `${name}.conf`);
  writeFileSync(
    conf,
    `events {}\nhttp { server { listen ${listen}; location / { return 200 "${name} $request_uri"; } } }\n`,
  );
  docker(
    "run", "-d", "--name", `${id}-${name}`, "--network", network, "--network-alias", `skinora-${name}`,
    "-v", `${conf}:/etc/nginx/nginx.conf:ro`, NGINX_IMAGE,
  );
}

function get(path) {
  return new Promise((resolvePromise, reject) => {
    const req = request({ host: "127.0.0.1", port, path, method: "GET" }, (res) => {
      let body = "";
      res.on("data", (d) => (body += d));
      res.on("end", () => resolvePromise({ status: res.statusCode, body }));
    });
    req.on("error", reject);
    req.end();
  });
}

async function waitForEdge() {
  for (let i = 0; i < 60; i++) {
    try {
      const r = await get("/nginx-health");
      if (r.status === 200) return;
    } catch {
      // not up yet
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`edge did not come up:\n${docker("logs", `${id}-edge`)}`);
}

describe("nginx edge /health (T165)", { skip: !HAS_DOCKER && "docker not available" }, () => {
  before(async () => {
    work = mkdtempSync(join(tmpdir(), "edge-health-"));
    docker("network", "create", network);
    fakeUpstream("backend", 5000);
    fakeUpstream("frontend", 3000);
    docker(
      "run", "-d", "--name", `${id}-edge`, "--network", network, "-p", "127.0.0.1::80",
      "-v", `${NGINX_CONF}:/etc/nginx/nginx.conf:ro`, NGINX_IMAGE,
    );
    port = Number(docker("port", `${id}-edge`, "80/tcp").split("\n")[0].split(":").pop());
    await waitForEdge();
  });

  after(() => {
    for (const c of ["edge", "backend", "frontend"]) spawnSync("docker", ["rm", "-f", `${id}-${c}`]);
    spawnSync("docker", ["network", "rm", network]);
    if (work) rmSync(work, { recursive: true, force: true });
  });

  for (const path of ["/health", "//health", "/./health", "/api/../health", "/api/%2e%2e/health", "/hubs/../health"]) {
    test(`${path} is answered by the backend's minimal /api/health`, async () => {
      const r = await get(path);
      assert.equal(r.status, 200);
      assert.equal(r.body, "backend /api/health");
    });
  }

  for (const path of ["/health/", "/health/x", "/HEALTH", "/healthz"]) {
    test(`${path} does not reach the backend's /health (frontend)`, async () => {
      const r = await get(path);
      assert.equal(r.status, 200);
      assert.match(r.body, /^frontend /);
    });
  }

  test("the API path still reaches the backend unchanged", async () => {
    const r = await get("/api/health");
    assert.equal(r.body, "backend /api/health");
  });
});
