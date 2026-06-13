import type { BotFailureReason, BotSessionStatus } from '../bot/BotSession.js';
import type { TradeDirection } from '../trade/types.js';

/**
 * Generic webhook envelope sent from sidecar → .NET backend
 * (signed per 05 §3.4 in WebhookClient.sendCallback).
 */
export interface WebhookPayload {
  event: string;
  timestamp: string;
  data: Record<string, unknown>;
}

/**
 * Discriminated data payload for bot lifecycle events
 * (consumed by backend in T68; emitted by BotManager today).
 */
export interface BotSessionFailedData {
  reason: BotFailureReason;
  status: BotSessionStatus;
}

export interface BotRemovedFromPoolData {
  accountName: string;
  reason: BotFailureReason;
  status: BotSessionStatus;
}

export type BotEventName = 'bot.session_failed' | 'bot.removed_from_pool';

export interface BotEventPayload<TData> extends WebhookPayload {
  event: BotEventName;
  data: TData & Record<string, unknown>;
}

/**
 * Trade offer lifecycle events.
 *
 *   trade_offer.sent           — T65: offer reached Steam (state 2/9), MA may still be pending
 *   trade_offer.failed         — T65: offer never made it to Steam (retries exhausted or
 *                                permanent eResult); backend should mark as FAILED
 *   trade_offer.accepted       — T66: partner accepted (state 3) → state machine advance
 *   trade_offer.declined       — T66: partner declined (state 7) → cancellation flow
 *   trade_offer.expired        — T66: offer hit Steam's expiration (state 5) → timeout flow
 *   trade_offer.countered      — T66: partner counter-offered (state 4); Skinora does not
 *                                support counters, backend treats as cancellation (08 §2.4)
 *   trade_offer.invalid_items  — T66: items no longer tradeable (state 8) → cancellation +
 *                                user notification (08 §2.4)
 */
export type TradeOfferEventName =
  | 'trade_offer.sent'
  | 'trade_offer.failed'
  | 'trade_offer.accepted'
  | 'trade_offer.declined'
  | 'trade_offer.expired'
  | 'trade_offer.countered'
  | 'trade_offer.invalid_items';

export interface TradeOfferSentData {
  transactionId: string;
  direction: TradeDirection;
  partnerSteamId: string;
  botSteamId?: string;
  botAccountName: string;
  offerId: string;
  /**
   * 'pending'  : Steam accepted but mobile confirmation outstanding (state 9)
   * 'sent'     : offer is active and visible to partner (state 2)
   * 'confirmed': mobile confirmation completed locally → state 2
   */
  status: 'pending' | 'sent' | 'confirmed';
  attempts: number;
}

export interface TradeOfferFailedData {
  transactionId: string;
  direction: TradeDirection;
  partnerSteamId: string;
  botAccountName?: string;
  reason: string;
  eresult?: number;
  retryable: boolean;
  attempts: number;
}

/**
 * Shared payload for T66 status-change events
 * (accepted/declined/expired/countered/invalid_items).
 *
 * The sidecar does not know the Skinora transactionId at status-change time —
 * Steam only knows the offerId. Backend resolves transactionId from its own
 * mapping persisted on `trade_offer.sent`. partnerSteamId is included as a
 * sanity-check so backend can detect cross-routing bugs.
 */
export interface TradeOfferStatusChangedData {
  offerId: string;
  partnerSteamId: string;
  botSteamId?: string;
  botAccountName: string;
  /** ETradeOfferState code; matches the event suffix. */
  newState: number;
  /** ETradeOfferState code of the previous state, for diagnostics. */
  oldState: number;
  /**
   * T106a — post-acceptance asset lineage (Accepted/state 3 only). After a
   * trade completes the moved item gets a NEW Steam asset id in the recipient's
   * inventory; the send-time descriptor is stale. The backend needs these to
   * populate `Transaction.EscrowBotAssetId` (escrow leg — item now in the bot)
   * and `Transaction.DeliveredBuyerAssetId` (delivery leg — item now with the
   * buyer), which the ITEM_ESCROWED / ITEM_DELIVERED state-machine guards
   * require. Resolved via `TradeOffer.getExchangeDetails`; absent for non-accept
   * states or when the exchange-details fetch fails (backend logs + acks rather
   * than advancing — never a silent stall).
   *
   * `receivedAssetId`  : new id of the item the BOT received (escrow leg).
   * `deliveredAssetId` : new id of the item the COUNTERPARTY received
   *                      (delivery + refund legs — bot sent the item out).
   */
  receivedAssetId?: string;
  deliveredAssetId?: string;
}

/** Maps the T66-tracked ETradeOfferState values to the webhook event name. */
export const TRADE_OFFER_STATE_EVENT_MAP: ReadonlyMap<number, TradeOfferEventName> = new Map([
  [3, 'trade_offer.accepted'],
  [4, 'trade_offer.countered'],
  [5, 'trade_offer.expired'],
  [7, 'trade_offer.declined'],
  [8, 'trade_offer.invalid_items'],
]);
