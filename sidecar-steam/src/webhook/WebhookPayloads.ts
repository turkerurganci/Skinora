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
 * Trade offer lifecycle events (T65: sent/failed; T66: status changes).
 *
 *   trade_offer.sent    — offer reached Steam, may still be awaiting MA confirm
 *   trade_offer.failed  — offer never made it to Steam (retries exhausted or
 *                         permanent eResult); backend should mark as FAILED
 */
export type TradeOfferEventName = 'trade_offer.sent' | 'trade_offer.failed';

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
