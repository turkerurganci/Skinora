import crypto from 'crypto';
import type { TradeOffer, TradeOfferError } from 'steam-tradeoffer-manager';
import { logger as rootLogger } from '../logger.js';
import { sendCallback } from '../webhook/WebhookClient.js';
import type {
  TradeOfferFailedData,
  TradeOfferSentData,
  WebhookPayload,
} from '../webhook/WebhookPayloads.js';
import type { BotManager } from '../bot/BotManager.js';
import type { BotSession } from '../bot/BotSession.js';
import {
  TRADE_OFFER_BACKOFF_MS,
  TRANSIENT_NETWORK_CODES,
  TRANSIENT_TRADE_ERESULTS,
  requiresMobileConfirmation,
  type SendTradeOfferRequest,
  type SendTradeOfferResponse,
} from './types.js';

const DEFAULT_TRADE_OFFER_ENDPOINT = '/api/v1/sidecar/steam/trade-offer-events';

export interface TradeOfferServiceOptions {
  /** Webhook endpoint backend will receive trade offer events on (T66 consumer). */
  tradeOfferEndpoint?: string;
  /** Override the webhook transport (used by tests). */
  webhookSender?: (
    endpoint: string,
    payload: WebhookPayload,
    correlationId: string,
  ) => Promise<void>;
  /** Override the backoff schedule (used by tests for fast assertion). */
  backoffMs?: readonly number[];
  /** Optional sleep injection (used by tests to bypass real timers). */
  sleep?: (ms: number) => Promise<void>;
}

/**
 * Steam trade offer dispatcher (T65).
 *
 * Lifecycle per offer:
 *   1. Pick a READY bot via BotManager.selectBot()
 *   2. Build TradeOffer (direction → addMyItem vs addTheirItem)
 *   3. send() with 08 §2.7 retry: 5s/15s/45s on transient errors only
 *   4. If status === 'pending' AND direction sends items out → auto-confirm
 *      via steamcommunity.acceptConfirmationForObject (deterministic; the 20s
 *      checker on BotSession remains a fallback)
 *   5. Emit trade_offer.sent webhook to backend
 *   6. On exhausted retries OR permanent eresult → trade_offer.failed
 *
 * Counter-offer cancellation, polling, and Accepted/Declined/Expired/Countered
 * transitions are intentionally deferred to T66 (08 §2.4 polling). T66 wires up
 * the steam-tradeoffer-manager `sentOfferChanged` event and toggles
 * `pollInterval` from `-1` to `10_000`.
 */
export class TradeOfferService {
  private readonly log = rootLogger.child({ component: 'TradeOfferService' });
  private readonly tradeOfferEndpoint: string;
  private readonly webhookSender: NonNullable<TradeOfferServiceOptions['webhookSender']>;
  private readonly backoffMs: readonly number[];
  private readonly sleep: NonNullable<TradeOfferServiceOptions['sleep']>;

  constructor(
    private readonly botManager: BotManager,
    options: TradeOfferServiceOptions = {},
  ) {
    this.tradeOfferEndpoint = options.tradeOfferEndpoint ?? DEFAULT_TRADE_OFFER_ENDPOINT;
    this.webhookSender = options.webhookSender ?? sendCallback;
    this.backoffMs = options.backoffMs ?? TRADE_OFFER_BACKOFF_MS;
    this.sleep = options.sleep ?? defaultSleep;
  }

  async sendOffer(request: SendTradeOfferRequest): Promise<SendTradeOfferResponse> {
    this.validateRequest(request);

    // T106a — honour the backend's escrow-bot hint so the delivery + refund
    // legs route through the bot that actually holds the item; falls back to
    // round-robin inside BotManager when the hint is absent / not READY.
    const bot = this.botManager.selectBot(request.botAccountName);
    if (!bot) {
      const reason = 'No READY bots available';
      this.log.error({ transactionId: request.transactionId }, reason);
      await this.emitFailed(request, undefined, reason, 0, false, 0);
      return { status: 'failed', reason, retryable: true, attempts: 0 };
    }

    const tradeManager = bot.getTradeManager();
    if (!tradeManager) {
      const reason = `Selected bot ${bot.accountName} is not READY`;
      this.log.error({ transactionId: request.transactionId, bot: bot.accountName }, reason);
      await this.emitFailed(request, bot.accountName, reason, 0, true, 0);
      return { status: 'failed', reason, retryable: true, attempts: 0 };
    }

    for (let attempt = 1; attempt <= this.backoffMs.length + 1; attempt++) {
      const sendResult = await this.attemptSend(
        bot,
        tradeManager.createOffer(request.partnerSteamId),
        request,
      );

      if (sendResult.ok) {
        const confirmed = await this.maybeConfirm(
          bot,
          sendResult.offerId,
          sendResult.status,
          request,
        );
        const finalStatus = confirmed ? 'confirmed' : sendResult.status;
        await this.emitSent(request, bot, sendResult.offerId, finalStatus, attempt);
        return {
          status: finalStatus,
          offerId: sendResult.offerId,
          attempts: attempt,
        };
      }

      const retryable = isTransientError(sendResult.error);
      const exhausted = attempt > this.backoffMs.length;
      if (!retryable || exhausted) {
        const reason = sendResult.error.message;
        this.log.error(
          {
            transactionId: request.transactionId,
            attempt,
            retryable,
            exhausted,
            eresult: sendResult.error.eresult,
          },
          'Trade offer send failed permanently',
        );
        await this.emitFailed(
          request,
          bot.accountName,
          reason,
          sendResult.error.eresult,
          retryable,
          attempt,
        );
        return {
          status: 'failed',
          reason,
          retryable,
          attempts: attempt,
        };
      }

      const waitMs = this.backoffMs[attempt - 1];
      this.log.warn(
        {
          transactionId: request.transactionId,
          attempt,
          waitMs,
          eresult: sendResult.error.eresult,
        },
        'Trade offer send transient failure — backing off',
      );
      await this.sleep(waitMs);
    }

    // Unreachable: the loop returns inside every branch. Defensive guard for the compiler.
    throw new Error('TradeOfferService.sendOffer loop fell through');
  }

  private validateRequest(request: SendTradeOfferRequest): void {
    if (!request.transactionId) throw new Error('transactionId is required');
    if (!request.partnerSteamId) throw new Error('partnerSteamId is required');
    if (!Array.isArray(request.items) || request.items.length === 0) {
      throw new Error('items must be a non-empty array');
    }
  }

  private async attemptSend(
    _bot: BotSession,
    offer: TradeOffer,
    request: SendTradeOfferRequest,
  ): Promise<SendAttemptResult> {
    // Direction routing — wrong side ≡ silent data loss, surface as validation error.
    const useTheirItems = request.direction === 'SELLER_TO_BOT';
    for (const item of request.items) {
      const accepted = useTheirItems ? offer.addTheirItem(item) : offer.addMyItem(item);
      if (!accepted) {
        return {
          ok: false,
          error: Object.assign(new Error(`Item rejected by trade offer builder: ${item.assetid}`), {
            cause: 'ItemAddFailed',
          }) as TradeOfferError,
        };
      }
    }

    if (request.message) offer.setMessage(request.message);

    return new Promise<SendAttemptResult>((resolve) => {
      offer.send((err, status) => {
        if (err) {
          resolve({ ok: false, error: err });
          return;
        }
        if (!offer.id) {
          resolve({
            ok: false,
            error: Object.assign(new Error('Offer.send returned no id'), {
              cause: 'MissingOfferId',
            }) as TradeOfferError,
          });
          return;
        }
        resolve({ ok: true, offerId: offer.id, status });
      });
    });
  }

  private async maybeConfirm(
    bot: BotSession,
    offerId: string,
    status: 'pending' | 'sent',
    request: SendTradeOfferRequest,
  ): Promise<boolean> {
    if (status !== 'pending') return false;
    if (!requiresMobileConfirmation(request.direction)) return false;
    try {
      await bot.acceptTradeConfirmation(offerId);
      return true;
    } catch (err) {
      this.log.warn(
        { err: (err as Error).message, offerId, bot: bot.accountName },
        'Mobile confirmation failed inline — 20s checker will retry',
      );
      return false;
    }
  }

  private async emitSent(
    request: SendTradeOfferRequest,
    bot: BotSession,
    offerId: string,
    status: TradeOfferSentData['status'],
    attempts: number,
  ): Promise<void> {
    const data: TradeOfferSentData = {
      transactionId: request.transactionId,
      direction: request.direction,
      partnerSteamId: request.partnerSteamId,
      botSteamId: bot.getStatus().steamId,
      botAccountName: bot.accountName,
      offerId,
      status,
      attempts,
    };
    await this.publish('trade_offer.sent', data as unknown as Record<string, unknown>);
  }

  private async emitFailed(
    request: SendTradeOfferRequest,
    botAccountName: string | undefined,
    reason: string,
    eresult: number | undefined,
    retryable: boolean,
    attempts: number,
  ): Promise<void> {
    const data: TradeOfferFailedData = {
      transactionId: request.transactionId,
      direction: request.direction,
      partnerSteamId: request.partnerSteamId,
      botAccountName,
      reason,
      eresult,
      retryable,
      attempts,
    };
    await this.publish('trade_offer.failed', data as unknown as Record<string, unknown>);
  }

  private async publish(event: string, data: Record<string, unknown>): Promise<void> {
    const payload: WebhookPayload = {
      event,
      timestamp: new Date().toISOString(),
      data,
    };
    const correlationId = crypto.randomUUID();
    try {
      await this.webhookSender(this.tradeOfferEndpoint, payload, correlationId);
    } catch (err) {
      // Backend handler lands in T66/T68 — until then 404 is expected.
      this.log.warn(
        { err: (err as Error).message, event, correlationId },
        'Trade offer webhook send failed (backend handler is wired in T66/T68)',
      );
    }
  }
}

type SendAttemptResult =
  | { ok: true; offerId: string; status: 'pending' | 'sent' }
  | { ok: false; error: TradeOfferError };

function isTransientError(err: TradeOfferError): boolean {
  if (typeof err.eresult === 'number' && TRANSIENT_TRADE_ERESULTS.has(err.eresult)) {
    return true;
  }
  const code = (err as { code?: string }).code;
  if (typeof code === 'string' && TRANSIENT_NETWORK_CODES.has(code)) {
    return true;
  }
  return false;
}

function defaultSleep(ms: number): Promise<void> {
  return new Promise((resolve) => {
    const t = setTimeout(resolve, ms);
    t.unref?.();
  });
}
