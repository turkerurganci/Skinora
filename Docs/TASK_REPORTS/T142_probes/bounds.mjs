import { rpc, now, hex, pad32, TRANSFER, USDT, USDC, sleep } from './rpc.mjs';
import { randomBytes } from 'node:crypto';
const rnd = (n) => Array.from({ length: n }, () => pad32('0x' + randomBytes(20).toString('hex')));
const show = (x) => (Array.isArray(x.result) ? `ok logs=${x.result.length}` : `ERR ${x.http} ${JSON.stringify(x.error || x.raw).slice(0, 140)}`);
const PN = 'https://ethereum-rpc.publicnode.com', DR = 'https://eth.drpc.org', TD = 'https://mainnet.gateway.tenderly.co';
const head = Number(BigInt((await rpc(PN, 'eth_blockNumber', [])).result));
const fin = Number(BigInt((await rpc(PN, 'eth_getBlockByNumber', ['finalized', false])).result.number));
console.log(now(), 'head', head, 'finalized', fin);
const q = (from, to, list) => ({ fromBlock: hex(from), toBlock: hex(to), address: [USDT, USDC], topics: [TRANSFER, null, list] });
const L200 = rnd(200);
// 1) dRPC free: range cap
for (const r of [100, 101, 200, 500, 999, 1000]) {
  const x = await rpc(DR, 'eth_getLogs', [q(fin - r + 1, fin, L200)]);
  console.log(now(), 'dRPC range', r, show(x)); await sleep(1200);
}
// 2) PublicNode: how far back (range 10 ending at head-k)
for (const k of [1000, 5000, 7000, 8000, 8100, 8191, 8300, 9000, 9500, 12000]) {
  const x = await rpc(PN, 'eth_getLogs', [q(head - k - 9, head - k, L200)]);
  console.log(now(), 'PublicNode 10-block window ending head -', k, show(x)); await sleep(1200);
}
// 2b) PublicNode: range size from finalized
for (const r of [5000, 6000, 7000, 8000]) {
  const x = await rpc(PN, 'eth_getLogs', [q(fin - r + 1, fin, L200)]);
  console.log(now(), 'PublicNode range', r, 'ending finalized', show(x)); await sleep(1200);
}
// 2c) PublicNode eth_call at an old block (state availability)
for (const k of [64, 128, 129, 1000, 9000, 20000]) {
  const x = await rpc(PN, 'eth_call', [{ to: USDT, data: '0x313ce567' }, hex(head - k)]);
  console.log(now(), 'PublicNode eth_call at head -', k, x.result ? 'ok' : 'ERR ' + JSON.stringify(x.error || x.raw).slice(0, 140)); await sleep(800);
}
// 3) Tenderly: list cap and result cap
for (const [n, r] of [[2000, 10], [3000, 10], [4000, 10], [1000, 2000], [200, 2000], [200, 3000]]) {
  const x = await rpc(TD, 'eth_getLogs', [q(fin - r + 1, fin, rnd(n))], { timeoutMs: 60000 });
  console.log(now(), 'Tenderly list', n, 'range', r, show(x)); await sleep(2000);
}
// 3b) Tenderly result cap with no address filter in topics[2]
for (const r of [1000, 1200, 1500, 2000]) {
  const x = await rpc(TD, 'eth_getLogs', [{ fromBlock: hex(fin - r + 1), toBlock: hex(fin), address: [USDT], topics: [TRANSFER] }], { timeoutMs: 60000 });
  console.log(now(), 'Tenderly unfiltered USDT range', r, show(x)); await sleep(2000);
}
console.log(now(), 'end');
