import { SidecarError, SteamApiError } from '../errors/SidecarError.js';
import type { RateLimitedQueue } from '../queue/RateLimitedQueue.js';
import { logger as defaultLogger, type Logger } from '../logger.js';

/**
 * Steam Web API trade-hold (escrow) duration check — 08 §2.2.
 *
 * Steam exposes no direct "is Mobile Authenticator active?" endpoint; the
 * platform infers it from the user's trade hold duration via
 * `IEconService/GetTradeHoldDurations/v1`:
 *
 *   their_escrow.escrow_end_duration_seconds === 0  → MA active (no hold)
 *   their_escrow.escrow_end_duration_seconds  > 0   → MA inactive (15-day escrow)
 *
 * This is a **Web API key** call (no bot session required): it is keyed on the
 * platform `STEAM_API_KEY` plus the target SteamID64 and the
 * `trade_offer_access_token` parsed from the user's trade URL (mandatory for
 * non-friend targets — 08 §2.2). It therefore works even when the bot pool is
 * empty, which is why it lives outside the BotSession/TradeOfferManager stack.
 *
 * The backend calls this through `ITradeHoldChecker` (U17 trade-URL save) and
 * `IMobileAuthenticatorCheck` (A7 re-verify) — both map a successful result to
 * `mobileAuthenticatorActive` and fall back to `STEAM_API_UNAVAILABLE`
 * (07 §5.16a) when this throws.
 */
export interface TradeHoldResult {
  /** True when the target's escrow hold is 0 seconds → Mobile Authenticator active. */
  active: boolean;
  /** Raw escrow hold for the target account, in seconds (0 when MA is active). */
  escrowEndDurationSeconds: number;
}

/** Injectable fetch surface so unit tests can stub the Steam round-trip. */
export type FetchLike = (
  input: string,
  init?: { method?: string; headers?: Record<string, string> },
) => Promise<{ ok: boolean; status: number; json: () => Promise<unknown> }>;

const STEAM_TRADE_HOLD_URL = 'https://api.steampowered.com/IEconService/GetTradeHoldDurations/v1/';

/** Thrown when STEAM_API_KEY is not configured — the check cannot run. */
export class SteamApiKeyMissingError extends SidecarError {
  constructor() {
    super('STEAM_API_KEY is not configured', 'STEAM_API_KEY_MISSING', false);
    this.name = 'SteamApiKeyMissingError';
  }
}

export class TradeHoldService {
  constructor(
    private readonly apiKey: string,
    private readonly queue?: RateLimitedQueue,
    private readonly fetchFn: FetchLike = fetch as unknown as FetchLike,
    private readonly log: Logger = defaultLogger,
  ) {}

  /**
   * Resolve the trade-hold (escrow) duration for `steamId` using
   * `trade_offer_access_token`. Throws {@link SteamApiKeyMissingError} when the
   * key is absent and {@link SteamApiError} on transport / upstream / malformed
   * responses — the backend maps both to the 07 §5.16a fallback.
   */
  async getTradeHold(steamId: string, accessToken: string): Promise<TradeHoldResult> {
    if (!this.apiKey) {
      throw new SteamApiKeyMissingError();
    }
    const run = (): Promise<TradeHoldResult> => this.fetchTradeHold(steamId, accessToken);
    return this.queue ? this.queue.enqueue(run) : run();
  }

  private async fetchTradeHold(steamId: string, accessToken: string): Promise<TradeHoldResult> {
    // 08 §2.2 — prefer the x-webapi-key header over the ?key= query param so the
    // secret never lands in upstream access logs / proxies.
    const params = new URLSearchParams({
      steamid_target: steamId,
      trade_offer_access_token: accessToken,
    });
    const url = `${STEAM_TRADE_HOLD_URL}?${params.toString()}`;

    let response: Awaited<ReturnType<FetchLike>>;
    try {
      response = await this.fetchFn(url, {
        method: 'GET',
        headers: { 'x-webapi-key': this.apiKey },
      });
    } catch (err) {
      throw new SteamApiError(`GetTradeHoldDurations transport failure: ${(err as Error).message}`);
    }

    if (!response.ok) {
      this.log.warn({ steamId, status: response.status }, 'GetTradeHoldDurations returned non-2xx');
      throw new SteamApiError('GetTradeHoldDurations upstream error', response.status);
    }

    let body: unknown;
    try {
      body = await response.json();
    } catch (err) {
      throw new SteamApiError(
        `GetTradeHoldDurations payload parse failure: ${(err as Error).message}`,
      );
    }

    const seconds = extractTargetEscrowSeconds(body);
    if (seconds === null) {
      throw new SteamApiError('GetTradeHoldDurations response missing their_escrow duration');
    }

    return { active: seconds === 0, escrowEndDurationSeconds: seconds };
  }
}

/**
 * Pull `response.their_escrow.escrow_end_duration_seconds` out of the
 * GetTradeHoldDurations payload. `their_escrow` is the target account
 * (`steamid_target`); `my_escrow` is the API-key account and is irrelevant
 * here. Returns null when the field is absent / non-numeric so the caller can
 * fail closed.
 */
function extractTargetEscrowSeconds(body: unknown): number | null {
  if (body === null || typeof body !== 'object') return null;
  const response = (body as Record<string, unknown>).response;
  if (response === null || typeof response !== 'object') return null;
  const theirEscrow = (response as Record<string, unknown>).their_escrow;
  if (theirEscrow === null || typeof theirEscrow !== 'object') return null;
  const seconds = (theirEscrow as Record<string, unknown>).escrow_end_duration_seconds;
  return typeof seconds === 'number' && Number.isFinite(seconds) ? seconds : null;
}
