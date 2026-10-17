// Gas units of plain USDT transfers from real receipts, classified by recipient balance before
// and sender balance after. Also native ETH transfers to EOAs.
import { rpcR as rpc, now, hex, pad32, USDT, sleep, pct, stats } from './rpc.mjs';
import { writeFileSync } from 'node:fs';
const url = process.argv[2] || 'https://eth.drpc.org';
const nBlocks = Number(process.argv[3] || 30);
const maxCand = Number(process.argv[4] || 400);
const fin = Number(BigInt((await rpc(url, 'eth_getBlockByNumber', ['finalized', false])).result.number));
console.log(now(), 'endpoint', url, 'finalized', fin, 'blocks', fin - nBlocks + 1, '..', fin);
const bal = async (who, block) => { const r = await rpc(url, 'eth_call', [{ to: USDT, data: '0x70a08231' + pad32(who).slice(2) }, hex(block)]); if (!r.result) throw new Error(JSON.stringify(r.error || r.raw)); return BigInt(r.result); };
const cands = [], eth = [];
for (let b = fin; b > fin - nBlocks && cands.length < maxCand; b--) {
  const blk = (await rpc(url, 'eth_getBlockByNumber', [hex(b), true])).result;
  const rc = (await rpc(url, 'eth_getBlockReceipts', [hex(b)])).result;
  if (!blk || !rc) { console.log('skip block', b); continue; }
  const byHash = new Map(rc.map((r) => [r.transactionHash, r]));
  for (const tx of blk.transactions) {
    const r = byHash.get(tx.hash); if (!r || r.status !== '0x1') continue;
    if (tx.to && tx.to.toLowerCase() === USDT && tx.input.startsWith('0xa9059cbb') && tx.input.length === 138 && r.logs.length === 1) {
      cands.push({ b, hash: tx.hash, from: tx.from.toLowerCase(), to: '0x' + tx.input.slice(34, 74), value: BigInt('0x' + tx.input.slice(74)), gasUsed: Number(BigInt(r.gasUsed)), gasLimit: Number(BigInt(tx.gas)), type: Number(BigInt(tx.type)), egp: Number(BigInt(r.effectiveGasPrice)) / 1e9 });
    } else if (tx.input === '0x' && tx.to && eth.length < 200) {
      eth.push({ b, hash: tx.hash, gasUsed: Number(BigInt(r.gasUsed)), to: tx.to });
    }
  }
  await sleep(150);
}
console.log(now(), 'candidates', cands.length, 'plain ETH txs', eth.length);
const out = [];
for (const c of cands) {
  try {
    const toBefore = await bal(c.to, c.b - 1);
    const fromAfter = await bal(c.from, c.b);
    const cls = (toBefore === 0n ? 'newRecipient' : 'existingRecipient') + '+' + (fromAfter === 0n ? 'senderEmptied' : 'senderKeeps');
    out.push({ ...c, value: c.value.toString(), toBefore: toBefore.toString(), fromAfter: fromAfter.toString(), cls });
  } catch (e) { console.log('bal err', c.hash, String(e).slice(0, 150)); await sleep(1000); }
  await sleep(120);
}
const groups = {};
for (const o of out) (groups[o.cls] ||= []).push(o);
for (const [k, arr] of Object.entries(groups)) {
  const g = arr.map((x) => x.gasUsed);
  const ex = arr.slice(0, 2).map((x) => `${x.hash}@${x.b}`).join(' ');
  console.log(`${k}: n=${arr.length} gasUsed min=${Math.min(...g)} p50=${pct(g, 50)} p90=${pct(g, 90)} max=${Math.max(...g)} | examples ${ex}`);
}
// native ETH: separate EOA recipients (no code) and gasUsed
const ethEoa = [];
for (const e of eth.slice(0, 60)) {
  const code = (await rpc(url, 'eth_getCode', [e.to, hex(e.b)])).result;
  if (code === '0x') ethEoa.push(e);
  await sleep(100);
}
const eg = ethEoa.map((x) => x.gasUsed);
console.log(`native ETH to EOA: n=${ethEoa.length} gasUsed distinct=${[...new Set(eg)].join(',')} example ${ethEoa[0]?.hash}@${ethEoa[0]?.b}`);
writeFileSync('receipts.json', JSON.stringify({ url, fin, collectedAt: now(), out, ethEoa }, null, 1));
console.log(now(), 'done retries', stats.retries);
