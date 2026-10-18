import { logger } from '../logger.js';
import { usdtStatusOk } from '../metrics.js';
import { SELECTORS, decodeBool, decodeUint } from '../monitor/erc20.js';
import type { EthereumRpcClient } from '../rpc/RpcClient.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';

/**
 * Is the configured USDT contract usable for payments? (T151 — 08 §3.7.1,
 * 11 §F8 T142 "USDT durum kontrolü" (öneriyle kaydedildi — ⚠)).
 *
 * <para>
 * Tether's contract has three switches that would change what a payment means:
 * a transfer fee (`basisPointsRate` / `maximumFee` — the deposit would receive
 * less than the buyer sent), `paused` (no transfer moves) and `deprecated`
 * (the token moved to `upgradedAddress`; this contract's events are no longer
 * the whole story). All three were off when T142 read them. The service
 * re-reads them at the finalized block on a fixed cadence and on startup;
 * while any is on — or while they could not be read yet — the service does
 * not declare `acceptsPayments` (/health), so the backend keeps Ethereum out
 * of `supportedNetworks` and its probe reports the network unhealthy
 * (T151 kod düzeyi kapı). The sending path (T152) repeats the check before
 * every transfer; {@link readUsdtSwitches} is the shared read.
 * </para>
 */

export type UsdtState =
  | 'not_configured'
  | 'unknown'
  | 'ok'
  | 'fee_enabled'
  | 'paused'
  | 'deprecated';

export interface UsdtSwitches {
  readonly basisPointsRate: bigint;
  readonly maximumFee: bigint;
  readonly paused: boolean;
  readonly deprecated: boolean;
}

export interface UsdtStatusSnapshot {
  readonly state: UsdtState;
  readonly block: bigint | null;
  readonly switches: UsdtSwitches | null;
  readonly lastError: string | null;
}

/** Reads Tether's four switches at an explicit block. */
export async function readUsdtSwitches(
  rpc: Pick<EthereumRpcClient, 'call'>,
  contract: string,
  block: bigint,
): Promise<UsdtSwitches> {
  const read = (data: string) => rpc.call({ to: contract, data }, block);
  return {
    basisPointsRate: decodeUint(await read(SELECTORS.basisPointsRate)),
    maximumFee: decodeUint(await read(SELECTORS.maximumFee)),
    paused: decodeBool(await read(SELECTORS.paused)),
    deprecated: decodeBool(await read(SELECTORS.deprecated)),
  };
}

/** Which switch makes the contract unusable — the order is the order an operator should look in. */
export function usdtStateOf(s: UsdtSwitches): UsdtState {
  if (s.deprecated) return 'deprecated';
  if (s.paused) return 'paused';
  if (s.basisPointsRate !== 0n || s.maximumFee !== 0n) return 'fee_enabled';
  return 'ok';
}

export interface UsdtStatusMonitorDeps {
  rpc: Pick<EthereumRpcClient, 'call'>;
  /** Canonical contract; '' = not configured. */
  contract: string;
  finality: () => FinalityStatus;
  intervalMs: number;
  /**
   * While the state is still `unknown` (no read has succeeded — typically the
   * finalized head was not read yet at start) the read is retried this often,
   * so a restart does not leave payments closed for a whole `intervalMs`
   * (T151 doğrulaması). Default 15 s.
   */
  retryMs?: number;
}

export const DEFAULT_UNKNOWN_RETRY_MS = 15_000;

export class UsdtStatusMonitor {
  private snapshot: UsdtStatusSnapshot;
  private timer?: NodeJS.Timeout;
  private retryTimer?: NodeJS.Timeout;
  private checking = false;

  constructor(private readonly deps: UsdtStatusMonitorDeps) {
    this.snapshot = {
      state: deps.contract === '' ? 'not_configured' : 'unknown',
      block: null,
      switches: null,
      lastError: null,
    };
  }

  status(): UsdtStatusSnapshot {
    return this.snapshot;
  }

  start(): void {
    if (this.deps.contract === '') return;
    void this.checkOnce();
    this.timer = setInterval(() => void this.checkOnce(), this.deps.intervalMs);
    this.timer.unref();
    this.retryTimer = setInterval(() => {
      if (this.snapshot.state === 'unknown') void this.checkOnce();
    }, this.deps.retryMs ?? DEFAULT_UNKNOWN_RETRY_MS);
    this.retryTimer.unref();
  }

  stop(): void {
    if (this.timer) clearInterval(this.timer);
    if (this.retryTimer) clearInterval(this.retryTimer);
    this.timer = undefined;
    this.retryTimer = undefined;
  }

  /**
   * One read at the current finalized block. Never throws. A failed read
   * keeps the last known state (a provider hiccup does not close payments);
   * the first read failing leaves it `unknown` (closed).
   */
  async checkOnce(): Promise<void> {
    if (this.deps.contract === '' || this.checking) return;
    const head = this.deps.finality().snapshot;
    if (!head) return;
    this.checking = true;
    try {
      const switches = await readUsdtSwitches(this.deps.rpc, this.deps.contract, head.number);
      const state = usdtStateOf(switches);
      if (state !== this.snapshot.state) {
        const log = state === 'ok' ? logger.info.bind(logger) : logger.error.bind(logger);
        log(
          {
            contract: this.deps.contract,
            block: String(head.number),
            previous: this.snapshot.state,
            state,
            basisPointsRate: String(switches.basisPointsRate),
            maximumFee: String(switches.maximumFee),
            paused: switches.paused,
            deprecated: switches.deprecated,
          },
          state === 'ok'
            ? 'USDT contract usable — fee switch off, not paused, not deprecated'
            : 'USDT contract NOT usable — Ethereum stops accepting new payments',
        );
      }
      this.snapshot = { state, block: head.number, switches, lastError: null };
      usdtStatusOk.set(state === 'ok' ? 1 : 0);
    } catch (err) {
      this.snapshot = { ...this.snapshot, lastError: (err as Error).message };
      if (this.snapshot.state === 'unknown') usdtStatusOk.set(0);
      logger.warn({ reason: (err as Error).message }, 'USDT status could not be read');
    } finally {
      this.checking = false;
    }
  }
}
