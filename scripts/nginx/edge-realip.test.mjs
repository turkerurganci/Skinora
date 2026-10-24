// T162 — the tunnel edge's trust in Cloudflare's headers, run on the real
// nginx image with the repo's nginx/nginx.conf. Cloudflare writes
// CF-Connecting-IP (visitor IP), X-Forwarded-Proto and CF-IPCountry; nginx
// may believe them ONLY when its TCP peer is the tunnel connector
// (skinora-cloudflared, fixed address in docker-compose.trial.yml). Any other
// peer — another container, or a request to the loopback operator port, which
// Docker Desktop delivers from the network gateway — sends the same headers
// and must get nothing for them: its own socket address as the client IP, the
// edge's own scheme, no country.
//
// The connector address is read from nginx.conf and swapped for one in a
// private test subnet (the trial's 172.30.161.0/24 may be in use on the
// machine running this). The swap is checked: exactly one address, used by
// both set_real_ip_from and the geo map. scripts/compose-exposure.test.mjs
// pins the same address to the compose file.
//
// Needs the docker CLI and daemon. Skipped locally without them; FAILS in CI
// (process.env.CI) — a gate that skips is not a gate.
import { test, describe, before, after } from "node:test";
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { request } from "node:http";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const NGINX_CONF = join(ROOT, "nginx", "nginx.conf");
const NGINX_IMAGE = "nginx:alpine";
// The heartbeat's pinned curl image (docker-compose.trial.yml) — already
// pulled wherever the trial runs.
const CURL_IMAGE = "curlimages/curl:8.21.0";

/** The connector address nginx.conf trusts (set_real_ip_from + geo /32). */
function trustedEdgeAddresses(conf) {
  const realIp = [...conf.matchAll(/^\s*set_real_ip_from\s+([^;\s]+)\s*;/gm)].map((m) => m[1]);
  const geoBlock = conf.match(/geo\s+\$realip_remote_addr\s+\$skinora_trusted_edge\s*\{([^}]*)\}/);
  const geoTrusted = geoBlock
    ? [...geoBlock[1].matchAll(/^\s*([0-9.]+(?:\/\d+)?)\s+1\s*;/gm)].map((m) => m[1])
    : [];
  return { realIp, geoTrusted, geoKeyedOnSocket: Boolean(geoBlock) };
}

const CONF_TEXT = readFileSync(NGINX_CONF, "utf8");

describe("nginx.conf trusts exactly one tunnel connector address (T162)", () => {
  test("set_real_ip_from and the geo map name the same single /32, keyed on the socket address", () => {
    const { realIp, geoTrusted, geoKeyedOnSocket } = trustedEdgeAddresses(CONF_TEXT);
    assert.ok(geoKeyedOnSocket, "geo must read $realip_remote_addr — after realip, $remote_addr is the visitor");
    assert.equal(realIp.length, 1, `exactly one set_real_ip_from, got ${JSON.stringify(realIp)}`);
    assert.match(realIp[0], /^\d+\.\d+\.\d+\.\d+$/, "a single address, not a network");
    assert.deepEqual(geoTrusted, [`${realIp[0]}/32`]);
    assert.match(CONF_TEXT, /^\s*real_ip_header\s+CF-Connecting-IP\s*;/m);
    assert.doesNotMatch(CONF_TEXT, /X-Forwarded-Proto\s+\$scheme/, "the backend's scheme comes from $skinora_forwarded_proto");
  });
});

const HAS_DOCKER = spawnSync("docker", ["info"], { encoding: "utf8" }).status === 0;
if (!HAS_DOCKER && process.env.CI) {
  throw new Error("edge-realip test: docker is not available in CI — the tunnel trust gate cannot run.");
}

const id = `t162edge${process.pid}`;
const network = `${id}-net`;
const octet = 20 + (process.pid % 200);
const SUBNET = `10.231.${octet}.0/24`;
const IP_RANGE = `10.231.${octet}.0/25`;
const CONNECTOR = `10.231.${octet}.250`;
const STRANGER = `10.231.${octet}.200`;
const VISITOR = "203.0.113.7";
let work;
let port;

const docker = (...args) => execFileSync("docker", args, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();

/** Upstream that answers with the forwarded headers it received. */
function echoUpstream(name, listen) {
  const conf = join(work, `${name}.conf`);
  writeFileSync(
    conf,
    `events {}\nhttp { server { listen ${listen}; location / { return 200 ` +
      `"${name}|rip=$http_x_real_ip|xff=$http_x_forwarded_for|proto=$http_x_forwarded_proto|cc=$http_x_country_code"; } } }\n`,
  );
  docker(
    "run", "-d", "--name", `${id}-${name}`, "--network", network, "--network-alias", `skinora-${name}`,
    "-v", `${conf}:/etc/nginx/nginx.conf:ro`, NGINX_IMAGE,
  );
}

/** Cloudflare's headers, as a client would forge them (or Cloudflare would write them). */
const CF_HEADERS = (proto) => [
  "-H", `CF-Connecting-IP: ${VISITOR}`,
  "-H", `X-Forwarded-For: ${VISITOR}`,
  "-H", `X-Forwarded-Proto: ${proto}`,
  "-H", "CF-IPCountry: TR",
  "-H", "Host: getskinora.com",
];

/** curl from a container with a fixed address on the test network. */
function curlFrom(ip, path, proto) {
  const out = docker(
    "run", "--rm", "--network", network, "--ip", ip, CURL_IMAGE,
    "-s", "-o", "-", "-w", "\n%{http_code} %{redirect_url}", ...CF_HEADERS(proto), `http://skinora-reverse-proxy${path}`,
  );
  const lines = out.split("\n");
  const [status, location = ""] = lines.pop().split(" ");
  return { status: Number(status), location, body: lines.join("\n") };
}

/** A request to the published port (Docker delivers it from the gateway). */
function fromHostPort(path, proto) {
  const headers = {};
  for (let i = 0; i < CF_HEADERS(proto).length; i += 2) {
    const [k, ...v] = CF_HEADERS(proto)[i + 1].split(": ");
    headers[k] = v.join(": ");
  }
  return new Promise((resolvePromise, reject) => {
    const req = request({ host: "127.0.0.1", port, path, method: "GET", headers }, (res) => {
      let body = "";
      res.on("data", (d) => (body += d));
      res.on("end", () => resolvePromise({ status: res.statusCode, body, location: res.headers.location ?? "" }));
    });
    req.on("error", reject);
    req.end();
  });
}

function parse(body) {
  return Object.fromEntries(body.split("|").slice(1).map((kv) => [kv.slice(0, kv.indexOf("=")), kv.slice(kv.indexOf("=") + 1)]));
}

async function waitForEdge() {
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fromHostPort("/nginx-health", "http");
      if (r.status === 200) return;
    } catch {
      // not up yet
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`edge did not come up:\n${docker("logs", `${id}-edge`)}`);
}

describe("nginx edge behind the Cloudflare tunnel (T162)", { skip: !HAS_DOCKER && "docker not available" }, () => {
  before(async () => {
    work = mkdtempSync(join(tmpdir(), "edge-realip-"));
    const [trusted] = trustedEdgeAddresses(CONF_TEXT).realIp;
    const conf = join(work, "nginx.conf");
    writeFileSync(conf, CONF_TEXT.split(trusted).join(CONNECTOR));
    assert.deepEqual(trustedEdgeAddresses(readFileSync(conf, "utf8")).realIp, [CONNECTOR]);
    docker("network", "create", "--subnet", SUBNET, "--ip-range", IP_RANGE, network);
    echoUpstream("backend", 5000);
    echoUpstream("frontend", 3000);
    docker(
      "run", "-d", "--name", `${id}-edge`, "--network", network, "--network-alias", "skinora-reverse-proxy",
      "-p", "127.0.0.1::80", "-v", `${conf}:/etc/nginx/nginx.conf:ro`, NGINX_IMAGE,
    );
    port = Number(docker("port", `${id}-edge`, "80/tcp").split("\n")[0].split(":").pop());
    await waitForEdge();
  });

  after(() => {
    for (const c of ["edge", "backend", "frontend"]) spawnSync("docker", ["rm", "-f", `${id}-${c}`]);
    spawnSync("docker", ["network", "rm", network]);
    if (work) rmSync(work, { recursive: true, force: true });
  });

  for (const path of ["/api/v1/auth/steam", "/hubs/notifications"]) {
    test(`connector: ${path} reaches the backend as the visitor, over https, with the country`, () => {
      const r = curlFrom(CONNECTOR, path, "https");
      assert.equal(r.status, 200, r.body);
      const h = parse(r.body);
      assert.equal(h.rip, VISITOR);
      assert.equal(h.xff.split(/,\s*/).pop(), VISITOR, "ForwardLimit=1 reads the last hop: it must be the visitor, not the connector");
      assert.ok(!h.xff.includes(CONNECTOR), `the connector must not appear in X-Forwarded-For: ${h.xff}`);
      assert.equal(h.proto, "https");
      assert.equal(h.cc, "TR");
    });
  }

  test("connector: the frontend sees the visitor and https too", () => {
    const h = parse(curlFrom(CONNECTOR, "/", "https").body);
    assert.equal(h.rip, VISITOR);
    assert.equal(h.proto, "https");
  });

  test("connector: plain http through Cloudflare is redirected to https on the same host and path", () => {
    const r = curlFrom(CONNECTOR, "/api/v1/auth/steam?returnUrl=%2F", "http");
    assert.equal(r.status, 301);
    assert.equal(r.location, "https://getskinora.com/api/v1/auth/steam?returnUrl=%2F");
  });

  // Every location that forwards these headers must ignore them from a stranger:
  // /api/ (backend), /hubs/ (backend, WebSocket) and / (frontend).
  for (const path of ["/api/v1/auth/steam", "/hubs/notifications", "/"]) {
    test(`another container sending the same headers to ${path} gets its own address, http and no country`, () => {
      const r = curlFrom(STRANGER, path, "https");
      assert.equal(r.status, 200, r.body);
      const h = parse(r.body);
      assert.equal(h.rip, STRANGER);
      assert.equal(h.xff.split(/,\s*/).pop(), STRANGER);
      assert.equal(h.proto, "http");
      assert.equal(h.cc, "");
    });

    test(`another container's forged X-Forwarded-Proto: http to ${path} is not redirected (only the connector's is believed)`, () => {
      assert.equal(curlFrom(STRANGER, path, "http").status, 200);
    });

    test(`the loopback operator port (Docker gateway) cannot claim the visitor's IP, https or a country on ${path}`, async () => {
      const r = await fromHostPort(path, "https");
      assert.equal(r.status, 200, r.body);
      const h = parse(r.body);
      assert.notEqual(h.rip, VISITOR);
      assert.equal(h.xff.split(/,\s*/).pop(), h.rip);
      assert.equal(h.proto, "http");
      assert.equal(h.cc, "");
    });
  }
});
