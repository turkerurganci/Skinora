import { rpcR as rpc, now, hex, pad32, USDT, USDC, sleep, stats } from './rpc.mjs';
const url = process.argv[2] || 'https://mainnet.gateway.tenderly.co';
const log = (...a) => console.log(now(), ...a);
const fin = (await rpc(url, 'eth_getBlockByNumber', ['finalized', false])).result;
const B = fin.number;
log('endpoint', url, 'finalized block', Number(BigInt(B)), 'ts', new Date(Number(BigInt(fin.timestamp)) * 1000).toISOString());
const call = async (to, data, from, block = B) => {
  const p = { to, data }; if (from) p.from = from;
  const r = await rpc(url, 'eth_call', [p, block]);
  return r.result !== undefined ? r.result : 'ERR ' + JSON.stringify(r.error || r.raw).slice(0, 300);
};
const S = { decimals: '0x313ce567', name: '0x06fdde03', symbol: '0x95d89b41', totalSupply: '0x18160ddd', basisPointsRate: '0xdd644f72', maximumFee: '0x35390714', paused: '0x5c975abb', deprecated: '0x0e136b19', upgradedAddress: '0x26976e3f', owner: '0x8da5cb5b' };
const dec = (h) => (h && h.startsWith('0x') && h.length > 2 ? BigInt(h.slice(0, 66)).toString() : h);
for (const [tok, nm] of [[USDT, 'USDT'], [USDC, 'USDC']]) {
  const code = await rpc(url, 'eth_getCode', [tok, B]);
  log(nm, 'codeBytes', (code.result.length - 2) / 2);
  for (const [k, sel] of Object.entries(S)) {
    if (nm === 'USDC' && ['basisPointsRate', 'maximumFee', 'deprecated', 'upgradedAddress'].includes(k)) continue;
    const r = await call(tok, sel);
    log(nm, k, ['name', 'symbol'].includes(k) ? r.slice(0, 200) : `${r} => ${dec(r)}`);
  }
}
// USDC proxy implementation slot (zeppelinos)
const slot = await rpc(url, 'eth_getStorageAt', [USDC, '0x7050c9e0f4ca769c69bd3a8ef740bc37934f8e2c036e5a723fd8ee048ed3f8c3', B]);
log('USDC proxy impl slot', slot.result);
// transfer return-data via eth_call from a large holder
const holders = ['0x28c6c06298d514db089934071355e5743bf21d60', '0xf977814e90da44bfa03b6295a0616a897441acec', '0x5041ed759dd4afc3a72b8192c143f72f4724081a'];
const to = '0x000000000000000000000000000000000000dead';
const freshTo = '0x' + 'ab'.repeat(19) + '01';
for (const [tok, nm] of [[USDT, 'USDT'], [USDC, 'USDC']]) {
  for (const h of holders) {
    const bal = await call(tok, '0x70a08231' + pad32(h).slice(2));
    if (BigInt(bal) === 0n) { log(nm, 'holder', h, 'balance 0'); continue; }
    log(nm, 'holder', h, 'balance raw', BigInt(bal).toString());
    const data = '0xa9059cbb' + pad32(freshTo).slice(2) + BigInt(1).toString(16).padStart(64, '0');
    const r = await call(tok, data, h);
    log(nm, 'eth_call transfer(fresh,1) from holder -> returnData', JSON.stringify(r), 'len', r.startsWith('0x') ? (r.length - 2) / 2 : 'n/a');
    const eg = await rpc(url, 'eth_estimateGas', [{ from: h, to: tok, data }]);
    log(nm, 'estimateGas transfer(fresh,1)', eg.result ? Number(BigInt(eg.result)) : JSON.stringify(eg.error));
    break;
  }
}
// blacklist: find recent AddedBlackList events (USDT) and Blacklisted (USDC)
const lastN = Number(BigInt(B));
async function findEvents(tok, topic, span, steps) {
  const found = [];
  for (let i = 0; i < steps && found.length < 5; i++) {
    const to_ = lastN - i * span, from_ = to_ - span + 1;
    const r = await rpc(url, 'eth_getLogs', [{ fromBlock: hex(from_), toBlock: hex(to_), address: tok, topics: [topic] }], { timeoutMs: 60000 });
    if (!Array.isArray(r.result)) { log('getLogs err', from_, to_, JSON.stringify(r.error || r.raw).slice(0, 200)); await sleep(1000); continue; }
    found.push(...r.result);
    await sleep(500);
  }
  return found;
}
const ab = await findEvents(USDT, '0x42e160154868087d6bfdc0ca23d96a1c1cfa32f1b72ba9ba27b69b98a0d819dc', 50000, 10);
log('USDT AddedBlackList events found', ab.length);
const bl = [];
for (const e of ab.slice(-5)) {
  const addr = '0x' + e.data.slice(26, 66);
  const st = await call(USDT, '0xe47d6060' + pad32(addr).slice(2));
  const bal = await call(USDT, '0x70a08231' + pad32(addr).slice(2));
  log('USDT blacklisted', addr, 'block', Number(BigInt(e.blockNumber)), 'tx', e.transactionHash, 'isBlackListed', dec(st), 'balance', dec(bal));
  bl.push({ addr, bal: BigInt(bal) });
}
const withBal = bl.find((x) => x.bal > 0n);
if (withBal) {
  const data = '0xa9059cbb' + pad32(freshTo).slice(2) + BigInt(1).toString(16).padStart(64, '0');
  log('USDT eth_call transfer FROM blacklisted', withBal.addr, '->', await call(USDT, data, withBal.addr));
}
if (bl.length) {
  const data = '0xa9059cbb' + pad32(bl[bl.length - 1].addr).slice(2) + BigInt(1).toString(16).padStart(64, '0');
  log('USDT eth_call transfer TO blacklisted', bl[bl.length - 1].addr, 'from holder ->', JSON.stringify(await call(USDT, data, holders[0])));
}
const ub = await findEvents(USDC, '0xffa4e6181777692565cf28528fc88fd1516ea86b56da075235fa575af6a4b855', 50000, 10);
log('USDC Blacklisted events found', ub.length);
for (const e of ub.slice(-2)) {
  const addr = '0x' + e.topics[1].slice(26);
  const st = await call(USDC, '0xfe575a87' + pad32(addr).slice(2));
  log('USDC blacklisted', addr, 'block', Number(BigInt(e.blockNumber)), 'isBlacklisted', dec(st));
  const data = '0xa9059cbb' + pad32(addr).slice(2) + BigInt(1).toString(16).padStart(64, '0');
  const hh = holders[0];
  log('USDC eth_call transfer TO blacklisted from holder ->', JSON.stringify(await call(USDC, data, hh)));
}
// Params / Pause history over the whole contract life (deploy block 4634748)
for (const [nm, topic] of [['Params', '0xb044a1e409eac5c48e5af22d4af52670dd1a99059537a78b31b48c6500a6354e'], ['Pause', '0x6985a02210a168e66602d3235cb6db0e70f92b3ba4d376a33c0f3d9434bff625'], ['Unpause', '0x7805862f689e2f13df9f062ff482ad3ad112aca9e0847911ed832e158c525b33']]) {
  const r = await rpc(url, 'eth_getLogs', [{ fromBlock: hex(4634748), toBlock: B, address: USDT, topics: [topic] }], { timeoutMs: 90000 });
  if (Array.isArray(r.result)) log(`USDT ${nm} events since deploy:`, r.result.length, r.result.map((e) => `${Number(BigInt(e.blockNumber))}:${e.data}`).join(' | ').slice(0, 600));
  else log(`USDT ${nm} full-range getLogs err`, JSON.stringify(r.error || r.raw).slice(0, 300));
}
log('done; rate-limit retries', stats.retries);
