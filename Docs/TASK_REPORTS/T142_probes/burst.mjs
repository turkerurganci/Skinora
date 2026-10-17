// Rate-limit behaviour: 60 sequential eth_blockNumber as fast as possible, then 30 in parallel.
import { rpc, now, sleep } from './rpc.mjs';
const eps = ['https://ethereum-rpc.publicnode.com', 'https://eth.drpc.org', 'https://mainnet.gateway.tenderly.co', 'https://ethereum-sepolia-rpc.publicnode.com', 'https://sepolia.gateway.tenderly.co'];
const ok = (r) => r.result !== undefined;
const why = (r) => (r.http + ' ' + JSON.stringify(r.error || r.raw || '')).slice(0, 90);
for (const url of eps) {
  const t0 = Date.now(); let good = 0; const errs = {};
  for (let i = 0; i < 60; i++) { const r = await rpc(url, 'eth_blockNumber', []); if (ok(r)) good++; else errs[why(r)] = (errs[why(r)] || 0) + 1; }
  const secs = (Date.now() - t0) / 1000;
  console.log(JSON.stringify({ t: now(), url, mode: 'sequential60', ok: good, seconds: secs.toFixed(1), rps: (60 / secs).toFixed(1), errs }));
  await sleep(3000);
  const rs = await Promise.all(Array.from({ length: 30 }, () => rpc(url, 'eth_blockNumber', [])));
  const e2 = {}; rs.filter((r) => !ok(r)).forEach((r) => (e2[why(r)] = (e2[why(r)] || 0) + 1));
  console.log(JSON.stringify({ t: now(), url, mode: 'parallel30', ok: rs.filter(ok).length, errs: e2 }));
  await sleep(3000);
}
