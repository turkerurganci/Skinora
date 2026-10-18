import type { EthereumRpcClient } from '../rpc/RpcClient.js';
import { balanceOfCall, decodeUint } from '../monitor/erc20.js';
import type { StablecoinAllowlist, StablecoinSymbol } from '../monitor/tokenRules.js';
import { CU_COST, type RatePacer } from './RatePacer.js';

/**
 * Paced balance reads at one explicit block (T151 — 08 §3.7.4 "Bakiye
 * mutabakatı"). Every read goes through the {@link RatePacer}; nothing is read
 * at `latest` (the RPC client does not allow it). Used by the hourly
 * reconciliation and by `POST /api/wallet/balances`.
 */

export interface AddressBalances {
  /** Wei. */
  readonly ETH: bigint;
  /** Base units per configured stablecoin; a symbol without a contract is absent. */
  readonly tokens: Partial<Record<StablecoinSymbol, bigint>>;
}

export class BalanceReader {
  constructor(
    private readonly rpc: Pick<EthereumRpcClient, 'call' | 'getBalance'>,
    private readonly allowlist: StablecoinAllowlist,
    private readonly pacer: RatePacer,
  ) {}

  /** Compute units one {@link read} costs (for logs and the spreading arithmetic). */
  costPerAddress(): number {
    return CU_COST.eth_getBalance + this.configuredSymbols().length * CU_COST.eth_call;
  }

  configuredSymbols(): StablecoinSymbol[] {
    return (['USDT', 'USDC'] as const).filter((s) => this.allowlist[s] !== '');
  }

  async read(address: string, block: bigint): Promise<AddressBalances> {
    await this.pacer.acquire(CU_COST.eth_getBalance);
    const eth = await this.rpc.getBalance(address, block);
    const tokens: Partial<Record<StablecoinSymbol, bigint>> = {};
    for (const symbol of this.configuredSymbols()) {
      await this.pacer.acquire(CU_COST.eth_call);
      tokens[symbol] = decodeUint(
        await this.rpc.call({ to: this.allowlist[symbol], data: balanceOfCall(address) }, block),
      );
    }
    return { ETH: eth, tokens };
  }
}
