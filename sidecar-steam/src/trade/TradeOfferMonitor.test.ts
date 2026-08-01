import { EventEmitter } from 'events';
import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../logger.js', () => ({
  logger: {
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
    debug: vi.fn(),
    child: vi.fn().mockReturnThis(),
  },
}));

import { TradeOfferMonitor } from './TradeOfferMonitor.js';
import type { BotManager } from '../bot/BotManager.js';
import type { BotSession } from '../bot/BotSession.js';
import type { TradeOfferEventHandler } from './types.js';
import type { TradeOffer } from 'steam-tradeoffer-manager';
import type { WebhookPayload } from '../webhook/WebhookPayloads.js';

interface FakeOfferOptions {
  id?: string;
  state: number;
  partnerSteamId?: string;
}

function makeOffer(opts: FakeOfferOptions): TradeOffer {
  return {
    id: opts.id ?? 'offer-123',
    state: opts.state,
    partner: { getSteamID64: () => opts.partnerSteamId ?? '76561198000000999' },
    itemsToGive: [],
    itemsToReceive: [],
    message: '',
    addMyItem: () => true,
    addTheirItem: () => true,
    setMessage: () => undefined,
    send: () => undefined,
    cancel: () => undefined,
  } as unknown as TradeOffer;
}

/**
 * Fake bot session that captures the handler registered via
 * bindTradeOfferEvents so tests can drive sentOfferChanged synchronously.
 */
function makeBot(
  accountName: string,
  overrides: { steamId?: string } = {},
): BotSession & {
  fire: (offer: TradeOffer, oldState: number) => void;
  firePollFailure: (err: Error) => void;
  handler: () => TradeOfferEventHandler | undefined;
} {
  let captured: TradeOfferEventHandler | undefined;
  const status = {
    accountName,
    state: 'READY' as const,
    steamId: overrides.steamId ?? '76561198000000001',
    lastTransitionAt: new Date().toISOString(),
    retryCount: 0,
  };
  const session = {
    accountName,
    isReady: () => true,
    getStatus: () => status,
    bindTradeOfferEvents: vi.fn((h: TradeOfferEventHandler) => {
      captured = h;
    }),
  };
  const enriched = Object.assign(session, {
    fire(offer: TradeOffer, oldState: number) {
      captured?.onSentOfferChanged(offer, oldState);
    },
    firePollFailure(err: Error) {
      captured?.onPollFailure(err);
    },
    handler: () => captured,
  });
  return enriched as unknown as ReturnType<typeof makeBot>;
}

function makeBotManager(bots: BotSession[]): BotManager {
  return {
    allSessions: () => bots,
    selectBot: () => bots[0] ?? null,
  } as unknown as BotManager;
}

const recordedWebhook = vi.fn().mockResolvedValue(undefined);

beforeEach(() => {
  vi.clearAllMocks();
  recordedWebhook.mockResolvedValue(undefined);
});

describe('TradeOfferMonitor.start', () => {
  it('attaches handlers to every session in the pool', () => {
    const bot1 = makeBot('bot1');
    const bot2 = makeBot('bot2');
    const monitor = new TradeOfferMonitor(makeBotManager([bot1, bot2]), {
      webhookSender: recordedWebhook,
    });

    monitor.start();

    expect(bot1.bindTradeOfferEvents).toHaveBeenCalledOnce();
    expect(bot2.bindTradeOfferEvents).toHaveBeenCalledOnce();
    expect(monitor.attachedBots()).toEqual(['bot1', 'bot2']);
  });

  it('is idempotent — second start() is a no-op', () => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });

    monitor.start();
    monitor.start();

    expect(bot.bindTradeOfferEvents).toHaveBeenCalledOnce();
    expect(monitor.attachedBots()).toEqual(['bot1']);
  });

  it('attachToSession is idempotent per accountName', () => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([]), {
      webhookSender: recordedWebhook,
    });

    monitor.attachToSession(bot);
    monitor.attachToSession(bot);

    expect(bot.bindTradeOfferEvents).toHaveBeenCalledOnce();
  });
});

describe('TradeOfferMonitor.sentOfferChanged → webhook mapping (08 §2.4)', () => {
  const baseFlow = (state: number) => async (event: string) => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    bot.fire(makeOffer({ id: 'offer-X', state }), 2);
    // Allow the fire-and-forget promise inside onSentOfferChanged to resolve.
    await new Promise((r) => setImmediate(r));

    expect(recordedWebhook).toHaveBeenCalledOnce();
    const [endpoint, payload] = recordedWebhook.mock.calls[0] as [string, WebhookPayload, string];
    expect(endpoint).toBe('/api/v1/webhooks/steam/trade-events');
    expect(payload.event).toBe(event);
    expect(payload.data).toMatchObject({
      offerId: 'offer-X',
      newState: state,
      oldState: 2,
      partnerSteamId: '76561198000000999',
      botAccountName: 'bot1',
      botSteamId: '76561198000000001',
    });
  };

  it('state 3 Accepted → trade_offer.accepted', async () => {
    await baseFlow(3)('trade_offer.accepted');
  });

  it('state 4 Countered → trade_offer.countered', async () => {
    await baseFlow(4)('trade_offer.countered');
  });

  it('state 5 Expired → trade_offer.expired', async () => {
    await baseFlow(5)('trade_offer.expired');
  });

  it('state 7 Declined → trade_offer.declined', async () => {
    await baseFlow(7)('trade_offer.declined');
  });

  it('state 8 InvalidItems → trade_offer.invalid_items', async () => {
    await baseFlow(8)('trade_offer.invalid_items');
  });
});

describe('TradeOfferMonitor.sentOfferChanged — ignored states', () => {
  it.each([
    [1, 'Invalid'],
    [2, 'Active'],
    [6, 'Canceled'],
    [9, 'CreatedNeedsConfirmation'],
    [10, 'CanceledBySecondFactor'],
    [11, 'InEscrow'],
  ])('state %i (%s) does not emit a webhook', async (state) => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    bot.fire(makeOffer({ id: 'offer-Y', state }), 2);
    await new Promise((r) => setImmediate(r));

    expect(recordedWebhook).not.toHaveBeenCalled();
  });
});

describe('TradeOfferMonitor idempotency', () => {
  it('duplicate sentOfferChanged at same newState emits once', async () => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    const offer = makeOffer({ id: 'offer-Z', state: 3 });
    bot.fire(offer, 2);
    bot.fire(offer, 2);
    await new Promise((r) => setImmediate(r));

    expect(recordedWebhook).toHaveBeenCalledOnce();
  });

  it('different offerIds at the same newState each emit once', async () => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    bot.fire(makeOffer({ id: 'offer-A', state: 3 }), 2);
    bot.fire(makeOffer({ id: 'offer-B', state: 3 }), 2);
    await new Promise((r) => setImmediate(r));

    expect(recordedWebhook).toHaveBeenCalledTimes(2);
  });

  it('skips offers with missing offer.id (defensive)', async () => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    const offer = makeOffer({ state: 3 });
    (offer as { id?: string }).id = undefined;
    bot.fire(offer, 2);
    await new Promise((r) => setImmediate(r));

    expect(recordedWebhook).not.toHaveBeenCalled();
  });
});

describe('TradeOfferMonitor pollFailure', () => {
  it('logs but does not propagate or emit a webhook (08 §2.7 — built-in poller retries)', async () => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    expect(() => bot.firePollFailure(new Error('econn reset'))).not.toThrow();
    await new Promise((r) => setImmediate(r));

    expect(recordedWebhook).not.toHaveBeenCalled();
  });
});

describe('TradeOfferMonitor webhook error handling', () => {
  it('swallows webhook send failures (T68 handler not yet wired)', async () => {
    const failingSender = vi.fn().mockRejectedValue(new Error('404 from backend'));
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: failingSender,
    });
    monitor.start();

    bot.fire(makeOffer({ id: 'offer-FAIL', state: 7 }), 2);
    await new Promise((r) => setImmediate(r));
    await new Promise((r) => setImmediate(r));

    expect(failingSender).toHaveBeenCalledOnce();
  });
});

describe('TradeOfferMonitor — T106a asset-id capture (Accepted only)', () => {
  function makeOfferWithExchange(
    state: number,
    exchange: {
      err?: Error;
      receivedItems?: Array<{ new_assetid?: string; assetid: string }>;
      sentItems?: Array<{ new_assetid?: string; assetid: string }>;
    },
  ): TradeOffer {
    const offer = makeOffer({ id: 'offer-EX', state });
    (offer as unknown as { getExchangeDetails: unknown }).getExchangeDetails = (
      cb: (
        err: Error | null,
        status: number,
        tradeInitTime: Date,
        receivedItems: unknown[],
        sentItems: unknown[],
      ) => void,
    ) =>
      cb(
        exchange.err ?? null,
        3,
        new Date(0),
        exchange.receivedItems ?? [],
        exchange.sentItems ?? [],
      );
    return offer;
  }

  it('includes receivedAssetId + deliveredAssetId from getExchangeDetails', async () => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    bot.fire(
      makeOfferWithExchange(3, {
        receivedItems: [{ new_assetid: 'recv-1', assetid: 'old-r' }],
        sentItems: [{ new_assetid: 'deliv-1', assetid: 'old-s' }],
      }),
      2,
    );
    await new Promise((r) => setImmediate(r));

    const [, payload] = recordedWebhook.mock.calls[0] as [string, WebhookPayload, string];
    expect(payload.data).toMatchObject({ receivedAssetId: 'recv-1', deliveredAssetId: 'deliv-1' });
  });

  it('emits without asset ids when getExchangeDetails errors', async () => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    bot.fire(makeOfferWithExchange(3, { err: new Error('exchange unavailable') }), 2);
    await new Promise((r) => setImmediate(r));

    expect(recordedWebhook).toHaveBeenCalledOnce();
    const [, payload] = recordedWebhook.mock.calls[0] as [string, WebhookPayload, string];
    expect(payload.data).not.toHaveProperty('receivedAssetId');
    expect(payload.data).not.toHaveProperty('deliveredAssetId');
  });

  it('does not call getExchangeDetails for non-accept states', async () => {
    const bot = makeBot('bot1');
    const monitor = new TradeOfferMonitor(makeBotManager([bot]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    const getExchangeDetails = vi.fn();
    const offer = makeOffer({ id: 'offer-decline', state: 7 });
    (offer as unknown as { getExchangeDetails: unknown }).getExchangeDetails = getExchangeDetails;
    bot.fire(offer, 2);
    await new Promise((r) => setImmediate(r));

    expect(getExchangeDetails).not.toHaveBeenCalled();
    const [, payload] = recordedWebhook.mock.calls[0] as [string, WebhookPayload, string];
    expect(payload.data).not.toHaveProperty('receivedAssetId');
  });
});

/**
 * Integration-style: verify the BotSession.bindTradeOfferEvents bridge actually
 * forwards real EventEmitter events to the monitor handler.
 */
describe('TradeOfferMonitor via BotSession.bindTradeOfferEvents bridge', () => {
  it('real EventEmitter sentOfferChanged is forwarded end-to-end', async () => {
    const tradeManager = new EventEmitter();
    const realishSession = {
      accountName: 'bot-integration',
      isReady: () => true,
      getStatus: () => ({
        accountName: 'bot-integration',
        state: 'READY' as const,
        steamId: '76561198000000777',
        lastTransitionAt: new Date().toISOString(),
        retryCount: 0,
      }),
      bindTradeOfferEvents(handler: TradeOfferEventHandler) {
        tradeManager.on('sentOfferChanged', (offer: TradeOffer, oldState: number) =>
          handler.onSentOfferChanged(offer, oldState),
        );
        tradeManager.on('pollFailure', (err: Error) => handler.onPollFailure(err));
      },
    } as unknown as BotSession;

    const monitor = new TradeOfferMonitor(makeBotManager([realishSession]), {
      webhookSender: recordedWebhook,
    });
    monitor.start();

    tradeManager.emit('sentOfferChanged', makeOffer({ id: 'offer-INT', state: 5 }), 2);
    await new Promise((r) => setImmediate(r));

    expect(recordedWebhook).toHaveBeenCalledOnce();
    const [, payload] = recordedWebhook.mock.calls[0] as [string, WebhookPayload, string];
    expect(payload.event).toBe('trade_offer.expired');
    expect(payload.data).toMatchObject({ offerId: 'offer-INT', botSteamId: '76561198000000777' });
  });
});
