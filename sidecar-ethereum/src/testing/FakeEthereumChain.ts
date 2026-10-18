import { AbiCoder, Transaction } from 'ethers';
import { canonicalAddress } from '../address/canonicalAddress.js';
import { SELECTORS, TRANSFER_TOPIC, addressWord } from '../monitor/erc20.js';
import {
  RpcError,
  type BlockHeader,
  type RpcLog,
  type TransactionReceipt,
} from '../rpc/RpcClient.js';
import {
  TOKEN_ABI,
  TRANSFER_TOPIC as SEND_TRANSFER_TOPIC,
  addressTopic,
  decodeTransferCall,
} from '../transfer/erc20.js';

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
  /** Rewrites every eth_getLogs answer — a provider that returns removed or out-of-range logs (tests only). */
  tamperLogs: ((logs: Array<Record<string, unknown>>) => Array<Record<string, unknown>>) | null =
    null;

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
    return this.tamperLogs ? this.tamperLogs(out as Array<Record<string, unknown>>) : out;
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

/**
 * An in-memory Ethereum for the send tests (T152) — {@link FakeEthereumChain}'s
 * sibling: that one answers JSON-RPC over `fetch` for the payment monitor
 * (T151); this one is the RpcClient surface the send path calls directly: accounts with nonces, ETH
 * and token balances, a mempool with EIP-1559 replacement rules, blocks that
 * include the best transaction per (sender, nonce), receipts with `Transfer`
 * logs, a USDT contract with its pause / deprecate / fee switches and
 * blacklist (sender-side revert only — T142's measurement), and finality.
 *
 * The rules that matter for double payment are the chain's, not the
 * service's: one transaction per (sender, nonce) can ever be mined, a
 * replacement needs +10 % on both fee fields, a re-sent known transaction is
 * "already known", a used nonce is "nonce too low".
 */
export class FakeSendChain {
  readonly chainId: bigint;
  head = 1_000n;
  finalized = 900n;
  baseFee = 1_000_000_000n;
  tip = 50_000_000n;
  estimate = 46_201n;
  usdtState = { deprecated: false, paused: false, bps: 0n, maxFee: 0n };
  readonly blacklist = new Set<string>();
  readonly tokenBalances = new Map<string, Map<string, bigint>>();
  readonly ethBalances = new Map<string, bigint>();
  readonly nonces = new Map<string, number>();
  readonly mempool = new Map<string, Transaction>();
  readonly receipts = new Map<string, TransactionReceipt>();
  readonly logs: RpcLog[] = [];
  /** Every raw transaction handed to sendRawTransaction, in order. */
  readonly sent: string[] = [];
  /** Errors to throw on the next sendRawTransaction calls (before the node sees the bytes). */
  readonly sendFailures: Error[] = [];
  /** Called with the raw bytes when a broadcast arrives (before failures apply). */
  onSend?: (raw: string) => void;
  /** When true, every RPC call fails at the transport level. */
  down = false;
  /** USDC's simulated `transfer` answers false (a token that refuses without reverting). */
  usdcReturnsFalse = false;
  /** Errors the next estimateGas / callFrom / call (USDT state reads) throw. */
  readonly simulationFailures: Error[] = [];
  readonly stateReadFailures: Error[] = [];
  /** Receipt fields of the next mined transactions (T154: realized fee). */
  receiptGasUsed = 41_273n;
  receiptEffectiveGasPrice: bigint | null = null;
  /** When true, mined token transfers leave no Transfer log (a forwarding / non-moving token). */
  dropTransferLogs = false;
  private readonly blockHashes = new Map<bigint, string>();

  constructor(
    readonly usdt: string,
    readonly usdc: string,
    chainId = 11_155_111n,
  ) {
    this.chainId = chainId;
    this.usdt = canonicalAddress(usdt);
    this.usdc = canonicalAddress(usdc);
  }

  blockHash(n: bigint): string {
    return this.blockHashes.get(n) ?? '0x' + n.toString(16).padStart(64, '0');
  }

  /** Re-org helper: block n now has a different hash. */
  replaceBlock(n: bigint): void {
    this.blockHashes.set(n, '0x' + 'f'.repeat(48) + n.toString(16).padStart(16, '0'));
  }

  setToken(contract: string, address: string, units: bigint): void {
    const c = canonicalAddress(contract);
    if (!this.tokenBalances.has(c)) this.tokenBalances.set(c, new Map());
    this.tokenBalances.get(c)!.set(canonicalAddress(address), units);
  }

  token(contract: string, address: string): bigint {
    return this.tokenBalances.get(canonicalAddress(contract))?.get(canonicalAddress(address)) ?? 0n;
  }

  setEth(address: string, wei: bigint): void {
    this.ethBalances.set(canonicalAddress(address), wei);
  }

  nonce(address: string): number {
    return this.nonces.get(canonicalAddress(address)) ?? 0;
  }

  /** A transaction this service did not sign uses the account's next nonce. */
  foreignSend(address: string): void {
    this.nonces.set(canonicalAddress(address), this.nonce(address) + 1);
    this.head += 1n;
  }

  /** Pending count the node reports beyond the mined one (a foreign transaction in the mempool). */
  readonly pendingForeign = new Map<string, number>();
  foreignPending(address: string): void {
    const from = canonicalAddress(address);
    this.pendingForeign.set(
      from,
      Math.max(this.pendingForeign.get(from) ?? 0, this.nonce(from)) + 1,
    );
  }

  /** Mines one block: per sender, the best-priced transaction at its next nonce. */
  mine(): string[] {
    const block = this.head + 1n;
    const mined: string[] = [];
    const bySender = new Map<string, Transaction[]>();
    for (const tx of this.mempool.values()) {
      const from = canonicalAddress(tx.from ?? '');
      if (!bySender.has(from)) bySender.set(from, []);
      bySender.get(from)!.push(tx);
    }
    for (const [from, txs] of bySender) {
      for (;;) {
        const next = this.nonce(from);
        const candidates = txs.filter((t) => t.nonce === next);
        if (candidates.length === 0) break;
        candidates.sort((a, b) => ((b.maxFeePerGas ?? 0n) > (a.maxFeePerGas ?? 0n) ? 1 : -1));
        const tx = candidates[0];
        if ((tx.maxFeePerGas ?? 0n) < this.baseFee) break; // priced out; waits
        mined.push(this.execute(tx, block));
        for (const t of candidates) this.mempool.delete((t.hash ?? '').toLowerCase());
        this.nonces.set(from, next + 1);
      }
    }
    this.head = block;
    return mined;
  }

  private execute(tx: Transaction, block: bigint): string {
    const hash = (tx.hash ?? '').toLowerCase();
    const from = canonicalAddress(tx.from ?? '');
    const to = canonicalAddress(tx.to ?? '');
    const logs: RpcLog[] = [];
    let status = 1;
    const call = decodeTransferCall(tx.data);
    if (call && (to === this.usdt || to === this.usdc)) {
      const balance = this.token(to, from);
      const blocked = to === this.usdt && (this.blacklist.has(from) || this.usdtState.paused);
      if (blocked || balance < call.amountUnits) {
        status = 0;
      } else {
        this.setToken(to, from, balance - call.amountUnits);
        this.setToken(to, call.to, this.token(to, call.to) + call.amountUnits);
        const log: RpcLog = {
          address: to,
          topics: [SEND_TRANSFER_TOPIC, addressTopic(from), addressTopic(call.to)],
          data: '0x' + call.amountUnits.toString(16).padStart(64, '0'),
          blockNumber: block,
          blockHash: this.blockHash(block),
          transactionHash: hash,
          logIndex: this.logs.length,
          removed: false,
        };
        if (!this.dropTransferLogs) {
          logs.push(log);
          this.logs.push(log);
        }
      }
    } else if (tx.data === '0x' && tx.value > 0n) {
      this.setEth(to, (this.ethBalances.get(to) ?? 0n) + tx.value);
    }
    this.receipts.set(hash, {
      transactionHash: hash,
      blockNumber: block,
      blockHash: this.blockHash(block),
      status,
      from,
      to,
      gasUsed: this.receiptGasUsed,
      effectiveGasPrice: this.receiptEffectiveGasPrice ?? this.baseFee + this.tip,
      logs,
    });
    return hash;
  }

  private guard(): void {
    if (this.down) throw new RpcError('fake transport failure', 'transport', 'fake');
  }

  private revertIfBlocked(from: string, to: string, data: string): void {
    const call = decodeTransferCall(data);
    if (!call) throw new RpcError('execution reverted', 'rpc', 'fake');
    const contract = canonicalAddress(to);
    const usdtBlocked =
      contract === this.usdt &&
      (this.blacklist.has(canonicalAddress(from)) || this.usdtState.paused);
    if (usdtBlocked || this.token(contract, from) < call.amountUnits) {
      throw new RpcError('execution reverted', 'rpc', 'fake');
    }
  }

  /** The RpcClient surface the send path uses. */
  readonly rpc = {
    getBlockHeader: async (block: 'finalized' | 'latest' | bigint): Promise<BlockHeader> => {
      this.guard();
      const n = block === 'latest' ? this.head : block === 'finalized' ? this.finalized : block;
      return {
        number: n,
        hash: this.blockHash(n),
        timestamp: Number(n) * 12,
        provider: 'fake',
        baseFeePerGas: this.baseFee,
      };
    },
    getBalance: async (address: string, _block: bigint): Promise<bigint> => {
      this.guard();
      return this.ethBalances.get(canonicalAddress(address)) ?? 0n;
    },
    call: async (tx: { to: string; data: string }, _block: bigint): Promise<string> => {
      this.guard();
      const stateFailure = this.stateReadFailures.shift();
      if (stateFailure) throw stateFailure;
      const parsed = TOKEN_ABI.parseTransaction({ data: tx.data });
      const coder = AbiCoder.defaultAbiCoder();
      switch (parsed?.name) {
        case 'deprecated':
          return coder.encode(['bool'], [this.usdtState.deprecated]);
        case 'paused':
          return coder.encode(['bool'], [this.usdtState.paused]);
        case 'basisPointsRate':
          return coder.encode(['uint256'], [this.usdtState.bps]);
        case 'maximumFee':
          return coder.encode(['uint256'], [this.usdtState.maxFee]);
        case 'isBlackListed':
          return coder.encode(
            ['bool'],
            [this.blacklist.has(canonicalAddress(parsed.args[0] as string))],
          );
        default:
          throw new RpcError('execution reverted', 'rpc', 'fake');
      }
    },
    callFrom: async (
      tx: { from: string; to: string; data: string },
      _block: bigint,
    ): Promise<string> => {
      this.guard();
      const failure = this.simulationFailures.shift();
      if (failure) throw failure;
      this.revertIfBlocked(tx.from, tx.to, tx.data);
      return canonicalAddress(tx.to) === this.usdc
        ? AbiCoder.defaultAbiCoder().encode(['bool'], [!this.usdcReturnsFalse])
        : '0x';
    },
    estimateGas: async (
      tx: { from: string; to: string; data: string },
      _block: bigint,
    ): Promise<bigint> => {
      this.guard();
      const failure = this.simulationFailures.shift();
      if (failure) throw failure;
      this.revertIfBlocked(tx.from, tx.to, tx.data);
      return this.estimate;
    },
    getTransactionCount: async (address: string, _block: bigint): Promise<number> => {
      this.guard();
      return this.nonce(address);
    },
    /** Mined count, or one past the highest consecutive nonce of the sender in the mempool. */
    getPendingTransactionCount: async (address: string): Promise<number> => {
      this.guard();
      const from = canonicalAddress(address);
      let next = this.nonce(from);
      const pending = new Set(
        [...this.mempool.values()]
          .filter((t) => canonicalAddress(t.from ?? '') === from)
          .map((t) => t.nonce),
      );
      while (pending.has(next)) next++;
      return Math.max(next, this.pendingForeign.get(from) ?? 0);
    },
    medianPriorityFee: async (_count: number, _block: bigint): Promise<bigint> => {
      this.guard();
      return this.tip;
    },
    sendRawTransaction: async (raw: string): Promise<string> => {
      this.sent.push(raw);
      this.onSend?.(raw);
      const failure = this.sendFailures.shift();
      if (failure) throw failure;
      this.guard();
      const tx = Transaction.from(raw);
      const hash = (tx.hash ?? '').toLowerCase();
      if (tx.chainId !== this.chainId) throw new RpcError('invalid chain id', 'rpc', 'fake');
      const from = canonicalAddress(tx.from ?? '');
      if (this.mempool.has(hash) || this.receipts.has(hash)) {
        throw new RpcError('already known', 'rpc', 'fake');
      }
      if (tx.nonce < this.nonce(from)) throw new RpcError('nonce too low', 'rpc', 'fake');
      for (const other of this.mempool.values()) {
        if (canonicalAddress(other.from ?? '') === from && other.nonce === tx.nonce) {
          const okFee = (tx.maxFeePerGas ?? 0n) * 10n >= (other.maxFeePerGas ?? 0n) * 11n;
          const okTip =
            (tx.maxPriorityFeePerGas ?? 0n) * 10n >= (other.maxPriorityFeePerGas ?? 0n) * 11n;
          if (!okFee || !okTip)
            throw new RpcError('replacement transaction underpriced', 'rpc', 'fake');
          this.mempool.delete((other.hash ?? '').toLowerCase());
        }
      }
      this.mempool.set(hash, tx);
      return hash;
    },
    getTransactionReceipt: async (hash: string): Promise<TransactionReceipt | null> => {
      this.guard();
      return this.receipts.get(hash.toLowerCase()) ?? null;
    },
    getLogs: async (filter: {
      address: string | readonly string[];
      topics: ReadonlyArray<string | readonly string[] | null>;
      fromBlock: bigint;
      toBlock: bigint;
    }): Promise<RpcLog[]> => {
      this.guard();
      const contracts = (Array.isArray(filter.address) ? filter.address : [filter.address]).map(
        (a) => canonicalAddress(a as string),
      );
      return this.logs.filter(
        (l) =>
          contracts.includes(canonicalAddress(l.address)) &&
          l.blockNumber >= filter.fromBlock &&
          l.blockNumber <= filter.toBlock &&
          filter.topics.every((t, i) => t === null || t === l.topics[i]),
      );
    },
  };

  /** Adds a historical hot-wallet outflow log (for the daily limit). */
  addOutflowLog(contract: string, from: string, to: string, units: bigint, block: bigint): void {
    this.logs.push({
      address: canonicalAddress(contract),
      topics: [SEND_TRANSFER_TOPIC, addressTopic(from), addressTopic(to)],
      data: '0x' + units.toString(16).padStart(64, '0'),
      blockNumber: block,
      blockHash: this.blockHash(block),
      transactionHash: '0x' + (this.logs.length + 1).toString(16).padStart(64, 'a'),
      logIndex: 0,
      removed: false,
    });
  }
}
