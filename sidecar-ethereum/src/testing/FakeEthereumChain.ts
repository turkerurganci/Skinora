import { canonicalAddress } from '../address/canonicalAddress.js';
import { SELECTORS, TRANSFER_TOPIC, addressWord } from '../monitor/erc20.js';

/**
 * A small Ethereum chain behind a JSON-RPC `fetch` (tests only — T151).
 *
 * <para>
 * It interprets the queries instead of replaying canned answers, so a test
 * shows what the monitor's REAL requests return: `eth_getLogs` honours the
 * address list (absent = any contract), every topic position (null = any,
 * array = OR) and the block range, and refuses — with the messages the
 * providers measured in T142 use — a range wider than `maxBlockRange`, a list
 * longer than `maxAddresses` and a result longer than `maxResults`.
 * Balances are computed from the same transfers (`eth_call balanceOf` and
 * `eth_getBalance` at an explicit block), so a transfer can also be made
 * invisible to `eth_getLogs` while still moving the balance — a provider that
 * answers incompletely without an error (Tenderly, T142).
 * </para>
 *
 * <para>
 * `latest` and `finalized` are separate; {@link reorg} replaces blocks above
 * the finalized head (never at or below it).
 * </para>
 */

export interface FakeTransfer {
  contract: string;
  from: string;
  to: string;
  value: bigint;
  /** Extra topics after `to` (an ERC-721 token id) — not an ERC-20 transfer. */
  extraTopic?: string;
  /** Present in balances but never returned by eth_getLogs. */
  hiddenFromLogs?: boolean;
}

interface Block {
  number: bigint;
  hash: string;
  timestamp: number;
  logs: Array<{
    address: string;
    topics: string[];
    data: string;
    transactionHash: string;
    logIndex: number;
    hidden: boolean;
    transfer: FakeTransfer | null;
  }>;
  eth: Array<{ to: string; wei: bigint }>;
}

export interface FakeChainOptions {
  chainId?: number;
  maxBlockRange?: number;
  maxAddresses?: number;
  maxResults?: number;
  /** Block 0's timestamp (unix s); each block is 12 s later. */
  genesisTimestamp?: number;
}

export interface TokenSwitches {
  basisPointsRate?: bigint;
  maximumFee?: bigint;
  paused?: boolean;
  deprecated?: boolean;
}

export class FakeEthereumChain {
  readonly calls: Array<{ method: string; params: unknown[] }> = [];
  private readonly blocks = new Map<bigint, Block>();
  private latestNumber = 0n;
  private finalizedNumber = 0n;
  private readonly switches = new Map<string, TokenSwitches>();
  private readonly opts: Required<FakeChainOptions>;
  private txSeq = 0;
  private fork = 0;
  /** When set, every call fails at the transport level (a dead provider). */
  down = false;
  /** When set, eth_getLogs without `address` is refused the way Sepolia PublicNode refuses it (T151 probe). */
  refuseAddressless = false;

  constructor(options: FakeChainOptions = {}) {
    this.opts = {
      chainId: options.chainId ?? 11155111,
      maxBlockRange: options.maxBlockRange ?? 10_000,
      maxAddresses: options.maxAddresses ?? 1000,
      maxResults: options.maxResults ?? 10_000,
      genesisTimestamp: options.genesisTimestamp ?? 1_790_000_000,
    };
    this.blocks.set(0n, this.emptyBlock(0n));
  }

  get latest(): bigint {
    return this.latestNumber;
  }

  get finalized(): bigint {
    return this.finalizedNumber;
  }

  timestampOf(n: bigint): number {
    return this.opts.genesisTimestamp + Number(n) * 12;
  }

  private emptyBlock(n: bigint): Block {
    return {
      number: n,
      hash: '0x' + `${this.fork}`.padStart(8, '0') + n.toString(16).padStart(56, '0'),
      timestamp: this.timestampOf(n),
      logs: [],
      eth: [],
    };
  }

  /** Mines empty blocks up to <c>n</c>. */
  mineTo(n: bigint): void {
    while (this.latestNumber < n) {
      this.latestNumber++;
      this.blocks.set(this.latestNumber, this.emptyBlock(this.latestNumber));
    }
  }

  /** Mines one block holding these transfers (one transaction each) and returns its number and tx hashes. */
  mine(
    transfers: FakeTransfer[] = [],
    eth: Array<{ to: string; wei: bigint }> = [],
  ): {
    block: bigint;
    txHashes: string[];
  } {
    this.mineTo(this.latestNumber + 1n);
    const block = this.blocks.get(this.latestNumber)!;
    const txHashes: string[] = [];
    for (const t of transfers) {
      const txHash = '0x' + (++this.txSeq).toString(16).padStart(64, 'a');
      txHashes.push(txHash);
      this.addLog(block, t, txHash);
    }
    block.eth.push(...eth.map((e) => ({ to: canonicalAddress(e.to), wei: e.wei })));
    return { block: block.number, txHashes };
  }

  /** Several transfers inside ONE transaction (log indexes 0, 1, …) — the multi-event case. */
  mineOneTransaction(transfers: FakeTransfer[]): { block: bigint; txHash: string } {
    this.mineTo(this.latestNumber + 1n);
    const block = this.blocks.get(this.latestNumber)!;
    const txHash = '0x' + (++this.txSeq).toString(16).padStart(64, 'b');
    for (const t of transfers) this.addLog(block, t, txHash);
    return { block: block.number, txHash };
  }

  private addLog(block: Block, t: FakeTransfer, txHash: string): void {
    const topics = [TRANSFER_TOPIC, addressWord(t.from), addressWord(t.to)];
    if (t.extraTopic) topics.push(t.extraTopic);
    block.logs.push({
      address: canonicalAddress(t.contract),
      topics,
      data: t.extraTopic ? '0x' : '0x' + t.value.toString(16).padStart(64, '0'),
      transactionHash: txHash,
      logIndex: block.logs.length,
      hidden: t.hiddenFromLogs === true,
      transfer: t.extraTopic ? null : t,
    });
  }

  finalize(n: bigint): void {
    if (n > this.latestNumber) throw new Error('cannot finalize past latest');
    if (n < this.finalizedNumber) throw new Error('finality does not go back');
    this.finalizedNumber = n;
  }

  /** Drops every block from <c>from</c> on (all above finalized) — a reorg; mine again afterwards. */
  reorg(from: bigint): void {
    if (from <= this.finalizedNumber) throw new Error('a finalized block cannot be reorged');
    this.fork++;
    for (let n = from; n <= this.latestNumber; n++) this.blocks.delete(n);
    this.latestNumber = from - 1n;
  }

  setSwitches(contract: string, s: TokenSwitches): void {
    this.switches.set(canonicalAddress(contract), s);
  }

  tokenBalance(contract: string, holder: string, at: bigint): bigint {
    const c = canonicalAddress(contract);
    const h = canonicalAddress(holder);
    let total = 0n;
    for (let n = 0n; n <= at; n++) {
      for (const log of this.blocks.get(n)?.logs ?? []) {
        const t = log.transfer;
        if (!t || canonicalAddress(t.contract) !== c) continue;
        if (canonicalAddress(t.to) === h) total += t.value;
        if (canonicalAddress(t.from) === h) total -= t.value;
      }
    }
    return total;
  }

  ethBalance(holder: string, at: bigint): bigint {
    const h = canonicalAddress(holder);
    let total = 0n;
    for (let n = 0n; n <= at; n++) {
      for (const e of this.blocks.get(n)?.eth ?? []) if (e.to === h) total += e.wei;
    }
    return total;
  }

  count(method: string): number {
    return this.calls.filter((c) => c.method === method).length;
  }

  readonly fetchFn = (async (_input: string | URL | Request, init?: RequestInit) => {
    const body = JSON.parse(String(init?.body)) as {
      id: number;
      method: string;
      params: unknown[];
    };
    this.calls.push({ method: body.method, params: body.params });
    if (this.down) {
      throw Object.assign(new TypeError('fetch failed'), { cause: { code: 'ECONNREFUSED' } });
    }
    try {
      return json({ id: body.id, result: this.answer(body.method, body.params) });
    } catch (err) {
      if (err instanceof RpcFault)
        return json({ id: body.id, error: { code: err.code, message: err.message } });
      throw err;
    }
  }) as typeof fetch;

  private blockParam(p: unknown): bigint {
    if (p === 'latest') return this.latestNumber;
    if (p === 'finalized') return this.finalizedNumber;
    if (typeof p === 'string' && p.startsWith('0x')) return BigInt(p);
    throw new RpcFault(-32602, `bad block param ${String(p)}`);
  }

  private answer(method: string, params: unknown[]): unknown {
    switch (method) {
      case 'eth_chainId':
        return '0x' + this.opts.chainId.toString(16);
      case 'eth_getBlockByNumber': {
        const n = this.blockParam(params[0]);
        const b = this.blocks.get(n);
        if (!b) return null;
        return {
          number: '0x' + n.toString(16),
          hash: b.hash,
          timestamp: '0x' + b.timestamp.toString(16),
        };
      }
      case 'eth_getBalance': {
        const at = this.blockParam(params[1]);
        if (at > this.latestNumber) throw new RpcFault(-32000, 'header not found');
        return '0x' + this.ethBalance(String(params[0]), at).toString(16);
      }
      case 'eth_call':
        return this.call(params[0] as { to: string; data: string }, this.blockParam(params[1]));
      case 'eth_getLogs':
        return this.getLogs(params[0] as Record<string, unknown>);
      default:
        throw new RpcFault(-32601, `method ${method} not supported by the fake`);
    }
  }

  private call(tx: { to: string; data: string }, at: bigint): string {
    if (at > this.latestNumber) throw new RpcFault(-32000, 'header not found');
    const selector = tx.data.slice(0, 10);
    const s = this.switches.get(canonicalAddress(tx.to)) ?? {};
    const word = (v: bigint) => '0x' + v.toString(16).padStart(64, '0');
    switch (selector) {
      case SELECTORS.balanceOf:
        return word(this.tokenBalance(tx.to, '0x' + tx.data.slice(34, 74), at));
      case SELECTORS.basisPointsRate:
        return word(s.basisPointsRate ?? 0n);
      case SELECTORS.maximumFee:
        return word(s.maximumFee ?? 0n);
      case SELECTORS.paused:
        return word(s.paused ? 1n : 0n);
      case SELECTORS.deprecated:
        return word(s.deprecated ? 1n : 0n);
      case SELECTORS.decimals:
        return word(6n);
      default:
        throw new RpcFault(3, 'execution reverted');
    }
  }

  private getLogs(filter: Record<string, unknown>): unknown[] {
    const from = this.blockParam(filter.fromBlock);
    const to = this.blockParam(filter.toBlock);
    if (to - from + 1n > BigInt(this.opts.maxBlockRange)) {
      throw new RpcFault(-32600, `exceed maximum block range: ${this.opts.maxBlockRange}`);
    }
    if (to > this.latestNumber) throw new RpcFault(-32000, 'unknown block');
    const addressFilter = filter.address;
    if (addressFilter === undefined && this.refuseAddressless) {
      throw new RpcFault(
        -32701,
        'Please specify an address in your request or, to remove restrictions, order a dedicated full node here: https://www.allnodes.com/eth/host',
      );
    }
    const addresses =
      addressFilter === undefined
        ? null
        : (Array.isArray(addressFilter) ? addressFilter : [addressFilter]).map((a) =>
            canonicalAddress(String(a)),
          );
    const topics = (filter.topics as Array<string | string[] | null> | undefined) ?? [];
    for (const position of [addresses, ...topics]) {
      if (Array.isArray(position) && position.length > this.opts.maxAddresses) {
        throw new RpcFault(
          -32602,
          `query exceeds the maximum of ${this.opts.maxAddresses} addresses or topics per search position`,
        );
      }
    }
    const out: unknown[] = [];
    for (let n = from; n <= to; n++) {
      const b = this.blocks.get(n);
      if (!b) continue;
      for (const log of b.logs) {
        if (log.hidden) continue;
        if (addresses && !addresses.includes(log.address)) continue;
        const matches = topics.every((t, i) => {
          if (t === null || t === undefined) return true;
          const actual = log.topics[i]?.toLowerCase();
          return Array.isArray(t)
            ? t.map((x) => x.toLowerCase()).includes(actual ?? '')
            : t.toLowerCase() === actual;
        });
        if (!matches) continue;
        out.push({
          address: log.address,
          topics: log.topics,
          data: log.data,
          blockNumber: '0x' + n.toString(16),
          blockHash: b.hash,
          transactionHash: log.transactionHash,
          logIndex: '0x' + log.logIndex.toString(16),
          removed: false,
        });
        if (out.length > this.opts.maxResults) {
          throw new RpcFault(-32005, `query returned more than ${this.opts.maxResults} results`);
        }
      }
    }
    return out;
  }
}

class RpcFault extends Error {
  constructor(
    public readonly code: number,
    message: string,
  ) {
    super(message);
  }
}

function json(body: unknown): Response {
  return new Response(JSON.stringify({ jsonrpc: '2.0', ...(body as object) }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
}
