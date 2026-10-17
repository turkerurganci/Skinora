import { rpcR as rpc, now, hex, pad32, TRANSFER, sleep } from './rpc.mjs';
import { randomBytes } from 'node:crypto';
const PN = 'https://ethereum-sepolia-rpc.publicnode.com', TD = 'https://sepolia.gateway.tenderly.co';
const USDC_S = '0x1c7d4b196cb0c7b01d743fbc6116a902379c7238', AAVE_USDT = '0xaa8e23fb1079ea71e0a56f48a2aa51851d8433d0', AAVE_USDC = '0x94a9d9ac8a22534e3faca9f4e7f2e2cf85d5e4c8';
const fin = Number(BigInt((await rpc(PN, 'eth_getBlockByNumber', ['finalized', false])).result.number));
console.log(now(), 'sepolia finalized', fin);
// 1) Tenderly truncation check: same 20 real recipients, list padded to 1000 vs 5000, same range, repeated
const seed = await rpc(PN, 'eth_getLogs', [{ fromBlock: hex(fin - 49), toBlock: hex(fin), address: [USDC_S], topics: [TRANSFER] }]);
const real = [...new Set(seed.result.map((l) => l.topics[2]))].slice(0, 20);
const pad = (n) => [...real, ...Array.from({ length: n - real.length }, () => pad32('0x' + randomBytes(20).toString('hex')))];
for (let rep = 0; rep < 2; rep++) for (const n of [20, 1000, 2000, 3000, 5000]) for (const url of [PN, TD]) {
  const x = await rpc(url, 'eth_getLogs', [{ fromBlock: hex(fin - 999), toBlock: hex(fin), address: [USDC_S], topics: [TRANSFER, null, pad(n)] }], { timeoutMs: 60000 });
  console.log(now(), 'rep', rep, url.includes('tenderly') ? 'Tenderly' : 'PublicNode', 'list', n, 'range 1000', Array.isArray(x.result) ? 'logs=' + x.result.length : 'ERR ' + JSON.stringify(x.error || x.raw).slice(0, 120));
  await sleep(800);
}
// 2) Sepolia tokens
const call = async (to, data, from) => { const p = { to, data }; if (from) p.from = from; const r = await rpc(PN, 'eth_call', [p, hex(fin)]); return r.result ?? 'ERR ' + JSON.stringify(r.error).slice(0, 160); };
const str = (h) => { try { const len = Number(BigInt('0x' + h.slice(66, 130))); return Buffer.from(h.slice(130, 130 + len * 2), 'hex').toString(); } catch { return h; } };
for (const [nm, a] of [['Circle USDC (Sepolia)', USDC_S], ['Aave USDT (Sepolia)', AAVE_USDT], ['Aave USDC (Sepolia)', AAVE_USDC]]) {
  const code = (await rpc(PN, 'eth_getCode', [a, hex(fin)])).result;
  const dec = BigInt(await call(a, '0x313ce567'));
  console.log(now(), nm, a, 'codeBytes', (code.length - 2) / 2, 'name', str(await call(a, '0x06fdde03')), 'symbol', str(await call(a, '0x95d89b41')), 'decimals', dec.toString());
  // find a holder from recent Transfer logs and simulate transfer(fresh,1)
  const lg = await rpc(PN, 'eth_getLogs', [{ fromBlock: hex(fin - 4999), toBlock: hex(fin), address: a, topics: [TRANSFER] }]);
  const holders = [...new Set((lg.result || []).map((l) => '0x' + l.topics[2].slice(26)))];
  let done = false;
  for (const h of holders.slice(0, 30)) {
    const bal = BigInt(await call(a, '0x70a08231' + pad32(h).slice(2)));
    if (bal === 0n) continue;
    const data = '0xa9059cbb' + pad32('0x' + 'ab'.repeat(19) + '02').slice(2) + '1'.padStart(64, '0');
    const r = await call(a, data, h);
    console.log(now(), nm, 'transfer(fresh,1) from holder', h, 'returnData', r, '(recent Transfer logs in 5000 blocks:', (lg.result || []).length + ')');
    done = true; break;
  }
  if (!done) console.log(now(), nm, 'no holder with balance found in recent logs', (lg.result || []).length);
}
// 3) Sepolia fee history (last 1024 blocks)
const fh = (await rpc(PN, 'eth_feeHistory', ['0x400', 'latest', [50]])).result;
const base = fh.baseFeePerGas.map((h) => Number(BigInt(h)) / 1e9).sort((a, b) => a - b);
const tips = fh.reward.map((a) => Number(BigInt(a[0])) / 1e9).sort((a, b) => a - b);
console.log(now(), 'Sepolia baseFee gwei last 1024 blocks: p50', base[512].toFixed(6), 'p99', base[1013].toFixed(6), 'max', base[1024].toFixed(6), '| tip p50', tips[512].toFixed(6));
