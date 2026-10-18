import { AbiCoder, Transaction } from 'ethers';
import { canonicalAddress } from '../address/canonicalAddress.js';
import {
  RpcError,
  type BlockHeader,
  type RpcLog,
  type TransactionReceipt,
} from '../rpc/RpcClient.js';
import { TOKEN_ABI, TRANSFER_TOPIC, addressTopic, decodeTransferCall } from '../transfer/erc20.js';

/**
 * An in-memory Ethereum for the send tests (T152): accounts with nonces, ETH
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
export class FakeEthereumChain {
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
          topics: [TRANSFER_TOPIC, addressTopic(from), addressTopic(call.to)],
          data: '0x' + call.amountUnits.toString(16).padStart(64, '0'),
          blockNumber: block,
          blockHash: this.blockHash(block),
          transactionHash: hash,
          logIndex: this.logs.length,
          removed: false,
        };
        logs.push(log);
        this.logs.push(log);
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
      gasUsed: 41_273n,
      effectiveGasPrice: this.baseFee + this.tip,
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
      this.revertIfBlocked(tx.from, tx.to, tx.data);
      return canonicalAddress(tx.to) === this.usdc
        ? AbiCoder.defaultAbiCoder().encode(['bool'], [true])
        : '0x';
    },
    estimateGas: async (
      tx: { from: string; to: string; data: string },
      _block: bigint,
    ): Promise<bigint> => {
      this.guard();
      this.revertIfBlocked(tx.from, tx.to, tx.data);
      return this.estimate;
    },
    getTransactionCount: async (address: string, _block: bigint): Promise<number> => {
      this.guard();
      return this.nonce(address);
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
      topics: [TRANSFER_TOPIC, addressTopic(from), addressTopic(to)],
      data: '0x' + units.toString(16).padStart(64, '0'),
      blockNumber: block,
      blockHash: this.blockHash(block),
      transactionHash: '0x' + (this.logs.length + 1).toString(16).padStart(64, 'a'),
      logIndex: 0,
      removed: false,
    });
  }
}
