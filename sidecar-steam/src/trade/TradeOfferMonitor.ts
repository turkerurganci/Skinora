import crypto from 'crypto';
import type { ExchangeDetailsItem, TradeOffer } from 'steam-tradeoffer-manager';
import { logger as rootLogger } from '../logger.js';
import { sendCallback } from '../webhook/WebhookClient.js';
import {
  TRADE_OFFER_STATE_EVENT_MAP,
  type TradeOfferEventName,
  type TradeOfferStatusChangedData,
  type WebhookPayload,
} from '../webhook/WebhookPayloads.js';
import type { BotManager } from '../bot/BotManager.js';
import type { BotSession } from '../bot/BotSession.js';

const DEFAULT_TRADE_OFFER_ENDPOINT = '/api/v1/sidecar/steam/trade-offer-events';

export interface TradeOfferMonitorOptions {
  /** Webhook endpoint backend will receive trade offer status events on (T68 consumer). */
  tradeOfferEndpoint?: string;
  /** Override the webhook transport (used by tests). */
  webhookSender?: (
    endpoint: string,
    payload: WebhookPayload,
    correlationId: string,
  ) => Promise<void>;
}

/**
 * Steam trade offer status monitor (T66).
 *
 * Wires `sentOfferChanged` from every bot's TradeOfferManager → backend webhook,
 * mapping ETradeOfferState transitions to the 5 events tracked in 08 §2.4:
 *
 *   Accepted (3)     → trade_offer.accepted        (state machine advance)
 *   Countered (4)    → trade_offer.countered       (cancellation; Skinora does not support counters)
 *   Expired (5)      → trade_offer.expired         (timeout flow)
 *   Declined (7)     → trade_offer.declined        (cancellation flow)
 *   InvalidItems (8) → trade_offer.invalid_items   (cancellation + user notice)
 *
 * Unmapped transitions (1 Invalid, 2 Active, 6 Canceled, 9 NeedsConfirmation,
 * 10 CanceledBy2FA, 11 InEscrow) are silently ignored — backend learns about
 * sent/pending via T65's `trade_offer.sent`, and cancel/2FA paths are sidecar-
 * initiated (no need to round-trip notify ourselves).
 *
 * Idempotency:
 *   - Within a sidecar run: in-memory `Map<offerId, lastNewState>` skips duplicate
 *     emissions if the polling cycle re-reports the same terminal state.
 *   - Across restarts: state lost. Steam persists state durably; backend handles
 *     cross-restart idempotency on its end (T68 webhook handler).
 *
 * Pool dynamics (verified WP6 — resolved by design):
 *   - start() iterates the static pool loaded by BotManager.initialize().
 *     BotManager exposes no dynamic-add path (only initialize/removeFromPool),
 *     so every live bot is attached at startup and nothing is ever hot-added.
 *   - Session recovery (SESSION_EXPIRED → RECONNECTING → READY) reuses the same
 *     BotSession + TradeOfferManager instance, so the listener bound here
 *     survives reconnects without re-attaching.
 *   - The idempotent {@link attachToSession} hook is already in place for a
 *     future dynamic pool (T69 capacity scaling): when BotManager grows a
 *     hot-add path, it calls attachToSession(newSession) once. No re-attach is
 *     needed today — there is no dynamic pool to re-attach to.
 */
export class TradeOfferMonitor {
  private readonly log = rootLogger.child({ component: 'TradeOfferMonitor' });
  private readonly tradeOfferEndpoint: string;
  private readonly webhookSender: NonNullable<TradeOfferMonitorOptions['webhookSender']>;
  private readonly attached = new Set<string>();
  private readonly handledTransitions = new Map<string, number>();
  private started = false;

  constructor(
    private readonly botManager: BotManager,
    options: TradeOfferMonitorOptions = {},
  ) {
    this.tradeOfferEndpoint = options.tradeOfferEndpoint ?? DEFAULT_TRADE_OFFER_ENDPOINT;
    this.webhookSender = options.webhookSender ?? sendCallback;
  }

  start(): void {
    if (this.started) {
      this.log.warn('start() called twice, ignoring');
      return;
    }
    this.started = true;
    const sessions = this.botManager.allSessions();
    for (const session of sessions) {
      this.attachToSession(session);
    }
    this.log.info({ attached: this.attached.size }, 'TradeOfferMonitor attached to bot pool');
  }

  /**
   * Manually attach to a session — used for tests and (future) dynamic pool
   * growth. Idempotent per accountName so re-attaching is a no-op.
   */
  attachToSession(session: BotSession): void {
    if (this.attached.has(session.accountName)) {
      this.log.debug({ bot: session.accountName }, 'session already attached, skipping');
      return;
    }
    session.bindTradeOfferEvents({
      onSentOfferChanged: (offer, oldState) => {
        // Fire-and-forget so the EventEmitter loop is not blocked on webhook latency.
        this.handleSentOfferChanged(session, offer, oldState).catch((err) => {
          this.log.error(
            { err, bot: session.accountName, offerId: offer.id },
            'handleSentOfferChanged threw',
          );
        });
      },
      onPollFailure: (err) => {
        // 08 §2.7 — polling failures are transient; built-in poller retries
        // on its own schedule. We only log + expose via metrics-side later.
        this.log.warn(
          { err: err.message, bot: session.accountName },
          'TradeOfferManager pollFailure (built-in poller will retry)',
        );
      },
    });
    this.attached.add(session.accountName);
  }

  /** Snapshot of which bots have an active listener (test/observability hook). */
  attachedBots(): readonly string[] {
    return [...this.attached];
  }

  private async handleSentOfferChanged(
    session: BotSession,
    offer: TradeOffer,
    oldState: number,
  ): Promise<void> {
    const newState = offer.state;
    const eventName = TRADE_OFFER_STATE_EVENT_MAP.get(newState);
    if (!eventName) {
      this.log.debug(
        { bot: session.accountName, offerId: offer.id, oldState, newState },
        'sentOfferChanged with unmapped state, ignoring',
      );
      return;
    }
    if (!offer.id) {
      this.log.warn(
        { bot: session.accountName, newState },
        'sentOfferChanged with missing offer.id, ignoring',
      );
      return;
    }
    const last = this.handledTransitions.get(offer.id);
    if (last === newState) {
      this.log.debug(
        { bot: session.accountName, offerId: offer.id, newState },
        'duplicate transition, skipping webhook',
      );
      return;
    }
    this.handledTransitions.set(offer.id, newState);

    const data: TradeOfferStatusChangedData = {
      offerId: offer.id,
      partnerSteamId: offer.partner.getSteamID64(),
      botSteamId: session.getStatus().steamId,
      botAccountName: session.accountName,
      newState,
      oldState,
    };

    // T106a — on Accepted (state 3) resolve the post-settlement asset ids so
    // the backend can populate EscrowBotAssetId / DeliveredBuyerAssetId (the
    // ITEM_ESCROWED / ITEM_DELIVERED guards require them). On fetch failure we
    // still emit the event without ids — the backend logs + acks rather than
    // advancing, and reconciliation/admin recovers (never a silent stall).
    if (eventName === 'trade_offer.accepted') {
      const assetIds = await this.resolveExchangeAssetIds(session, offer);
      if (assetIds.receivedAssetId) data.receivedAssetId = assetIds.receivedAssetId;
      if (assetIds.deliveredAssetId) data.deliveredAssetId = assetIds.deliveredAssetId;
    }

    this.log.info(
      { event: eventName, bot: session.accountName, offerId: offer.id, oldState, newState },
      'Trade offer state changed',
    );
    await this.publish(eventName, data as unknown as Record<string, unknown>);
  }

  /**
   * Resolve the new (post-settlement) asset ids for an Accepted offer. The bot
   * RECEIVES on the escrow (SELLER_TO_BOT) leg → `receivedItems`; the bot SENDS
   * on the delivery / refund legs → `sentItems`. We surface both and let the
   * backend pick by the stored TradeOffer.Direction. Errors degrade to empty
   * (logged) so the webhook still fires.
   */
  private resolveExchangeAssetIds(
    session: BotSession,
    offer: TradeOffer,
  ): Promise<{ receivedAssetId?: string; deliveredAssetId?: string }> {
    return new Promise((resolve) => {
      try {
        offer.getExchangeDetails((err, _status, _tradeInitTime, receivedItems, sentItems) => {
          if (err) {
            this.log.warn(
              { err: err.message, bot: session.accountName, offerId: offer.id },
              'getExchangeDetails failed — emitting accepted event without asset ids',
            );
            resolve({});
            return;
          }
          resolve({
            receivedAssetId: firstNewAssetId(receivedItems),
            deliveredAssetId: firstNewAssetId(sentItems),
          });
        });
      } catch (err) {
        this.log.warn(
          { err: (err as Error).message, bot: session.accountName, offerId: offer.id },
          'getExchangeDetails threw — emitting accepted event without asset ids',
        );
        resolve({});
      }
    });
  }

  private async publish(event: TradeOfferEventName, data: Record<string, unknown>): Promise<void> {
    const payload: WebhookPayload = {
      event,
      timestamp: new Date().toISOString(),
      data,
    };
    const correlationId = crypto.randomUUID();
    try {
      await this.webhookSender(this.tradeOfferEndpoint, payload, correlationId);
    } catch (err) {
      // Backend handler is T68 — until then 404 is expected; log and move on.
      this.log.warn(
        { err: (err as Error).message, event, correlationId },
        'Trade offer status webhook send failed (backend handler is wired in T68)',
      );
    }
  }
}

/**
 * Pick the new (post-settlement) asset id of the first item in an exchange-
 * details slice. Skinora trades are single-item, so the first entry is the
 * relevant one. Falls back to the original `assetid` only if `new_assetid` is
 * absent, and returns undefined for an empty slice.
 */
function firstNewAssetId(items: ExchangeDetailsItem[] | undefined): string | undefined {
  const first = items?.[0];
  if (!first) return undefined;
  return first.new_assetid ?? first.assetid;
}
