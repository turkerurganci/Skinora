import { describe, it, expect } from 'vitest';
import {
  TRADE_OFFER_STATE_EVENT_MAP,
  type WebhookPayload,
  type BotEventName,
  type BotEventPayload,
  type BotSessionFailedData,
  type BotRemovedFromPoolData,
  type TradeOfferEventName,
  type TradeOfferStatusChangedData,
} from './WebhookPayloads.js';
import type { BotSessionStatus } from '../bot/BotSession.js';

/**
 * Contract test: pin the exact event names and payload shape that the
 * .NET backend will deserialize once T68 lands. Renaming an event here
 * without updating the backend would break the cross-service contract,
 * so this test acts as a tripwire.
 */
describe('Webhook payload contract', () => {
  const status: BotSessionStatus = {
    accountName: 'bot1',
    state: 'FAILED',
    lastTransitionAt: '2026-05-12T00:00:00Z',
    retryCount: 3,
    lastError: 'eresult=5',
  };

  it('pins the bot event name set', () => {
    const names: BotEventName[] = ['bot.session_failed', 'bot.removed_from_pool'];
    expect(names).toEqual(['bot.session_failed', 'bot.removed_from_pool']);
  });

  it('bot.session_failed payload has reason + status fields', () => {
    const data: BotSessionFailedData = { reason: 'login_failed', status };
    const payload: BotEventPayload<BotSessionFailedData> = {
      event: 'bot.session_failed',
      timestamp: '2026-05-12T00:00:00Z',
      data: { ...data },
    };
    expect(payload.event).toBe('bot.session_failed');
    expect(payload.data.reason).toBe('login_failed');
    expect((payload.data as unknown as BotSessionFailedData).status.accountName).toBe('bot1');
  });

  it('bot.removed_from_pool payload has accountName + reason + status fields', () => {
    const data: BotRemovedFromPoolData = {
      accountName: 'bot1',
      reason: 'session_recovery_failed',
      status,
    };
    const payload: BotEventPayload<BotRemovedFromPoolData> = {
      event: 'bot.removed_from_pool',
      timestamp: '2026-05-12T00:00:00Z',
      data: { ...data },
    };
    expect(payload.event).toBe('bot.removed_from_pool');
    expect(payload.data.accountName).toBe('bot1');
    expect(payload.data.reason).toBe('session_recovery_failed');
  });

  it('WebhookPayload envelope shape is preserved', () => {
    const generic: WebhookPayload = {
      event: 'bot.session_failed',
      timestamp: '2026-05-12T00:00:00Z',
      data: { foo: 'bar' },
    };
    expect(Object.keys(generic).sort()).toEqual(['data', 'event', 'timestamp']);
  });
});

/**
 * T66 — pin the contract for the 5 trade offer status events (08 §2.4) and
 * the ETradeOfferState → event-name mapping. Backend (T68) deserializes by
 * event name, so a rename here without a backend update breaks the handshake.
 */
describe('T66 trade offer status webhook contract', () => {
  it('pins the full TradeOfferEventName union', () => {
    const names: TradeOfferEventName[] = [
      'trade_offer.sent',
      'trade_offer.failed',
      'trade_offer.accepted',
      'trade_offer.declined',
      'trade_offer.expired',
      'trade_offer.countered',
      'trade_offer.invalid_items',
    ];
    expect(names).toHaveLength(7);
  });

  it('TRADE_OFFER_STATE_EVENT_MAP maps the 08 §2.4 status codes', () => {
    expect(TRADE_OFFER_STATE_EVENT_MAP.get(3)).toBe('trade_offer.accepted');
    expect(TRADE_OFFER_STATE_EVENT_MAP.get(4)).toBe('trade_offer.countered');
    expect(TRADE_OFFER_STATE_EVENT_MAP.get(5)).toBe('trade_offer.expired');
    expect(TRADE_OFFER_STATE_EVENT_MAP.get(7)).toBe('trade_offer.declined');
    expect(TRADE_OFFER_STATE_EVENT_MAP.get(8)).toBe('trade_offer.invalid_items');
    expect(TRADE_OFFER_STATE_EVENT_MAP.size).toBe(5);
  });

  it.each([1, 2, 6, 9, 10, 11])('state %i is intentionally not mapped', (state) => {
    expect(TRADE_OFFER_STATE_EVENT_MAP.has(state)).toBe(false);
  });

  it('TradeOfferStatusChangedData has the wire fields backend expects', () => {
    const data: TradeOfferStatusChangedData = {
      offerId: 'offer-123',
      partnerSteamId: '76561198000000999',
      botSteamId: '76561198000000001',
      botAccountName: 'bot1',
      newState: 3,
      oldState: 2,
    };
    expect(data.offerId).toBe('offer-123');
    expect(data.newState).toBe(3);
    expect(data.oldState).toBe(2);
  });

  it('botSteamId is optional (set only once steam-user emits accountInfo)', () => {
    const data: TradeOfferStatusChangedData = {
      offerId: 'offer-123',
      partnerSteamId: '76561198000000999',
      botAccountName: 'bot1',
      newState: 7,
      oldState: 2,
    };
    expect(data.botSteamId).toBeUndefined();
  });

  it.each([
    ['trade_offer.accepted', 3],
    ['trade_offer.declined', 7],
    ['trade_offer.expired', 5],
    ['trade_offer.countered', 4],
    ['trade_offer.invalid_items', 8],
  ] as const)('%s envelope round-trips a status payload', (event, newState) => {
    const data: TradeOfferStatusChangedData = {
      offerId: 'offer-N',
      partnerSteamId: '76561198000000999',
      botAccountName: 'bot1',
      newState,
      oldState: 2,
    };
    const payload: WebhookPayload = {
      event,
      timestamp: '2026-05-13T00:00:00Z',
      data: data as unknown as Record<string, unknown>,
    };
    expect(payload.event).toBe(event);
    expect((payload.data as unknown as TradeOfferStatusChangedData).newState).toBe(newState);
  });
});
