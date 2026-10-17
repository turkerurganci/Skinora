// Monthly RPC usage for Skinora's Ethereum load, converted to each provider's unit.
// Load model (per network):
//  - finalized head poll every P s: eth_getBlockByNumber('finalized')
//  - finalized advances once per epoch (32 blocks, 384 s) -> 225 eth_getLogs/day, each 32 blocks;
//    providers with a smaller block-range cap need ceil(32/cap) calls
//  - balance reconciliation: D active deposits x (eth_call balanceOf + eth_getBalance) every R hours
//  - outgoing: T tx/day x (estimateGas 1, eth_call simulate 1, getTransactionCount 1, feeHistory 1, sendRaw 1, receipt 5)
//  - fee watch while a send waits / quotes: eth_feeHistory once a minute all day (upper bound)
const DAYS = 30;
function load({ poll, deposits, reconH, tx, feeWatchPerDay = 1440 }, rangeCap) {
  const epochs = 86400 / 384;
  const recon = (24 / reconH) * deposits;
  return {
    getBlock: 86400 / poll,
    getLogs: epochs * Math.ceil(32 / rangeCap),
    call: recon + tx,
    getBalance: recon,
    estimateGas: tx, txCount: tx, sendRaw: tx, receipt: 5 * tx,
    feeHistory: tx + feeWatchPerDay,
  };
}
const P = {
  'Alchemy Free': { cap: 10, unit: 'CU/month', quota: 30e6, per: 'month', cost: { getBlock: 20, getLogs: 60, call: 26, getBalance: 20, estimateGas: 20, txCount: 20, sendRaw: 40, receipt: 20, feeHistory: 10 } },
  'Chainstack Developer': { cap: 100, unit: 'RU/month', quota: 3e6, per: 'month', cost: Object.fromEntries(['getBlock', 'getLogs', 'call', 'getBalance', 'estimateGas', 'txCount', 'sendRaw', 'receipt', 'feeHistory'].map((k) => [k, 1])) },
  'Infura Core': { cap: 10000, unit: 'credits/day', quota: 3e6, per: 'day', cost: { getBlock: 80, getLogs: 255, call: 80, getBalance: 80, estimateGas: 300, txCount: 80, sendRaw: 80, receipt: 80, feeHistory: 80 } },
  'QuickNode trial (1 month)': { cap: 5, unit: 'credits/month', quota: 10e6, per: 'month', cost: Object.fromEntries(['getBlock', 'getLogs', 'call', 'getBalance', 'estimateGas', 'txCount', 'sendRaw', 'receipt', 'feeHistory'].map((k) => [k, 20])) },
  'dRPC Free (keyed)': { cap: 100, unit: 'CU/month', quota: 210e6, per: 'month', cost: Object.fromEntries(['getBlock', 'getLogs', 'call', 'getBalance', 'estimateGas', 'txCount', 'sendRaw', 'receipt', 'feeHistory'].map((k) => [k, 20])) },
  'Ankr Freemium': { cap: 3000, unit: 'credits/month', quota: 200e6, per: 'month', cost: Object.fromEntries(['getBlock', 'getLogs', 'call', 'getBalance', 'estimateGas', 'txCount', 'sendRaw', 'receipt', 'feeHistory'].map((k) => [k, 200])) },
};
const scenarios = {
  'mainnet poll 10s': { poll: 10, deposits: 300, reconH: 1, tx: 40 },
  'mainnet poll 30s': { poll: 30, deposits: 300, reconH: 1, tx: 40 },
  'sepolia test env poll 30s': { poll: 30, deposits: 20, reconH: 1, tx: 10 },
};
for (const [sn, s] of Object.entries(scenarios)) {
  const calls = load(s, 32);
  const perDay = Object.values(calls).reduce((a, b) => a + b, 0);
  console.log(`\n${sn}: ${Math.round(perDay)} calls/day = ${(perDay * DAYS / 1e6).toFixed(2)}M/month; mix ${JSON.stringify(Object.fromEntries(Object.entries(calls).map(([k, v]) => [k, Math.round(v)])))}`);
  for (const [pn, p] of Object.entries(P)) {
    const c = load(s, p.cap);
    const unitsDay = Object.entries(c).reduce((a, [k, v]) => a + v * p.cost[k], 0);
    const used = p.per === 'day' ? unitsDay : unitsDay * DAYS;
    console.log(`  ${pn}: ${(used / 1e6).toFixed(2)}M ${p.unit} of ${(p.quota / 1e6).toFixed(0)}M => ${((used / p.quota) * 100).toFixed(0)}% (getLogs calls/day ${Math.round(c.getLogs)})`);
  }
}
