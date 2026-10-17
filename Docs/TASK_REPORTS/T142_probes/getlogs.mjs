import { rpc, now, hex, pad32, TRANSFER, USDT, USDC, sleep } from './rpc.mjs';
import { randomBytes } from 'node:crypto';
const net = process.argv[2] || 'mainnet';
const eps = net === 'mainnet'
  ? ['https://ethereum-rpc.publicnode.com', 'https://eth.drpc.org', 'https://mainnet.gateway.tenderly.co', 'https://1rpc.io/eth', 'https://eth-mainnet.public.blastapi.io']
  : ['https://ethereum-sepolia-rpc.publicnode.com', 'https://sepolia.gateway.tenderly.co', 'https://1rpc.io/sepolia'];
const tokens = net === 'mainnet' ? [USDT, USDC] : ['0x1c7d4b196cb0c7b01d743fbc6116a902379c7238'];
const ref = eps[0];
const fin = Number(BigInt((await rpc(ref, 'eth_getBlockByNumber', ['finalized', false])).result.number));
// real recipients from last 50 finalized blocks
const seed = await rpc(ref, 'eth_getLogs', [{ fromBlock: hex(fin - 49), toBlock: hex(fin), address: tokens, topics: [TRANSFER] }]);
const real = [...new Set((seed.result || []).map((l) => l.topics[2]).filter(Boolean))].slice(0, 20);
console.log('start', now(), 'net', net, 'finalized', fin, 'realRecipients', real.length, 'seedLogs', (seed.result || []).length);
const mk = (n) => { const a = [...real.slice(0, Math.min(real.length, n))]; while (a.length < n) a.push(pad32('0x' + randomBytes(20).toString('hex'))); return a; };
const ranges = [10, 100, 1000, 5000, 10000];
const sizes = [1, 100, 1000, 5000];
const only = process.argv[3];
for (const url of eps) {
  if (only && !url.includes(only)) continue;
  for (const n of sizes) {
    const list = mk(n);
    for (const r of ranges) {
      const p = { fromBlock: hex(fin - r + 1), toBlock: hex(fin), address: tokens, topics: [TRANSFER, null, n === 1 ? list[0] : list] };
      const x = await rpc(url, 'eth_getLogs', [p], { timeoutMs: 60000 });
      const res = Array.isArray(x.result) ? `ok logs=${x.result.length}` : `ERR ${x.http} ${(x.error ? (x.error.code + ' ' + x.error.message) : x.raw || '').toString().replace(/\s+/g, ' ').slice(0, 160)}`;
      console.log(JSON.stringify({ t: now(), url, list: n, range: r, ms: x.ms, reqBytes: x.bytes, res }));
      await sleep(1200);
    }
  }
}
console.log('end', now());
