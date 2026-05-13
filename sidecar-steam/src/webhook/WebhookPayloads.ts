import type { BotFailureReason, BotSessionStatus } from '../bot/BotSession.js';

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
