import { readFileSync } from 'node:fs';
import { pct } from './rpc.mjs';
const ETH = Number(process.argv[2] || 2364.48);
const G = { fuel: 21000, sweepUsed: 41285, sweepLimit: 46201, payoutNew: 63209, payoutExisting: 46097 };
const SALE_USED = G.fuel + G.sweepUsed + G.payoutNew; // gas actually burned per sale (no dust)
const usd = (gas, gwei) => gas * gwei * 1e-9 * ETH;
function load(f) {
  const j = JSON.parse(readFileSync(f, 'utf8'));
  const rows = [];
  for (const c of j.data) for (let i = 0; i < c.base.length; i++) rows.push({ n: c.oldest + i, base: c.base[i], t10: c.rw[i]?.[0] ?? 0, t50: c.rw[i]?.[1] ?? 0, t90: c.rw[i]?.[2] ?? 0 });
  rows.sort((a, b) => a.n - b.n);
  return { j, rows };
}
const f = (x) => (x < 1 ? x.toFixed(4) : x.toFixed(2));
function runs(rows, thr, key = 'eff') {
  // longest consecutive run (in blocks) with value > thr, only counting contiguous block numbers
  let best = 0, cur = 0, prev = -1, bestEnd = 0, count = 0;
  for (const r of rows) {
    const v = key === 'eff' ? r.base + r.t50 : r[key];
    if (r.n !== prev + 1) cur = 0;
    if (v > thr) { cur++; count++; if (cur > best) { best = cur; bestEnd = r.n; } } else cur = 0;
    prev = r.n;
  }
  return { best, bestEnd, frac: count / rows.length };
}
for (const file of process.argv.slice(3)) {
  const { j, rows } = load(file);
  const base = rows.map((r) => r.base), t50 = rows.map((r) => r.t50), eff = rows.map((r) => r.base + r.t50);
  console.log(`\n=== ${file} | collected ${j.collectedAt} via ${j.url} | blocks ${rows.length} (${rows[0].n}..${rows[rows.length - 1].n})`);
  for (const [nm, arr] of [['baseFee', base], ['tip p50 (per-block median)', t50], ['effective = base + tip50', eff]]) {
    console.log(`${nm} gwei: p50=${f(pct(arr, 50))} p90=${f(pct(arr, 90))} p99=${f(pct(arr, 99))} p99.9=${f(pct(arr, 99.9))} max=${f(arr.reduce((a, b) => (b > a ? b : a), -Infinity))} mean=${f(arr.reduce((a, b) => a + b, 0) / arr.length)}`);
  }
  for (const p of [50, 90, 99, 99.9]) {
    const g = pct(eff, p);
    console.log(`  per-sale @eff p${p} ${f(g)} gwei: used-gas cost ${usd(SALE_USED, g).toFixed(4)} $ (fuel ${usd(G.fuel, g).toFixed(4)} + sweep ${usd(G.sweepUsed, g).toFixed(4)} + payout ${usd(G.payoutNew, g).toFixed(4)})`);
  }
  const mx = eff.reduce((a, b) => (b > a ? b : a), -Infinity);
  console.log(`  per-sale @eff max ${f(mx)} gwei: ${usd(SALE_USED, mx).toFixed(2)} $`);
  for (const thr of [2, 3, 5, 10, 20, 50]) {
    const r = runs(rows, thr);
    console.log(`  eff > ${thr} gwei: ${(r.frac * 100).toFixed(3)}% of blocks; longest contiguous run ${r.best} blocks (~${(r.best * 12 / 3600).toFixed(2)} h) ending ${r.bestEnd}`);
  }
}
console.log(`\nETH ${ETH} | gas per sale (used) ${SALE_USED} | 1 gwei => ${usd(SALE_USED, 1).toFixed(4)} $ per sale`);
