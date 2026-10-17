import { rpcR as rpc, now, hex, pad32, USDT, USDC, sleep, stats } from './rpc.mjs';
const url = process.argv[2] || 'https://mainnet.gateway.tenderly.co';
const log = (...a) => console.log(now(), ...a);
const fin = Number(BigInt((await rpc(url, 'eth_getBlockByNumber', ['finalized', false])).result.number));
const B = hex(fin);
const call = async (to, data, from) => { const p = { to, data }; if (from) p.from = from; const r = await rpc(url, 'eth_call', [p, B]); return r.result !== undefined ? r.result : 'REVERT/ERR ' + JSON.stringify(r.error || r.raw).slice(0, 200); };
log('endpoint', url, 'finalized', fin);
async function scan(tok, topic, span, steps, want) {
  const found = [];
  for (let i = 0; i < steps && found.length < want; i++) {
    const to = fin - i * span, from = to - span + 1;
    const r = await rpc(url, 'eth_getLogs', [{ fromBlock: hex(from), toBlock: hex(to), address: tok, topics: [topic] }], { timeoutMs: 60000 });
    if (!Array.isArray(r.result)) { log('getLogs err', from, to, JSON.stringify(r.error || r.raw).slice(0, 160)); continue; }
    found.push(...r.result);
    await sleep(300);
  }
  return found;
}
// try one wide call first for the rare admin events
for (const [nm, topic] of [['Params', '0xb044a1e409eac5c48e5af22d4af52670dd1a99059537a78b31b48c6500a6354e'], ['Pause', '0x6985a02210a168e66602d3235cb6db0e70f92b3ba4d376a33c0f3d9434bff625']]) {
  const r = await rpc(url, 'eth_getLogs', [{ fromBlock: hex(4634748), toBlock: B, address: USDT, topics: [topic] }], { timeoutMs: 120000 });
  if (Array.isArray(r.result)) log(`USDT ${nm} events since deploy (block 4634748):`, r.result.length, r.result.map((e) => `${Number(BigInt(e.blockNumber))}:${e.data}`).join(' | ').slice(0, 400));
  else log(`USDT ${nm} full-range err`, JSON.stringify(r.error || r.raw).slice(0, 200));
  await sleep(1000);
}
const ab = await scan(USDT, '0x42e160154868087d6bfdc0ca23d96a1c1cfa32f1b72ba9ba27b69b98a0d819dc', 10000, 80, 8);
log('USDT AddedBlackList events found', ab.length);
const holder = '0x28c6c06298d514db089934071355e5743bf21d60';
const fresh = '0x' + 'ab'.repeat(19) + '03';
let withBal = null, any = null;
for (const e of ab.slice(-8)) {
  const addr = '0x' + e.data.slice(26, 66);
  const st = BigInt(await call(USDT, '0xe47d6060' + pad32(addr).slice(2)));
  const bal = BigInt(await call(USDT, '0x70a08231' + pad32(addr).slice(2)));
  log('USDT AddedBlackList', addr, 'block', Number(BigInt(e.blockNumber)), 'tx', e.transactionHash, 'isBlackListed now', st.toString(), 'balance', bal.toString());
  if (st === 1n) { any = addr; if (bal > 0n) withBal = addr; }
}
const t = (to) => '0xa9059cbb' + pad32(to).slice(2) + '1'.padStart(64, '0');
if (withBal) log('USDT transfer(fresh,1) FROM blacklisted', withBal, '->', await call(USDT, t(fresh), withBal));
if (any) log('USDT transfer(blacklisted,1) TO blacklisted', any, 'from holder ->', JSON.stringify(await call(USDT, t(any), holder)));
const ub = await scan(USDC, '0xffa4e6181777692565cf28528fc88fd1516ea86b56da075235fa575af6a4b855', 10000, 80, 3);
log('USDC Blacklisted events found', ub.length);
for (const e of ub.slice(-2)) {
  const addr = '0x' + e.topics[1].slice(26);
  const st = BigInt(await call(USDC, '0xfe575a87' + pad32(addr).slice(2)));
  log('USDC Blacklisted', addr, 'block', Number(BigInt(e.blockNumber)), 'isBlacklisted now', st.toString());
  if (st === 1n) log('USDC transfer(blacklisted,1) TO blacklisted from holder ->', JSON.stringify(await call(USDC, t(addr), holder)));
}
log('done retries', stats.retries);
