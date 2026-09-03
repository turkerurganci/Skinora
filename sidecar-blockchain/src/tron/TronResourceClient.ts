import TronWeb from 'tronweb';
import { SidecarError } from '../errors/SidecarError.js';

/**
 * Read-only chain probes backing the pre-send fee estimate
 * (Prova-GasFeeChargedIsFixedGuess). Three primitives, all against the full
 * node's REST API with an injectable <c>fetchFn</c> (mirrors
 * <c>TronTransferClient.getTransactionStatus</c>):
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
   * Share of the call's Energy the CALLER pays, 0-100.
   *
   * A TRC-20 contract can be deployed so its owner absorbs the execution cost
   * (`consume_user_resource_percent = 0`), and the Nile test USDT used for
   * rehearsals is deployed exactly that way — which is why every measured
   * rehearsal transfer shows `fee: 0` while the hot wallet holds no stake at
   * all. Mainnet Tether sets 100, so the sender pays. Charging a user for
   * energy the contract owner covers is charging for a cost nobody incurred,
   * so the estimate has to read this rather than assume it.
   */
  callerPercent: number;
  /** Owner's Energy ceiling for a single call; 0 means the owner subsidises nothing. */
  originEnergyLimit: number;
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
  consume_user_resource_percent?: number;
  origin_energy_limit?: number;
}

interface ChainParametersResponse {
  chainParameter?: { key?: string; value?: number }[];
}

interface TriggerConstantResponse {
  result?: { result?: boolean; message?: string };
  energy_used?: number;
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
   * cost — both callers satisfy this (refund → deposit address holding the
   * buyer's payment, payout → hot wallet).
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
    // The field is omitted entirely when it is 0 — i.e. an absent
    // `consume_user_resource_percent` means the OWNER pays everything, the
    // opposite of what a naive `?? 100` default would conclude.
    const callerPercent =
      typeof body.consume_user_resource_percent === 'number'
        ? Math.min(100, Math.max(0, body.consume_user_resource_percent))
        : 0;
    const originEnergyLimit =
      typeof body.origin_energy_limit === 'number' ? body.origin_energy_limit : 0;
    return { callerPercent, originEnergyLimit };
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
