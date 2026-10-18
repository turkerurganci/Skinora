import { SidecarError } from '../errors/SidecarError.js';
import type { RpcProviderSettings } from '../config/index.js';
import { logger } from '../logger.js';
import { rpcFailoverTotal, rpcRequestDuration } from '../metrics.js';

/**
 * Ethereum JSON-RPC client with a primary and a backup provider (T150 —
 * 08 §3.7.1, §3.7.4, §3.7.8).
 *
 * <para>
 * <b>Why not ethers' providers.</b> ethers (used here for keys, addresses and,
 * from T152 on, signing) ships `JsonRpcProvider` / `FallbackProvider`, but the
 * fallback provider answers by quorum and hides which provider answered, the
 * plain provider batches calls (some free plans reject batches) and polls the
 * network on its own. The rules this service needs are narrower and must be
 * visible: try the primary, fall over to the backup on a transport failure
 * only, never read state at an implicit block, split `eth_getLogs` by each
 * provider's own limits. A thin `fetch` transport states them in ~300 lines
 * and keeps the dependency surface at ethers alone.
 * </para>
 *
 * <para>
 * <b>Explicit block numbers.</b> State reads (`getBalance`, `call`,
 * `getLogs`) take a `bigint` block number — there is no way to ask them for
 * `latest`. The only tags the client sends are on `getBlockHeader`, which is
 * how a caller learns the number of the `finalized` (or `latest`) block in the
 * first place (K5: payments are read at `finalized` only).
 * </para>
 *
 * <para>
 * <b>Failover.</b> A provider that fails at the transport level (connection,
 * timeout, HTTP 429/5xx, `-32005` rate limit, archive refusal) is skipped for
 * `failoverCooldownMs` and the next one answers; a JSON-RPC error that is an
 * answer about the request (e.g. `execution reverted`) is returned as is — the
 * other provider would give the same answer. A provider whose `eth_chainId`
 * is not the configured chain is never used.
 * </para>
 */

export type RpcFailureKind = 'transport' | 'rpc' | 'limit' | 'chain_mismatch';

export class RpcError extends SidecarError {
  constructor(
    message: string,
    public readonly kind: RpcFailureKind,
    public readonly provider: string,
    public readonly rpcCode?: number,
  ) {
    super(
      message,
      kind === 'chain_mismatch'
        ? 'RPC_CHAIN_MISMATCH'
        : kind === 'rpc'
          ? 'RPC_ERROR'
          : 'RPC_UNAVAILABLE',
      kind !== 'chain_mismatch' && kind !== 'rpc',
    );
    this.name = 'RpcError';
  }
}

export class AllProvidersFailedError extends SidecarError {
  constructor(
    method: string,
    public readonly failures: readonly RpcError[],
  ) {
    super(
      `${method}: no RPC provider could answer (${failures.map((f) => `${f.provider}: ${f.message}`).join('; ')})`,
      'RPC_ALL_PROVIDERS_FAILED',
      true,
    );
    this.name = 'AllProvidersFailedError';
  }
}

export type BlockTag = 'finalized' | 'latest';

export interface BlockHeader {
  readonly number: bigint;
  readonly hash: string;
  /** Unix seconds. */
  readonly timestamp: number;
  /** The provider that answered. */
  readonly provider: string;
}

export interface LogFilter {
  /**
   * Emitting contract(s). Omitted = any contract (T151: the payment monitor
   * reads every ERC-20 Transfer TO a deposit, whichever token sent it — the
   * wrong-token and spam sweep of 08 §3.7.4).
   */
  readonly address?: string | readonly string[];
  readonly topics: ReadonlyArray<string | readonly string[] | null>;
  readonly fromBlock: bigint;
  readonly toBlock: bigint;
}

export interface RpcLog {
  readonly address: string;
  readonly topics: readonly string[];
  readonly data: string;
  readonly blockNumber: bigint;
  readonly blockHash: string;
  readonly transactionHash: string;
  readonly logIndex: number;
  readonly removed: boolean;
}

export interface EthereumRpcClientDeps {
  providers: readonly RpcProviderSettings[];
  chainId: number;
  timeoutMs: number;
  failoverCooldownMs: number;
  fetchFn?: typeof fetch;
  now?: () => number;
}

// Messages the providers measured in T142 return when a request is too large
// for their plan rather than wrong (08 §3.7.4 "Sorgu", §3.7.8).
const LIMIT_PATTERNS = [
  /block range/i,
  /blocks range/i,
  /ranges? over \d+ blocks/i,
  /maximum of \d+ addresses/i,
  /more than \d+ results/i,
  /response size/i,
  /too many (results|logs)/i,
];
const RATE_OR_ARCHIVE_PATTERNS = [/rate limit/i, /too many requests/i, /archive/i, /timeout/i];

function hexBlock(block: bigint): string {
  return '0x' + block.toString(16);
}

function assertBlockNumber(block: unknown, what: string): asserts block is bigint {
  if (typeof block !== 'bigint' || block < 0n) {
    throw new SidecarError(
      `${what} must be an explicit block number (bigint >= 0); block tags are not accepted for state reads.`,
      'RPC_IMPLICIT_BLOCK',
      false,
    );
  }
}

class ProviderEndpoint {
  private seq = 0;

  constructor(
    readonly settings: RpcProviderSettings,
    private readonly fetchFn: typeof fetch,
    private readonly timeoutMs: number,
  ) {}

  get name(): string {
    return this.settings.name;
  }

  async request<T>(method: string, params: unknown[]): Promise<T> {
    const stop = rpcRequestDuration.startTimer({ provider: this.name, method });
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.timeoutMs);
    let outcome: RpcFailureKind | 'ok' = 'transport';
    try {
      let response: Response;
      try {
        response = await this.fetchFn(this.settings.url, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ jsonrpc: '2.0', id: ++this.seq, method, params }),
          signal: controller.signal,
        });
      } catch (err) {
        // The URL is never part of the message: it carries the API key.
        const cause = (err as { cause?: { code?: string } }).cause?.code;
        const reason = controller.signal.aborted
          ? `timed out after ${this.timeoutMs} ms`
          : (cause ?? (err as Error).name);
        throw new RpcError(`${method} transport failure: ${reason}`, 'transport', this.name);
      }

      const text = await response.text();
      if (!response.ok) {
        throw new RpcError(
          `${method} HTTP ${response.status}: ${text.slice(0, 200)}`,
          'transport',
          this.name,
        );
      }
      let body: { result?: T; error?: { code?: number; message?: string } };
      try {
        body = JSON.parse(text) as typeof body;
      } catch {
        throw new RpcError(`${method} returned a non-JSON body`, 'transport', this.name);
      }
      if (body.error) {
        const message = body.error.message ?? 'unknown error';
        const kind: RpcFailureKind = LIMIT_PATTERNS.some((p) => p.test(message))
          ? 'limit'
          : body.error.code === -32005 || RATE_OR_ARCHIVE_PATTERNS.some((p) => p.test(message))
            ? 'transport'
            : 'rpc';
        outcome = kind;
        throw new RpcError(`${method}: ${message}`, kind, this.name, body.error.code);
      }
      if (!('result' in body)) {
        throw new RpcError(
          `${method} response has neither result nor error`,
          'transport',
          this.name,
        );
      }
      outcome = 'ok';
      return body.result as T;
    } finally {
      clearTimeout(timer);
      stop({ outcome });
    }
  }
}

export class EthereumRpcClient {
  private readonly endpoints: ProviderEndpoint[];
  private readonly chainId: number;
  private readonly failoverCooldownMs: number;
  private readonly now: () => number;
  private readonly cooldownUntil = new Map<string, number>();
  private readonly chainChecks = new Map<string, Promise<void>>();

  constructor(deps: EthereumRpcClientDeps) {
    if (deps.providers.length === 0) {
      throw new Error('EthereumRpcClient needs at least one provider.');
    }
    const fetchFn = deps.fetchFn ?? fetch;
    this.endpoints = deps.providers.map((p) => new ProviderEndpoint(p, fetchFn, deps.timeoutMs));
    this.chainId = deps.chainId;
    this.failoverCooldownMs = deps.failoverCooldownMs;
    this.now = deps.now ?? (() => Date.now());
  }

  /** Provider names in priority order (primary first). */
  get providerNames(): string[] {
    return this.endpoints.map((e) => e.name);
  }

  /** The `finalized` / `latest` block, or an explicit one (08 §3.7.4). */
  async getBlockHeader(block: BlockTag | bigint): Promise<BlockHeader> {
    const param = typeof block === 'bigint' ? hexBlock(block) : block;
    if (typeof block !== 'bigint' && block !== 'finalized' && block !== 'latest') {
      throw new SidecarError(
        `Unsupported block tag ${String(block)}.`,
        'RPC_IMPLICIT_BLOCK',
        false,
      );
    }
    return this.withFailover('eth_getBlockByNumber', async (ep) => {
      const raw = await ep.request<{ number: string; hash: string; timestamp: string } | null>(
        'eth_getBlockByNumber',
        [param, false],
      );
      if (!raw) {
        throw new RpcError(`eth_getBlockByNumber(${param}) returned no block`, 'rpc', ep.name);
      }
      return {
        number: BigInt(raw.number),
        hash: raw.hash,
        timestamp: Number(BigInt(raw.timestamp)),
        provider: ep.name,
      };
    });
  }

  /** Native ETH balance at an explicit block (wei). */
  async getBalance(address: string, blockNumber: bigint): Promise<bigint> {
    assertBlockNumber(blockNumber, 'getBalance block');
    return this.withFailover('eth_getBalance', async (ep) =>
      BigInt(await ep.request<string>('eth_getBalance', [address, hexBlock(blockNumber)])),
    );
  }

  /** `eth_call` at an explicit block; returns the raw return data. */
  async call(tx: { to: string; data: string }, blockNumber: bigint): Promise<string> {
    assertBlockNumber(blockNumber, 'call block');
    return this.withFailover('eth_call', (ep) =>
      ep.request<string>('eth_call', [{ to: tx.to, data: tx.data }, hexBlock(blockNumber)]),
    );
  }

  /**
   * `eth_estimateGas` of a call, evaluated at an explicit block (T154). No
   * gas price is sent, so the node does not check the sender's ETH balance —
   * a deposit is priced before the fuel wallet has funded it (08 §3.7.5).
   * A revert is an `rpc` error (an answer about the request, not failed over).
   */
  async estimateGas(
    tx: { from: string; to: string; data: string },
    blockNumber: bigint,
  ): Promise<bigint> {
    assertBlockNumber(blockNumber, 'estimateGas block');
    return this.withFailover('eth_estimateGas', async (ep) =>
      parseQuantity(
        await ep.request<string>('eth_estimateGas', [
          { from: tx.from, to: tx.to, data: tx.data },
          hexBlock(blockNumber),
        ]),
        'eth_estimateGas',
        ep.name,
      ),
    );
  }

  /**
   * `eth_feeHistory` ending at an explicit block (T154). `baseFeePerGas` has
   * `blockCount + 1` entries — the last is the base fee of the block after
   * `newestBlock`; `reward[i][j]` is block i's priority fee at percentile j.
   */
  async feeHistory(
    blockCount: number,
    newestBlock: bigint,
    rewardPercentiles: readonly number[],
  ): Promise<FeeHistory> {
    assertBlockNumber(newestBlock, 'feeHistory newestBlock');
    return this.withFailover('eth_feeHistory', async (ep) => {
      const raw = await ep.request<{ baseFeePerGas?: unknown; reward?: unknown } | null>(
        'eth_feeHistory',
        ['0x' + blockCount.toString(16), hexBlock(newestBlock), rewardPercentiles],
      );
      if (!raw || !Array.isArray(raw.baseFeePerGas) || raw.baseFeePerGas.length === 0) {
        throw new RpcError('eth_feeHistory returned no baseFeePerGas', 'transport', ep.name);
      }
      const reward = Array.isArray(raw.reward) ? raw.reward : [];
      return {
        baseFeePerGas: raw.baseFeePerGas.map((v) => parseQuantity(v, 'eth_feeHistory', ep.name)),
        reward: reward.map((row: unknown) =>
          (Array.isArray(row) ? row : []).map((v) => parseQuantity(v, 'eth_feeHistory', ep.name)),
        ),
      };
    });
  }

  /**
   * `eth_getLogs` over an explicit, inclusive block range. Split so that no
   * single request exceeds the answering provider's block range or its
   * per-position list limit (08 §3.7.4: Alchemy Free 10 blocks, Chainstack
   * Developer 100; 1,000 addresses). A request the provider still refuses as
   * too large is halved and retried; a provider that fails over hands the
   * rest of the range to the next one, which applies its own limits.
   */
  async getLogs(filter: LogFilter): Promise<RpcLog[]> {
    assertBlockNumber(filter.fromBlock, 'getLogs fromBlock');
    assertBlockNumber(filter.toBlock, 'getLogs toBlock');
    if (filter.fromBlock > filter.toBlock) {
      throw new SidecarError('getLogs fromBlock is after toBlock.', 'RPC_INVALID_RANGE', false);
    }

    const listLimit = Math.min(...this.endpoints.map((e) => e.settings.maxAddressesPerQuery));
    const logs: RpcLog[] = [];
    for (const part of splitLists(filter, listLimit)) {
      let from = filter.fromBlock;
      while (from <= filter.toBlock) {
        const { batch, end } = await this.withFailover('eth_getLogs', (ep) =>
          this.logsChunk(ep, part, from, filter.toBlock),
        );
        logs.push(...batch);
        from = end + 1n;
      }
    }
    return logs;
  }

  private async logsChunk(
    ep: ProviderEndpoint,
    part: { address?: string | readonly string[]; topics: LogFilter['topics'] },
    from: bigint,
    to: bigint,
  ): Promise<{ batch: RpcLog[]; end: bigint }> {
    let span = BigInt(ep.settings.maxBlockRange);
    for (;;) {
      const end = from + span - 1n < to ? from + span - 1n : to;
      try {
        const raw = await ep.request<RawLog[]>('eth_getLogs', [
          {
            // An absent `address` is left out of the request, not sent as
            // null: some providers reject a null address.
            ...(part.address === undefined ? {} : { address: part.address }),
            topics: part.topics,
            fromBlock: hexBlock(from),
            toBlock: hexBlock(end),
          },
        ]);
        return { batch: raw.map(parseLog), end };
      } catch (err) {
        if (err instanceof RpcError && err.kind === 'limit' && end > from) {
          span = (end - from + 1n) / 2n;
          logger.warn(
            { provider: ep.name, from: String(from), span: String(span) },
            'eth_getLogs refused as too large — halving the block range (check the provider limit setting)',
          );
          continue;
        }
        throw err;
      }
    }
  }

  private async withFailover<T>(
    method: string,
    fn: (ep: ProviderEndpoint) => Promise<T>,
  ): Promise<T> {
    const now = this.now();
    // Providers not cooling down first, in configured order; cooled ones last.
    const ordered = [
      ...this.endpoints.filter((e) => (this.cooldownUntil.get(e.name) ?? 0) <= now),
      ...this.endpoints.filter((e) => (this.cooldownUntil.get(e.name) ?? 0) > now),
    ];
    const failures: RpcError[] = [];
    for (let i = 0; i < ordered.length; i++) {
      const ep = ordered[i];
      try {
        await this.ensureChain(ep);
        const result = await fn(ep);
        this.cooldownUntil.delete(ep.name);
        return result;
      } catch (err) {
        if (!(err instanceof RpcError)) throw err;
        // An answer about the request itself — the next provider would agree.
        if (err.kind === 'rpc') throw err;
        failures.push(err);
        this.cooldownUntil.set(ep.name, this.now() + this.failoverCooldownMs);
        const next = ordered[i + 1];
        if (next) rpcFailoverTotal.inc({ from: ep.name, method });
        logger.warn(
          {
            provider: ep.name,
            method,
            kind: err.kind,
            reason: err.message,
            next: next?.name ?? null,
          },
          next ? 'RPC provider failed — falling over' : 'RPC provider failed — no provider left',
        );
      }
    }
    throw new AllProvidersFailedError(method, failures);
  }

  /** Each provider's chain is checked once; a wrong chain disables the provider for good. */
  private ensureChain(ep: ProviderEndpoint): Promise<void> {
    const existing = this.chainChecks.get(ep.name);
    if (existing) return existing;
    const check = (async () => {
      const raw = await ep.request<string>('eth_chainId', []);
      const reported = Number(BigInt(raw));
      if (reported !== this.chainId) {
        logger.error(
          { provider: ep.name, reported, expected: this.chainId },
          'RPC provider is on another chain — it will not be used',
        );
        throw new RpcError(
          `provider is on chain ${reported}, expected ${this.chainId}`,
          'chain_mismatch',
          ep.name,
        );
      }
    })();
    this.chainChecks.set(ep.name, check);
    // A transport failure is not an answer: forget it so the next call re-checks.
    check.catch((err: unknown) => {
      if (!(err instanceof RpcError) || err.kind !== 'chain_mismatch') {
        this.chainChecks.delete(ep.name);
      }
    });
    return check;
  }
}

interface RawLog {
  address: string;
  topics: string[];
  data: string;
  blockNumber: string;
  blockHash: string;
  transactionHash: string;
  logIndex: string;
  removed?: boolean;
}

function parseLog(raw: RawLog): RpcLog {
  return {
    address: raw.address,
    topics: raw.topics,
    data: raw.data,
    blockNumber: BigInt(raw.blockNumber),
    blockHash: raw.blockHash,
    transactionHash: raw.transactionHash,
    logIndex: Number(BigInt(raw.logIndex)),
    removed: raw.removed === true,
  };
}

/** `eth_feeHistory`, decoded to bigints (T154). */
export interface FeeHistory {
  readonly baseFeePerGas: readonly bigint[];
  readonly reward: ReadonlyArray<readonly bigint[]>;
}

/**
 * A JSON-RPC quantity (`0x`-prefixed hex) → bigint. Anything else is a
 * malformed answer from that provider (transport kind, so the backup is
 * asked) — never a number that silently became 0 or lost precision.
 */
function parseQuantity(value: unknown, method: string, provider: string): bigint {
  if (typeof value !== 'string' || !/^0x[0-9a-fA-F]+$/.test(value)) {
    throw new RpcError(
      `${method} returned a malformed quantity ${JSON.stringify(value)}`,
      'transport',
      provider,
    );
  }
  return BigInt(value);
}

function chunk<T>(items: readonly T[], size: number): T[][] {
  const out: T[][] = [];
  for (let i = 0; i < items.length; i += size) out.push(items.slice(i, i + size));
  return out;
}

/**
 * Splits every list position (the contract address list and each topic
 * position) longer than <c>limit</c> into parts of at most <c>limit</c>, and
 * returns the cross product — each returned filter is within the limit at
 * every position and together they match exactly what the original matched.
 */
function splitLists(
  filter: LogFilter,
  limit: number,
): Array<{ address?: string | readonly string[]; topics: LogFilter['topics'] }> {
  const addressParts =
    Array.isArray(filter.address) && filter.address.length > limit
      ? chunk(filter.address as readonly string[], limit)
      : [filter.address];
  let combos: Array<{ address?: string | readonly string[]; topics: LogFilter['topics'] }> =
    addressParts.map((address) => ({ address, topics: [] as LogFilter['topics'] }));
  filter.topics.forEach((position) => {
    const parts =
      Array.isArray(position) && position.length > limit
        ? chunk(position as readonly string[], limit)
        : [position];
    combos = combos.flatMap((c) =>
      parts.map((p) => ({ address: c.address, topics: [...c.topics, p] })),
    );
  });
  return combos;
}
