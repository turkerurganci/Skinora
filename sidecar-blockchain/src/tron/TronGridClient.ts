import TronWeb from 'tronweb';
import { config } from '../config/index.js';
import { logger } from '../logger.js';
import { tronApiErrorsTotal, tronApiRequestDuration } from '../metrics.js';

/**
 * Single TRC-20 transfer record as returned by the TronGrid
 * `/v1/accounts/{address}/transactions/trc20` endpoint (08 §3.4).
 *
 * Only the fields the monitor consumes are typed; TronGrid may include
 * additional metadata that we ignore.
 */
export interface Trc20Record {
  transaction_id: string;
  token_info: {
    address: string;
    symbol?: string;
    decimals?: number;
    name?: string;
  };
  block_timestamp: number;
  from: string;
  to: string;
  type: string;
  value: string;
}

export interface Trc20ListResponse {
  records: Trc20Record[];
  /** Cursor for the next page. Stable across polls per TronGrid contract. */
  fingerprint: string | null;
}

export interface ListTrc20Options {
  address: string;
  /** Filter by token contract — phase 1 only; omit for phase 2 wrong-token scan. */
  contractAddress?: string;
  fingerprint?: string;
  /** Default 20 — matches 08 §3.4 paging strategy. */
  limit?: number;
}

export interface TransactionInfo {
  /** Solid block height that included the transaction; missing while still pending on solidity node. */
  blockNumber?: number;
  /** TronGrid surfaces failed contract calls — monitor ignores anything that is not SUCCESS. */
  contractRet?: string;
}

/**
 * A single TRC-20 `Transfer` event resolved from a transaction's on-chain log
 * array (08 §3.4 — WP10 event-index dedup). <c>index</c> is the position of
 * the log inside the transaction's `log[]` array, i.e. the canonical on-chain
 * event index; <c>value</c> is the raw integer transfer amount (base units,
 * decimals not applied) decoded from the log `data`. The monitor correlates a
 * trc20-list record to its log entry by matching <c>value</c>, which keeps the
 * per-event amount authoritative while still assigning a stable, real event
 * index that survives re-polls and restarts.
 */
export interface TransferLogEntry {
  index: number;
  value: string;
}

/**
 * keccak256("Transfer(address,address,uint256)") — topic[0] of every TRC-20
 * (ERC-20 compatible) Transfer event. Lower-case, no 0x prefix, matching the
 * shape TronGrid returns in `log[].topics`.
 */
const TRANSFER_EVENT_TOPIC = 'ddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef';

interface RawTransactionLog {
  address?: string;
  topics?: string[];
  data?: string;
}

/**
 * Convert a base58 Tron address to its 20-byte EVM-style hex (lower-case, no
 * `41` prefix) so it can be compared against a log topic / log address, which
 * TronGrid surfaces in hex. Returns null for an unparseable address.
 */
function toEvmHex20(base58Address: string): string | null {
  try {
    const hex = TronWeb.address.toHex(base58Address); // '41' + 40 hex chars
    if (typeof hex !== 'string' || hex.length === 0) return null;
    return hex.replace(/^0x/i, '').toLowerCase().slice(-40);
  } catch {
    return null;
  }
}

/**
 * Extract every TRC-20 `Transfer` log directed at <c>toAddress</c> from the
 * <c>contractAddress</c> contract, returning each one's on-chain log index and
 * decoded value. Pure + exported for table-driven unit tests.
 */
export function extractTransferLogEntries(
  logs: RawTransactionLog[],
  contractAddress: string,
  toAddress: string,
): TransferLogEntry[] {
  const contractHex = toEvmHex20(contractAddress);
  const toHex = toEvmHex20(toAddress);
  if (!contractHex || !toHex) return [];

  const entries: TransferLogEntry[] = [];
  logs.forEach((log, index) => {
    if (!log || !Array.isArray(log.topics) || log.topics.length < 3) return;
    if ((log.topics[0] ?? '').toLowerCase() !== TRANSFER_EVENT_TOPIC) return;

    const addrHex = (log.address ?? '').replace(/^0x/i, '').toLowerCase().slice(-40);
    if (addrHex !== contractHex) return;

    const recipientHex = (log.topics[2] ?? '').toLowerCase().slice(-40);
    if (recipientHex !== toHex) return;

    let value: string;
    try {
      const raw = log.data && log.data.length > 0 ? log.data : '0';
      value = BigInt(`0x${raw.replace(/^0x/i, '')}`).toString();
    } catch {
      value = '0';
    }
    entries.push({ index, value });
  });
  return entries;
}

/**
 * Raw balances at a single Tron account, as observed by reconciliation (T76 —
 * 05 §3.3). `trx` is in SUN (1 TRX = 1_000_000 SUN); `trc20` keys are the
 * token contract addresses and values are the raw integer amounts (apply the
 * token's decimals to convert). Snapshot block height is captured once per
 * batch by the caller (see `walletBalancesHandler`) and shared across every
 * address in the same request.
 */
export interface AccountBalances {
  address: string;
  trx: string;
  trc20: Record<string, string>;
}

/**
 * TronGrid REST client scoped to the three calls the active monitor needs
 * (08 §3.4). Keeps the wire surface narrow so unit tests can mock
 * `globalThis.fetch` without pulling tronweb into the rule layer.
 *
 * <para>
 * Auth note: the TRON_API_KEY (TronGrid pro plan) is sent as `TRON-PRO-API-KEY`
 * header per TronGrid docs. Missing key is allowed in dev — pro rate limit
 * still applies, but the public endpoints respond.
 * </para>
 *
 * <para>
 * Resilience (WP10 — 08 §3.5 / §3.6): every request goes through
 * <see cref="fetchResilient"/>, which fails over from the primary to the
 * secondary <c>TRON_API_KEY</c> on a 429 / key-suspension (403) — the two
 * keys hold independent rate-limit pools — and applies a short, bounded
 * exponential backoff if every key is throttled or the provider returns 5xx.
 * The schedule stays well under the polling interval so a single stalled
 * request never blocks the whole monitor tick; the loop re-polls naturally.
 * </para>
 */
export interface TronGridClientOptions {
  apiKeySecondary?: string;
  maxRetries?: number;
  backoffBaseMs?: number;
  backoffCapMs?: number;
  /** Overridable so unit tests skip real timers. */
  sleepFn?: (ms: number) => Promise<void>;
}

export class TronGridClient {
  private readonly apiKeySecondary: string;
  private readonly maxRetries: number;
  private readonly backoffBaseMs: number;
  private readonly backoffCapMs: number;
  private readonly sleepFn: (ms: number) => Promise<void>;
  /** Sticky key pointer — rotates on rate-limit so the next request alternates. */
  private keyIndex = 0;

  constructor(
    private readonly fullNodeUrl: string = config.tronFullNodeUrl,
    private readonly solidityUrl: string = config.tronSolidityUrl,
    private readonly apiKey: string = config.tronApiKey,
    private readonly fetchFn: typeof fetch = fetch,
    options: TronGridClientOptions = {},
  ) {
    this.apiKeySecondary = options.apiKeySecondary ?? config.tronApiKeySecondary;
    this.maxRetries = options.maxRetries ?? config.tronGridMaxRetries;
    this.backoffBaseMs = options.backoffBaseMs ?? config.tronGridRetryBackoffBaseMs;
    this.backoffCapMs = options.backoffCapMs ?? config.tronGridRetryBackoffCapMs;
    this.sleepFn = options.sleepFn ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
  }

  async listTrc20(options: ListTrc20Options): Promise<Trc20ListResponse> {
    const params = new URLSearchParams();
    params.set('only_confirmed', 'true');
    params.set('limit', String(options.limit ?? 20));
    if (options.contractAddress) {
      params.set('contract_address', options.contractAddress);
    }
    if (options.fingerprint) {
      params.set('fingerprint', options.fingerprint);
    }

    const url = `${this.fullNodeUrl}/v1/accounts/${encodeURIComponent(options.address)}/transactions/trc20?${params.toString()}`;
    const endpointLabel = options.contractAddress ? 'trc20.filtered' : 'trc20.unfiltered';

    const json = await this.getJson<{ data?: Trc20Record[]; meta?: { fingerprint?: string } }>(
      url,
      endpointLabel,
    );

    return {
      records: Array.isArray(json.data) ? json.data : [],
      fingerprint: json.meta?.fingerprint ?? null,
    };
  }

  /**
   * Returns the current solid (irreversible) block height. Tron finality is
   * counted against the solidity node so 20-block confirmation cannot be
   * faked by a single witness producing a reorg-able block on the full node.
   */
  async getNowSolidBlock(): Promise<number> {
    const url = `${this.solidityUrl}/walletsolidity/getnowblock`;
    const json = await this.postJson<{ block_header?: { raw_data?: { number?: number } } }>(
      url,
      {},
      'walletsolidity.getnowblock',
    );
    const block = json.block_header?.raw_data?.number;
    if (typeof block !== 'number' || !Number.isFinite(block)) {
      throw new Error('TronGrid getnowblock returned no block number');
    }
    return block;
  }

  /**
   * Returns the TRX + TRC-20 balances at a single Tron account, taken from
   * the TronGrid extended-account endpoint (`GET /v1/accounts/{address}`).
   * Used by the reconciliation job (T76 — 05 §3.3) to compare on-chain state
   * against the platform ledger. Raw integer amounts are returned verbatim;
   * the caller applies the token's <c>decimals</c> to convert to a
   * human-scale amount. Empty TRX balance is reported as <c>'0'</c>;
   * missing TRC-20 entries reflect "no balance for that token at this
   * address" — the caller decides whether that is a finding.
   */
  async getAccountBalances(address: string): Promise<AccountBalances> {
    const url = `${this.fullNodeUrl}/v1/accounts/${encodeURIComponent(address)}`;
    const json = await this.getJson<{
      data?: Array<{
        balance?: number | string;
        trc20?: Array<Record<string, string>>;
      }>;
    }>(url, 'accounts.get');

    const data = Array.isArray(json.data) && json.data.length > 0 ? json.data[0] : undefined;
    if (!data) {
      // TronGrid returns `data: []` for an address with no on-chain footprint.
      // Treat this as a zero-balance account so reconciliation can still
      // produce a comparison (expected may also be zero).
      return { address, trx: '0', trc20: {} };
    }

    const trx =
      typeof data.balance === 'number'
        ? Math.trunc(data.balance).toString()
        : typeof data.balance === 'string' && data.balance.length > 0
          ? data.balance
          : '0';

    const trc20: Record<string, string> = {};
    if (Array.isArray(data.trc20)) {
      for (const entry of data.trc20) {
        if (!entry || typeof entry !== 'object') continue;
        for (const [contractAddress, rawValue] of Object.entries(entry)) {
          if (typeof rawValue === 'string') {
            trc20[contractAddress] = rawValue;
          }
        }
      }
    }

    return { address, trx, trc20 };
  }

  /**
   * Looks up a transaction on the solidity node. Returns the block number
   * once the tx is included in a solid block. Pre-solid txs return undefined
   * `blockNumber` so the caller can retry on the next tick.
   */
  async getTransactionInfoById(txHash: string): Promise<TransactionInfo | null> {
    const url = `${this.solidityUrl}/walletsolidity/gettransactioninfobyid`;
    const json = await this.postJson<{
      id?: string;
      blockNumber?: number;
      receipt?: { result?: string };
    }>(url, { value: txHash }, 'walletsolidity.gettransactioninfobyid');

    if (!json || !json.id) {
      return null;
    }
    return {
      blockNumber: typeof json.blockNumber === 'number' ? json.blockNumber : undefined,
      contractRet: json.receipt?.result,
    };
  }

  /**
   * Resolve the on-chain log entries for the TRC-20 <c>Transfer</c>s that the
   * <c>contractAddress</c> contract sent to <c>toAddress</c> inside the given
   * transaction (08 §3.4 — WP10 event-index dedup). Returns each matching
   * log's canonical event index + decoded value so the monitor can assign a
   * stable, per-event identity. Returns an empty array when the solidity node
   * has no logs yet (lag) or the lookup fails — the caller falls back to the
   * status-quo single-event index 0 so it never regresses.
   */
  async resolveTransferEventIndices(
    txHash: string,
    contractAddress: string,
    toAddress: string,
  ): Promise<TransferLogEntry[]> {
    const url = `${this.solidityUrl}/walletsolidity/gettransactioninfobyid`;
    try {
      const json = await this.postJson<{ log?: RawTransactionLog[] }>(
        url,
        { value: txHash },
        'walletsolidity.gettransactioninfobyid.logs',
      );
      const logs = Array.isArray(json.log) ? json.log : [];
      return extractTransferLogEntries(logs, contractAddress, toAddress);
    } catch (err) {
      logger.debug(
        { txHash, err: (err as Error).message },
        'Event-index log lookup failed — caller falls back to index 0',
      );
      return [];
    }
  }

  private async getJson<T>(url: string, endpointLabel: string): Promise<T> {
    return this.measure(endpointLabel, async () => {
      const response = await this.fetchResilient(
        url,
        { method: 'GET', headers: { accept: 'application/json' } },
        endpointLabel,
      );
      return (await response.json()) as T;
    });
  }

  private async postJson<T>(url: string, body: unknown, endpointLabel: string): Promise<T> {
    return this.measure(endpointLabel, async () => {
      const response = await this.fetchResilient(
        url,
        {
          method: 'POST',
          headers: { accept: 'application/json', 'content-type': 'application/json' },
          body: JSON.stringify(body),
        },
        endpointLabel,
      );
      return (await response.json()) as T;
    });
  }

  /** Active key set — primary then secondary; `[undefined]` when none configured (dev). */
  private apiKeys(): (string | undefined)[] {
    const keys = [this.apiKey, this.apiKeySecondary].filter(
      (k): k is string => typeof k === 'string' && k.length > 0,
    );
    return keys.length > 0 ? keys : [undefined];
  }

  private backoffMs(attempt: number): number {
    return Math.min(this.backoffBaseMs * 2 ** (attempt - 1), this.backoffCapMs);
  }

  private static async drainBody(response: Response): Promise<void> {
    await response.text().catch(() => '');
  }

  /**
   * Issue an HTTP request with 429/key-suspension failover and bounded retry
   * (08 §3.5 / §3.6 — WP10). On the first throttle the request immediately
   * retries with the *other* API key (separate pool); once every key is
   * throttled it applies a short exponential backoff up to <c>maxRetries</c>
   * times, then throws <see cref="TronGridRateLimitError"/> so the monitor
   * re-polls on the next tick. 5xx errors retry on the same key (the provider,
   * not the key, is degraded). Other 4xx errors are non-retryable.
   */
  private async fetchResilient(
    url: string,
    init: RequestInit,
    endpointLabel: string,
  ): Promise<Response> {
    const keys = this.apiKeys();
    const baseHeaders = (init.headers ?? {}) as Record<string, string>;
    let keysTriedThisThrottle = 0;
    let backoffAttempts = 0;

    for (;;) {
      const apiKey = keys[this.keyIndex % keys.length];
      const headers: Record<string, string> = { ...baseHeaders };
      if (apiKey) {
        headers['TRON-PRO-API-KEY'] = apiKey;
      }

      const response = await this.fetchFn(url, { ...init, headers });
      if (response.ok) {
        return response;
      }

      const status = response.status;
      tronApiErrorsTotal.inc({ endpoint: endpointLabel, error_type: `http_${status}` });

      // 429 (rate limit) / 403 (key suspension) → fail over to the other key,
      // then short bounded backoff once every key is throttled.
      if (status === 429 || status === 403) {
        keysTriedThisThrottle += 1;
        await TronGridClient.drainBody(response);

        if (keys.length > 1 && keysTriedThisThrottle < keys.length) {
          this.keyIndex += 1; // immediate failover, no sleep (separate pool)
          tronApiErrorsTotal.inc({ endpoint: endpointLabel, error_type: 'key_failover' });
          continue;
        }

        this.keyIndex += 1; // rotate so the next request starts on the other key
        backoffAttempts += 1;
        if (backoffAttempts <= this.maxRetries) {
          logger.warn(
            { url, status, backoffAttempts, endpoint: endpointLabel },
            'TronGrid rate-limited on every key — backing off',
          );
          await this.sleepFn(this.backoffMs(backoffAttempts));
          keysTriedThisThrottle = 0; // allow failover again after the backoff
          continue;
        }
        throw new TronGridRateLimitError(status, response.statusText);
      }

      // 5xx provider error → retry the same key with bounded backoff.
      if (status >= 500 && status <= 599) {
        await TronGridClient.drainBody(response);
        backoffAttempts += 1;
        if (backoffAttempts <= this.maxRetries) {
          logger.warn(
            { url, status, backoffAttempts, endpoint: endpointLabel },
            'TronGrid 5xx — backing off',
          );
          await this.sleepFn(this.backoffMs(backoffAttempts));
          continue;
        }
        throw new TronGridHttpError(status, response.statusText);
      }

      // Other 4xx — non-retryable.
      const bodyText = await response.text().catch(() => '');
      logger.warn(
        { url, status, body: bodyText.slice(0, 256) },
        'TronGrid request failed (non-retryable)',
      );
      throw new TronGridHttpError(status, response.statusText);
    }
  }

  private async measure<T>(endpointLabel: string, fn: () => Promise<T>): Promise<T> {
    const stop = tronApiRequestDuration.startTimer({ endpoint: endpointLabel, status: 'ok' });
    try {
      const value = await fn();
      stop();
      return value;
    } catch (err) {
      tronApiRequestDuration.startTimer({ endpoint: endpointLabel, status: 'error' })();
      throw err;
    }
  }
}

export class TronGridHttpError extends Error {
  constructor(
    public readonly status: number,
    public readonly statusText: string,
  ) {
    super(`TronGrid HTTP ${status} ${statusText}`);
    this.name = 'TronGridHttpError';
  }
}

/**
 * Thrown when every TronGrid API key is rate-limited (429) / suspended (403)
 * and the bounded retry budget is exhausted (08 §3.5 / §3.6 — WP10). Subtype
 * of <see cref="TronGridHttpError"/> so existing `instanceof` checks still
 * match; the monitor treats it as a transient failure and re-polls next tick.
 */
export class TronGridRateLimitError extends TronGridHttpError {
  constructor(status: number, statusText: string) {
    super(status, statusText);
    this.name = 'TronGridRateLimitError';
  }
}
