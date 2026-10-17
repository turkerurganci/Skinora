import { rpc, now } from './rpc.mjs';
const eps = {
  mainnet: [
    'https://ethereum-rpc.publicnode.com', 'https://eth.drpc.org', 'https://rpc.ankr.com/eth', 'https://cloudflare-eth.com',
    'https://eth.llamarpc.com', 'https://1rpc.io/eth', 'https://eth-mainnet.public.blastapi.io', 'https://eth.merkle.io',
    'https://mainnet.gateway.tenderly.co', 'https://rpc.flashbots.net', 'https://ethereum.publicnode.com',
    'https://eth-mainnet.g.alchemy.com/v2/demo', 'https://mainnet.infura.io/v3/',
  ],
  sepolia: [
    'https://ethereum-sepolia-rpc.publicnode.com', 'https://sepolia.drpc.org', 'https://rpc.ankr.com/eth_sepolia', 'https://rpc.sepolia.org',
    'https://1rpc.io/sepolia', 'https://sepolia.gateway.tenderly.co', 'https://eth-sepolia.public.blastapi.io', 'https://rpc2.sepolia.org',
  ],
};
console.log('probe start', now());
for (const [net, list] of Object.entries(eps)) {
  for (const url of list) {
    const c = await rpc(url, 'eth_chainId', [], { timeoutMs: 15000 });
    const f = await rpc(url, 'eth_getBlockByNumber', ['finalized', false], { timeoutMs: 15000 });
    const s = await rpc(url, 'eth_getBlockByNumber', ['safe', false], { timeoutMs: 15000 });
    const out = {
      net, url, http: c.http, ms: c.ms, chainId: c.result ?? (c.error?.message ?? c.raw),
      finalized: f.result ? Number(BigInt(f.result.number)) : (f.error?.message ?? f.raw ?? null),
      safe: s.result ? Number(BigInt(s.result.number)) : (s.error?.message ?? s.raw ?? null),
    };
    console.log(JSON.stringify(out));
  }
}
console.log('probe end', now());
