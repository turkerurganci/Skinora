import { rpcR as rpc, hex } from './rpc.mjs';
const url = 'https://mainnet.gateway.tenderly.co';
const target = Math.floor(new Date(process.argv[2]).getTime() / 1000);
let lo = 20000000, hi = Number(BigInt((await rpc(url, 'eth_blockNumber', [])).result));
const ts = async (n) => Number(BigInt((await rpc(url, 'eth_getBlockByNumber', [hex(n), false])).result.timestamp));
while (hi - lo > 1) { const mid = Math.floor((lo + hi) / 2); if ((await ts(mid)) <= target) lo = mid; else hi = mid; }
console.log(process.argv[2], 'block', lo, new Date((await ts(lo)) * 1000).toISOString());
