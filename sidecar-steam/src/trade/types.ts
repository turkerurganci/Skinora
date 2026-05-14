import type { ItemDescriptor, TradeOffer } from 'steam-tradeoffer-manager';

/**
 * Trade direction — drives which side of the offer items are added to and
 * whether the bot must mobile-confirm after send (08 §2.4).
 *
 *   SELLER_TO_BOT          : bot RECEIVES items from seller → addTheirItem,
 *                            no mobile confirmation needed (bot is recipient)
 *   BOT_TO_BUYER           : bot SENDS items to buyer → addMyItem,
 *                            mobile confirmation required
 *   BOT_TO_SELLER_REFUND   : bot SENDS items back to seller → addMyItem,
 *                            mobile confirmation required
 */
export type TradeDirection = 'SELLER_TO_BOT' | 'BOT_TO_BUYER' | 'BOT_TO_SELLER_REFUND';

/** Request envelope from .NET backend → sidecar POST /api/trade-offers/send. */
export interface SendTradeOfferRequest {
  transactionId: string;
  direction: TradeDirection;
  partnerSteamId: string;
  items: ItemDescriptor[];
  message?: string;
}

/**
 * Synchronous response — offerId becomes available only on terminal-OK states.
 *
 *   pending   : Steam accepted but mobile confirmation outstanding (state 9)
 *   sent      : offer is active on partner side without MA confirmation
 *               (SELLER_TO_BOT path — bot is recipient, no MA needed)
 *   confirmed : offer reached Steam and mobile confirmation completed inline
 *   failed    : send failed permanently or retries exhausted
 */
export interface SendTradeOfferResponse {
  status: 'sent' | 'pending' | 'confirmed' | 'failed';
  offerId?: string;
  reason?: string;
  retryable?: boolean;
  attempts: number;
}

/** 08 §2.7 retry backoff schedule: 5s / 15s / 45s. */
export const TRADE_OFFER_BACKOFF_MS = [5_000, 15_000, 45_000] as const;

/**
 * EResult codes treated as transient for trade offer send retries.
 * Anything else (or no eresult on a non-network error) is permanent.
 *
 * Reference: https://steamerrors.com/
 *   - 10 NoConnection
 *   - 16 Timeout
 *   - 41 RemoteCallFailed
 *   - 84 RateLimitExceeded
 */
export const TRANSIENT_TRADE_ERESULTS: ReadonlySet<number> = new Set([10, 16, 41, 84]);

/**
 * Node.js network error codes — bubbled up from undici / steamcommunity HTTP
 * client wrapped inside steam-tradeoffer-manager. Treat as transient.
 */
export const TRANSIENT_NETWORK_CODES: ReadonlySet<string> = new Set([
  'ECONNRESET',
  'ETIMEDOUT',
  'ECONNREFUSED',
  'EAI_AGAIN',
  'ENETUNREACH',
]);

/** Direction → does this side need a mobile confirmation after send? */
export function requiresMobileConfirmation(direction: TradeDirection): boolean {
  return direction === 'BOT_TO_BUYER' || direction === 'BOT_TO_SELLER_REFUND';
}

/**
 * T66 — Handler bridge between BotSession's TradeOfferManager and
 * TradeOfferMonitor. The monitor binds these on every session so it does not
 * need access to the private tradeManager instance.
 *
 *   onSentOfferChanged : forwarded `sentOfferChanged` (offer, oldState)
 *   onPollFailure      : forwarded `pollFailure` (err)
 */
export interface TradeOfferEventHandler {
  onSentOfferChanged: (offer: TradeOffer, oldState: number) => void;
  onPollFailure: (err: Error) => void;
}
