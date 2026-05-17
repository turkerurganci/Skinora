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
 */
export class TronGridClient {
  constructor(
    private readonly fullNodeUrl: string = config.tronFullNodeUrl,
    private readonly solidityUrl: string = config.tronSolidityUrl,
    private readonly apiKey: string = config.tronApiKey,
    private readonly fetchFn: typeof fetch = fetch,
  ) {}

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

    const trx = typeof data.balance === 'number'
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

  private async getJson<T>(url: string, endpointLabel: string): Promise<T> {
    return this.measure(endpointLabel, async () => {
      const response = await this.fetchFn(url, {
        method: 'GET',
        headers: this.headers(),
      });
      return this.parseJsonResponse<T>(response, endpointLabel, url);
    });
  }

  private async postJson<T>(url: string, body: unknown, endpointLabel: string): Promise<T> {
    return this.measure(endpointLabel, async () => {
      const response = await this.fetchFn(url, {
        method: 'POST',
        headers: { ...this.headers(), 'content-type': 'application/json' },
        body: JSON.stringify(body),
      });
      return this.parseJsonResponse<T>(response, endpointLabel, url);
    });
  }

  private headers(): Record<string, string> {
    const headers: Record<string, string> = { accept: 'application/json' };
    if (this.apiKey) {
      headers['TRON-PRO-API-KEY'] = this.apiKey;
    }
    return headers;
  }

  private async parseJsonResponse<T>(
    response: Response,
    endpointLabel: string,
    url: string,
  ): Promise<T> {
    if (!response.ok) {
      tronApiErrorsTotal.inc({ endpoint: endpointLabel, error_type: `http_${response.status}` });
      const bodyText = await response.text().catch(() => '');
      logger.warn(
        { url, status: response.status, body: bodyText.slice(0, 256) },
        'TronGrid request failed',
      );
      throw new TronGridHttpError(response.status, response.statusText);
    }
    return (await response.json()) as T;
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
