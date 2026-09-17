import TronWeb from 'tronweb';
import { SidecarError, SimulationRevertedError } from '../errors/SidecarError.js';

/**
 * Read-only chain probes backing the pre-send fee estimate
 * (Prova-GasFeeChargedIsFixedGuess) and the deposit transfer resource flow
 * (EnergyDelegationService), all against the full node's REST API with an
 * injectable <c>fetchFn</c> (mirrors <c>TronTransferClient.getTransactionStatus</c>).
 * The core ones:
 *
 * <list type="bullet">
 *   <item><c>estimateTransferEnergy</c> — `triggerconstantcontract` simulation
 *     of the exact TRC-20 transfer. This is what captures the ~64k vs ~130k
 *     Energy split between a recipient that already holds the token and one
 *     that does not (config/index.ts:94-96 measurement).</item>
 *   <item><c>getAccountResources</c> — `getaccountresource` snapshot of an
 *     account's spendable Energy / Bandwidth.</item>
 *   <item><c>getChainFeeParameters</c> — `getchainparameters` unit prices
 *     (sun per Energy, sun per Bandwidth byte). Network-wide values that the
 *     committee can change, so they are read, not assumed.</item>
 *   <item><c>getTransactionBlockNumber</c> — `gettransactioninfobyid`: whether
 *     a broadcast step is actually IN a block, which no account read can
 *     tell.</item>
 * </list>
 */

export interface AccountResources {
  /** Spendable Energy: EnergyLimit − EnergyUsed, floored at 0. */
  energyAvailable: number;
  /** Spendable Bandwidth: free + staked allowances net of usage, floored at 0. */
  bandwidthAvailable: number;
  /**
   * Network-wide Energy produced per staked TRX (TotalEnergyLimit /
   * TotalEnergyWeight), or null when the node omits the fields. Needed to turn
   * a delegation expressed in TRX into the Energy it actually delivers.
   */
  energyPerTrx: number | null;
}

export interface ContractEnergyPolicy {
  /**
   * The contract's NOMINAL caller share, 0-100 (`consume_user_resource_percent`).
   *
   * Nominal only: the owner pays its share out of its OWN remaining Energy, so
   * the split the chain actually applies also needs <see cref="originAddress"/>'s
   * resources (`callerEnergyShare`, DelegationPlanner). A TRC-20 contract can
   * be deployed so its owner absorbs the execution cost, and the Nile test USDT
   * used for rehearsals is (0 here, ~197M Energy left on 2026-09-17) — which is
   * why rehearsal transfers show `fee: 0`. Mainnet Tether sets 30, but its
   * owner has no Energy left, and across 542 USDT calls in five blocks the
   * callers paid 100.00% (measured 2026-09-17). Read alone, this number charged
   * a mainnet user 30% of the real cost.
   */
  callerPercent: number;
  /** Owner's Energy ceiling for a single call; 0 means the owner subsidises nothing. */
  originEnergyLimit: number;
  /** The contract owner (`origin_address`) whose remaining Energy pays its share; null when not reported. */
  originAddress: string | null;
}

export interface AccountState {
  /**
   * Whether the account exists on-chain. An address that has only RECEIVED a
   * TRC-20 token is NOT an account: measured on Nile 2026-09-16, both
   * `delegateresource` to it ("Account[…] not exists") and a transfer FROM it
   * ("account […] does not exist") are rejected at validation. Only a TRX (or
   * TRC-10) transfer creates it.
   */
  exists: boolean;
  /** TRX balance in SUN; 0 when the account does not exist. */
  balanceSun: number;
}

export interface ChainFeeParameters {
  /** Sun burned per 1 Energy when the account has none (getEnergyFee). */
  energyFeeSun: number;
  /** Sun burned per 1 Bandwidth byte when the account has none (getTransactionFee). */
  bandwidthFeeSun: number;
}

interface AccountResourceResponse {
  freeNetLimit?: number;
  freeNetUsed?: number;
  NetLimit?: number;
  NetUsed?: number;
  EnergyLimit?: number;
  EnergyUsed?: number;
  TotalEnergyLimit?: number;
  TotalEnergyWeight?: number;
}

interface ContractResponse {
  contract_address?: string;
  origin_address?: string;
  consume_user_resource_percent?: number;
  origin_energy_limit?: number;
}

interface ChainParametersResponse {
  chainParameter?: { key?: string; value?: number }[];
}

interface TriggerConstantResponse {
  result?: { result?: boolean; message?: string };
  energy_used?: number;
  transaction?: { ret?: { ret?: string }[] };
}

export class TronResourceClient {
  private readonly fullNodeUrl: string;
  private readonly apiKey: string;

  constructor(fullNodeUrl: string, apiKey: string) {
    this.fullNodeUrl = fullNodeUrl;
    this.apiKey = apiKey;
  }

  /**
   * Simulate `transfer(to, amountUnits)` on <paramref name="contractAddress"/>
   * as <paramref name="fromAddress"/> and return the Energy the real broadcast
   * would consume. The simulation runs against current chain state, so the
   * sender must actually hold the tokens for the result to be the success-path
   * cost. That is the ordinary case (refund → deposit address holding the
   * buyer's payment, payout → hot wallet) but NOT a guarantee: a wrong-token
   * refund simulates a token the deposit does not hold, and a payout may run
   * before the sweep funds the hot wallet. Those simulations revert, and a
   * reverted simulation is rejected here rather than passed off as a cost.
   */
  async estimateTransferEnergy(
    contractAddress: string,
    fromAddress: string,
    toAddress: string,
    amountUnits: string,
    fetchFn: typeof fetch = fetch,
  ): Promise<number> {
    const toHex = TronWeb.address.toHex(toAddress); // '41' + 40 hex chars
    const parameter =
      toHex.slice(2).toLowerCase().padStart(64, '0') +
      BigInt(amountUnits).toString(16).padStart(64, '0');

    const body = await this.post<TriggerConstantResponse>(
      '/wallet/triggerconstantcontract',
      {
        owner_address: fromAddress,
        contract_address: contractAddress,
        function_selector: 'transfer(address,uint256)',
        parameter,
        visible: true,
      },
      fetchFn,
    );

    // `result.result: true` only means the node RAN the call, not that the
    // call succeeded. A reverting transfer answers HTTP 200 with that same
    // true, and reports the failure in `result.message` / `transaction.ret`
    // instead (measured on Nile 2026-09-06 — revert energy_used 1984 against
    // 29650 for the same transfer succeeding; on mainnet 2026-09-17 —
    // "REVERT opcode executed", ret FAILED, energy_used 8624 from an address
    // holding no USDT). Reading the revert as an estimate would charge a
    // fraction of the real cost and the platform would silently absorb the
    // rest. It is reachable without any outage: a payout simulates from a hot
    // wallet the sweep may not have funded yet, and a deposit transfer retried
    // after an unrecorded first attempt finds its tokens already gone.
    //
    // A node that REFUSES the call answers without `result: true`
    // (`OTHER_ERROR` for a malformed address, `CONTRACT_VALIDATE_ERROR` for a
    // missing contract — measured on mainnet 2026-09-17): that is a failed
    // probe, not an answer about the transfer, and it is not a revert.
    const executedButFailed =
      body.result?.result === true
        ? typeof body.result.message === 'string' && body.result.message.length > 0
          ? body.result.message
          : body.transaction?.ret?.find(
              (entry) =>
                typeof entry?.ret === 'string' && entry.ret !== '' && entry.ret !== 'SUCCESS',
            )?.ret
        : undefined;

    if (executedButFailed) {
      throw new SimulationRevertedError(
        `triggerconstantcontract simulation failed: ${executedButFailed}`,
        executedButFailed,
      );
    }
    if (body.result?.result !== true || typeof body.energy_used !== 'number') {
      throw new SidecarError(
        `triggerconstantcontract simulation failed: ${body.result?.message ?? 'no energy_used in response'}`,
        'FEE_ESTIMATE_SIMULATION_FAILED',
        true,
      );
    }
    return body.energy_used;
  }

  async getAccountResources(
    address: string,
    fetchFn: typeof fetch = fetch,
  ): Promise<AccountResources> {
    const body = await this.post<AccountResourceResponse>(
      '/wallet/getaccountresource',
      { address, visible: true },
      fetchFn,
    );
    // An unactivated account returns an empty object — zero of everything,
    // which is exactly what the fee math should see.
    const energyAvailable = Math.max(0, (body.EnergyLimit ?? 0) - (body.EnergyUsed ?? 0));
    const bandwidthAvailable =
      Math.max(0, (body.freeNetLimit ?? 0) - (body.freeNetUsed ?? 0)) +
      Math.max(0, (body.NetLimit ?? 0) - (body.NetUsed ?? 0));
    // Network-wide ratio, returned on every account response. Moves with the
    // total staked supply, so it is read per call and never cached as a
    // constant (measured 2026-08-29: mainnet ~9.57, Nile ~73.8).
    const energyPerTrx =
      typeof body.TotalEnergyLimit === 'number' &&
      typeof body.TotalEnergyWeight === 'number' &&
      body.TotalEnergyWeight > 0
        ? body.TotalEnergyLimit / body.TotalEnergyWeight
        : null;
    return { energyAvailable, bandwidthAvailable, energyPerTrx };
  }

  /**
   * Existence + TRX balance of <paramref name="address"/> (`getaccount`).
   *
   * An empty body is the node's answer for a non-existent account, and here —
   * unlike `getcontract` — that is a real answer, not a failed probe: the
   * caller acts on it by creating the account, which is harmless if the probe
   * was wrong (a TRX transfer to an existing account just adds balance).
   */
  async getAccountState(address: string, fetchFn: typeof fetch = fetch): Promise<AccountState> {
    const body = await this.post<{ address?: string; balance?: number }>(
      '/wallet/getaccount',
      { address, visible: true },
      fetchFn,
    );
    const exists = typeof body.address === 'string' && body.address.length > 0;
    return { exists, balanceSun: exists ? Math.max(0, body.balance ?? 0) : 0 };
  }

  /**
   * How much staked TRX (in SUN) <paramref name="ownerAddress"/> can delegate
   * as ENERGY right now (`getcandelegatedmaxsize`, type 1).
   *
   * The node answers `{}` when nothing is delegatable (measured on Nile
   * 2026-09-16 with no stake). Reading that as 0 is safe in the only direction
   * that matters: 0 sends the caller down the burn path, which always works.
   */
  async getDelegatableEnergySun(
    ownerAddress: string,
    fetchFn: typeof fetch = fetch,
  ): Promise<number> {
    const body = await this.post<{ max_size?: number }>(
      '/wallet/getcandelegatedmaxsize',
      { owner_address: ownerAddress, type: 1, visible: true },
      fetchFn,
    );
    return typeof body.max_size === 'number' && body.max_size > 0 ? body.max_size : 0;
  }

  /**
   * Who pays this contract's execution energy, and up to what ceiling.
   * Failure is NOT fatal: the caller falls back to "the sender pays
   * everything", which is the conservative direction — it can overcharge the
   * platform's own estimate, never the user beyond the true worst case.
   */
  async getContractEnergyPolicy(
    contractAddress: string,
    fetchFn: typeof fetch = fetch,
  ): Promise<ContractEnergyPolicy> {
    const body = await this.post<ContractResponse>(
      '/wallet/getcontract',
      { value: contractAddress, visible: true },
      fetchFn,
    );
    // An address that is not a contract answers HTTP 200 with an EMPTY object
    // (measured on Nile 2026-09-06), which the "omitted field means 0" rule
    // below would read as "the owner pays everything" — i.e. a mistyped or
    // unmigrated contract address would silently charge 0 forever. An
    // identity-less body is a failed probe, not an answer, so it takes the
    // caller's conservative fallback (the sender pays 100%) instead.
    if (typeof body.contract_address !== 'string' || body.contract_address.length === 0) {
      throw new SidecarError(
        `getcontract returned no contract at ${contractAddress}.`,
        'FEE_ESTIMATE_CONTRACT_NOT_FOUND',
        true,
      );
    }
    // The field is omitted entirely when it is 0 — i.e. an absent
    // `consume_user_resource_percent` means the OWNER pays everything, the
    // opposite of what a naive `?? 100` default would conclude.
    const callerPercent =
      typeof body.consume_user_resource_percent === 'number'
        ? Math.min(100, Math.max(0, body.consume_user_resource_percent))
        : 0;
    const originEnergyLimit =
      typeof body.origin_energy_limit === 'number' ? body.origin_energy_limit : 0;
    const originAddress =
      typeof body.origin_address === 'string' && body.origin_address.length > 0
        ? body.origin_address
        : null;
    return { callerPercent, originEnergyLimit, originAddress };
  }

  /**
   * The block <paramref name="txHash"/> landed in, or null while no block holds
   * it (`gettransactioninfobyid` on the full node).
   *
   * This — not an account read — is what "the step happened" means. Account
   * reads answer from the node's PENDING state: in the 2026-09-16 Nile run a
   * delegation, the transfer broadcast after its "Energy arrived" check and the
   * reclaim all landed in ONE block (71,019,348), so their order was only the
   * order the block producer received them in. The node answers `{}` for a
   * hash no block holds (measured 2026-09-17 on mainnet and Nile) and a
   * `blockNumber` as soon as it applies the block — no solidity lag. Anything
   * without a positive integer `blockNumber` is "not yet", never a block.
   */
  async getTransactionBlockNumber(
    txHash: string,
    fetchFn: typeof fetch = fetch,
  ): Promise<number | null> {
    const body = await this.post<{ id?: string; blockNumber?: unknown }>(
      '/wallet/gettransactioninfobyid',
      { value: txHash },
      fetchFn,
    );
    return typeof body.blockNumber === 'number' &&
      Number.isInteger(body.blockNumber) &&
      body.blockNumber > 0
      ? body.blockNumber
      : null;
  }

  async getChainFeeParameters(fetchFn: typeof fetch = fetch): Promise<ChainFeeParameters> {
    const response = await fetchFn(`${this.fullNodeUrl}/wallet/getchainparameters`, {
      method: 'GET',
      headers: this.headers(),
    });
    if (!response.ok) {
      throw new SidecarError(
        `getchainparameters returned HTTP ${response.status}`,
        'FEE_ESTIMATE_CHAIN_PARAMS_FAILED',
        true,
      );
    }
    const body = (await response.json()) as ChainParametersResponse;
    const find = (key: string): number | null => {
      const entry = body.chainParameter?.find((p) => p.key === key);
      return typeof entry?.value === 'number' ? entry.value : null;
    };
    const energyFeeSun = find('getEnergyFee');
    const bandwidthFeeSun = find('getTransactionFee');
    if (energyFeeSun === null || bandwidthFeeSun === null) {
      throw new SidecarError(
        'getchainparameters response is missing getEnergyFee / getTransactionFee.',
        'FEE_ESTIMATE_CHAIN_PARAMS_FAILED',
        true,
      );
    }
    return { energyFeeSun, bandwidthFeeSun };
  }

  private async post<T>(path: string, payload: unknown, fetchFn: typeof fetch): Promise<T> {
    const response = await fetchFn(`${this.fullNodeUrl}${path}`, {
      method: 'POST',
      headers: this.headers(),
      body: JSON.stringify(payload),
    });
    if (!response.ok) {
      throw new SidecarError(
        `${path} returned HTTP ${response.status}`,
        'FEE_ESTIMATE_HTTP_ERROR',
        true,
      );
    }
    return (await response.json()) as T;
  }

  private headers(): Record<string, string> {
    const headers: Record<string, string> = {
      accept: 'application/json',
      'content-type': 'application/json',
    };
    if (this.apiKey) headers['TRON-PRO-API-KEY'] = this.apiKey;
    return headers;
  }
}
