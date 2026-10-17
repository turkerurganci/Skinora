import { rpcR as rpc, now, hex, pad32, USDT, sleep, pct } from './rpc.mjs';
import { readFileSync } from 'node:fs';
const url = 'https://eth.drpc.org';
const { out } = JSON.parse(readFileSync('receipts.json', 'utf8'));
const by = {};
for (const o of out) (by[o.cls] ||= []).push(o);
console.log(now(), 'estimateGas at block-1 for real transfers (same from/to/value)');
for (const [cls, arr] of Object.entries(by)) {
  const res = [];
  for (const o of arr.slice(0, 12)) {
    const data = '0xa9059cbb' + pad32(o.to).slice(2) + BigInt(o.value).toString(16).padStart(64, '0');
    const r = await rpc(url, 'eth_estimateGas', [{ from: o.from, to: USDT, data }, hex(o.b - 1)]);
    if (r.result) res.push({ est: Number(BigInt(r.result)), used: o.gasUsed, lim: o.gasLimit });
    await sleep(200);
  }
  const e = res.map((x) => x.est), u = res.map((x) => x.used), l = res.map((x) => x.lim);
  console.log(`${cls}: n=${res.length} estimate min=${Math.min(...e)} p50=${pct(e, 50)} max=${Math.max(...e)} | gasUsed p50=${pct(u, 50)} | sender's own gasLimit p50=${pct(l, 50)} max=${Math.max(...l)}`);
}
