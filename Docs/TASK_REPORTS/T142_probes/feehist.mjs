// Collect eth_feeHistory: 30 days full coverage + 365 days sampled (one 1024-block window per ~day).
import { rpcR as rpc, now, hex, sleep, stats } from './rpc.mjs';
import { writeFileSync } from 'node:fs';
const url = process.argv[2] || 'https://ethereum-rpc.publicnode.com';
const mode = process.argv[3] || 'probe';
const out = process.argv[4] || 'fee.json';
const head = Number(BigInt((await rpc(url, 'eth_blockNumber', [])).result));
console.log(now(), 'endpoint', url, 'head', head, 'mode', mode);
async function fh(newest, count = 1024) {
  const r = await rpc(url, 'eth_feeHistory', [hex(count), hex(newest), [10, 50, 90]], { timeoutMs: 60000 });
  if (!r.result) return { err: JSON.stringify(r.error || r.raw).slice(0, 200) };
  const res = r.result;
  const oldest = Number(BigInt(res.oldestBlock));
  const base = res.baseFeePerGas.slice(0, -1).map((h) => Number(BigInt(h)) / 1e9); // gwei per block
  const rw = (res.reward || []).map((a) => a.map((h) => Number(BigInt(h)) / 1e9));
  return { oldest, base, rw };
}
if (mode === 'probe') {
  for (const back of [0, 10000, 100000, 1000000, 2700000]) {
    const r = await fh(head - back, 1024);
    console.log(now(), 'back', back, r.err ? 'ERR ' + r.err : `ok oldest=${r.oldest} n=${r.base.length} rewards=${r.rw.length} base0=${r.base[0]}`);
    await sleep(500);
  }
  process.exit(0);
}
const data = [];
if (mode === 'd30') {
  const N = 30 * 7200;
  for (let newest = head; newest > head - N; newest -= 1024) {
    const r = await fh(newest, 1024);
    if (r.err) { console.log('err', newest, r.err); await sleep(2000); continue; }
    data.push(r);
    if (data.length % 20 === 0) console.log(now(), 'chunks', data.length);
    await sleep(250);
  }
} else if (mode === 'd365') {
  // one window per ~day, newest offset shifted by 1013 blocks each day so time-of-day rotates
  for (let d = 0; d < 365; d++) {
    const newest = head - d * 7200 - ((d * 1013) % 7200);
    const r = await fh(newest, 1024);
    if (r.err) { console.log('err', newest, r.err); await sleep(2000); continue; }
    data.push(r);
    if (data.length % 30 === 0) console.log(now(), 'days', data.length);
    await sleep(250);
  }
} else if (mode === 'at') {
  const center = Number(process.argv[5]);
  for (let newest = center + 3072; newest > center - 3072; newest -= 1024) {
    const r = await fh(newest, 1024);
    if (r.err) { console.log('err', newest, r.err); continue; }
    data.push(r); await sleep(250);
  }
}
writeFileSync(out, JSON.stringify({ url, head, collectedAt: now(), mode, data }));
console.log(now(), 'saved', out, 'chunks', data.length, 'retries', stats.retries);
