// Smoothed analysis: our own tx would bid tip = median of the last 20 blocks' median tips,
// maxFee = 2*base + tip (ethers default shape). Episodes above a cap, fuel dust included.
import { readFileSync } from 'node:fs';
import { pct } from './rpc.mjs';
const ETH = Number(process.argv[2] || 2364.48);
const G = { fuel: 21000, sweepUsed: 41285, sweepLimit: 46201, payoutUsed: 63209, payoutLimit: 64031 };
const usd = (gas, gwei) => gas * gwei * 1e-9 * ETH;
const maxOf = (a) => a.reduce((x, y) => (y > x ? y : x), -Infinity);
const f = (x) => (x < 1 ? x.toFixed(4) : x.toFixed(2));
for (const file of process.argv.slice(3)) {
  const j = JSON.parse(readFileSync(file, 'utf8'));
  const rows = [];
  for (const c of j.data) for (let i = 0; i < c.base.length; i++) rows.push({ n: c.oldest + i, base: c.base[i], t50: c.rw[i]?.[1] ?? 0 });
  rows.sort((a, b) => a.n - b.n);
  // trailing 20-block median tip (within contiguous segments)
  const win = [];
  let prev = -1;
  for (const r of rows) {
    if (r.n !== prev + 1) win.length = 0;
    win.push(r.t50); if (win.length > 20) win.shift();
    r.tip = pct(win, 50); r.eff = r.base + r.tip; r.maxFee = 2 * r.base + r.tip;
    prev = r.n;
  }
  const eff = rows.map((r) => r.eff), tip = rows.map((r) => r.tip);
  console.log(`\n=== ${file} (${j.url}, collected ${j.collectedAt}) blocks=${rows.length} range ${rows[0].n}..${rows[rows.length - 1].n}`);
  console.log(`smoothed tip gwei: p50=${f(pct(tip, 50))} p90=${f(pct(tip, 90))} p99=${f(pct(tip, 99))} max=${f(maxOf(tip))}`);
  console.log(`effective (base + smoothed tip) gwei: p50=${f(pct(eff, 50))} p90=${f(pct(eff, 90))} p99=${f(pct(eff, 99))} p99.9=${f(pct(eff, 99.9))} max=${f(maxOf(eff))}`);
  const last = rows[rows.length - 1];
  const sale = (r) => ({ used: usd(G.fuel + G.sweepUsed + G.payoutUsed, r.eff), withDust: usd(G.fuel, r.eff) + usd(G.sweepLimit, r.maxFee) + usd(G.payoutUsed, r.eff) });
  const sales = rows.map(sale);
  const su = sales.map((s) => s.used), sd = sales.map((s) => s.withDust);
  console.log(`per-sale cost $ (gas used only): p50=${pct(su, 50).toFixed(4)} p90=${pct(su, 90).toFixed(4)} p99=${pct(su, 99).toFixed(4)} p99.9=${pct(su, 99.9).toFixed(4)} max=${maxOf(su).toFixed(2)}`);
  console.log(`per-sale cost $ (fuel sent = sweepLimit x maxFee, dust lost): p50=${pct(sd, 50).toFixed(4)} p90=${pct(sd, 90).toFixed(4)} p99=${pct(sd, 99).toFixed(4)} p99.9=${pct(sd, 99.9).toFixed(4)} max=${maxOf(sd).toFixed(2)}`);
  console.log(`latest block ${last.n}: base ${f(last.base)} tip ${f(last.tip)} eff ${f(last.eff)} gwei -> per-sale ${sale(last).used.toFixed(4)} $ (with dust ${sale(last).withDust.toFixed(4)} $)`);
  for (const cap of [2, 3, 5]) {
    // episodes where eff > cap; count episodes, durations
    const eps = []; let cur = 0; prev = -1;
    for (const r of rows) {
      if (r.n !== prev + 1 && cur) { eps.push(cur); cur = 0; }
      if (r.eff > cap) cur++; else if (cur) { eps.push(cur); cur = 0; }
      prev = r.n;
    }
    if (cur) eps.push(cur);
    const long = eps.filter((e) => e >= 50); // >= 10 min
    const durH = long.map((e) => (e * 12) / 3600);
    const above = rows.filter((r) => r.eff > cap).length / rows.length;
    console.log(`cap ${cap} gwei (~${usd(G.fuel + G.sweepLimit, cap).toFixed(3)} $ sweep op, ~${usd(G.payoutLimit, cap).toFixed(3)} $ payout at ETH ${ETH}): above ${(above * 100).toFixed(2)}% of blocks; episodes>=10min: ${long.length}, duration h p50=${long.length ? pct(durH, 50).toFixed(2) : '-'} p90=${long.length ? pct(durH, 90).toFixed(2) : '-'} max=${long.length ? maxOf(durH).toFixed(2) : '-'}`);
  }
}
