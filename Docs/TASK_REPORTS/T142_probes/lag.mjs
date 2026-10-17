import { rpcR as rpc, now, sleep } from './rpc.mjs';
const nets = { mainnet: 'https://ethereum-rpc.publicnode.com', sepolia: 'https://ethereum-sepolia-rpc.publicnode.com' };
const samples = Number(process.argv[2] || 16), gapMs = Number(process.argv[3] || 60000);
for (let i = 0; i < samples; i++) {
  for (const [net, url] of Object.entries(nets)) {
    const [h, f, s] = await Promise.all(['latest', 'finalized', 'safe'].map((t) => rpc(url, 'eth_getBlockByNumber', [t, false])));
    if (!h.result || !f.result || !s.result) { console.log(now(), net, 'ERR'); continue; }
    const n = (b) => Number(BigInt(b.number)), ts = (b) => Number(BigInt(b.timestamp));
    console.log(JSON.stringify({ t: now(), net, head: n(h.result), fin: n(f.result), safe: n(s.result), lagBlocks: n(h.result) - n(f.result), lagSec: ts(h.result) - ts(f.result), safeLagBlocks: n(h.result) - n(s.result), wallMinusFinSec: Math.round(Date.now() / 1000) - ts(f.result) }));
  }
  if (i < samples - 1) await sleep(gapMs);
}
