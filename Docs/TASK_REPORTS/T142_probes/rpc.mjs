// Minimal JSON-RPC helper (no deps). Read-only calls only.
export async function rpc(url, method, params = [], { timeoutMs = 30000 } = {}) {
  const body = JSON.stringify({ jsonrpc: '2.0', id: 1, method, params });
  const t0 = Date.now();
  const ctl = new AbortController();
  const tm = setTimeout(() => ctl.abort(), timeoutMs);
  try {
    const r = await fetch(url, { method: 'POST', headers: { 'content-type': 'application/json' }, body, signal: ctl.signal });
    const text = await r.text();
    const ms = Date.now() - t0;
    let j;
    try { j = JSON.parse(text); } catch { return { http: r.status, ms, raw: text.slice(0, 300), bytes: body.length }; }
    return { http: r.status, ms, result: j.result, error: j.error, bytes: body.length };
  } catch (e) {
    return { http: 0, ms: Date.now() - t0, raw: String(e).slice(0, 200), bytes: body.length };
  } finally { clearTimeout(tm); }
}
// Retry wrapper for rate-limit answers (429 / -32005 / "rate limit"); counts retries.
export const stats = { retries: 0 };
export async function rpcR(url, method, params = [], opts = {}) {
  for (let i = 0; ; i++) {
    const r = await rpc(url, method, params, opts);
    const msg = JSON.stringify(r.error || r.raw || '');
    const limited = r.http === 429 || /rate limit|too many|-32005/i.test(msg);
    if (!limited || i >= 6) return r;
    stats.retries++;
    await new Promise((res) => setTimeout(res, 1000 * (i + 1)));
  }
}
export const hex =(n) => '0x' + BigInt(n).toString(16);
export const big = (h) => BigInt(h);
export const now = () => new Date().toISOString();
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
export const pad32 = (addr) => '0x' + addr.toLowerCase().replace(/^0x/, '').padStart(64, '0');
export const TRANSFER = '0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef';
export const USDT = '0xdac17f958d2ee523a2206206994597c13d831ec7';
export const USDC = '0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48';
export function pct(arr, p) { const a = [...arr].sort((x, y) => x - y); if (!a.length) return NaN; const i = Math.min(a.length - 1, Math.max(0, Math.ceil((p / 100) * a.length) - 1)); return a[i]; }
