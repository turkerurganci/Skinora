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

import { TradeOfferService } from './TradeOfferService.js';
import type { SendTradeOfferRequest } from './types.js';
import type { BotManager } from '../bot/BotManager.js';
import type { BotSession } from '../bot/BotSession.js';
import type { ItemDescriptor, TradeOffer, TradeOfferError } from 'steam-tradeoffer-manager';

type SendCallback = (err: TradeOfferError | null, status: 'pending' | 'sent') => void;

interface FakeOfferOptions {
  /** Sequence of results per send() call. If exhausted, last result repeats. */
  sendResults: Array<
    { ok: true; id: string; status: 'pending' | 'sent' } | { ok: false; error: TradeOfferError }
  >;
}

function makeOffer(opts: FakeOfferOptions): TradeOffer & {
  addMyItem: ReturnType<typeof vi.fn>;
  addTheirItem: ReturnType<typeof vi.fn>;
  setMessage: ReturnType<typeof vi.fn>;
  send: ReturnType<typeof vi.fn>;
} {
  const results = [...opts.sendResults];
  const offer = {
    id: undefined as string | undefined,
    state: 0,
    partner: { getSteamID64: () => '76561198000000999' },
    itemsToGive: [] as ItemDescriptor[],
    itemsToReceive: [] as ItemDescriptor[],
    message: '',
    addMyItem: vi.fn((item: ItemDescriptor) => {
      offer.itemsToGive.push(item);
      return true;
    }),
    addTheirItem: vi.fn((item: ItemDescriptor) => {
      offer.itemsToReceive.push(item);
      return true;
    }),
    setMessage: vi.fn((m: string) => {
      offer.message = m;
    }),
    send: vi.fn((cb: SendCallback) => {
      const next = results.length > 1 ? results.shift()! : results[0];
      if (!next) {
        cb(Object.assign(new Error('no send result configured'), {}) as TradeOfferError, 'sent');
        return;
      }
      if (next.ok) {
        offer.id = next.id;
        cb(null, next.status);
      } else {
        cb(next.error, 'sent');
      }
    }),
    cancel: vi.fn((cb: (err: TradeOfferError | null) => void) => cb(null)),
  };
  return offer as unknown as ReturnType<typeof makeOffer>;
}

function makeBot(
  offers: ReturnType<typeof makeOffer>[],
  overrides: Partial<{
    ready: boolean;
    confirm: ReturnType<typeof vi.fn>;
    accountName: string;
  }> = {},
): BotSession {
  const offerQueue = [...offers];
  const ready = overrides.ready ?? true;
  const confirm = overrides.confirm ?? vi.fn().mockResolvedValue(undefined);
  return {
    accountName: overrides.accountName ?? 'bot1',
    isReady: () => ready,
    getStatus: () => ({
      accountName: overrides.accountName ?? 'bot1',
      state: ready ? 'READY' : 'FAILED',
      steamId: '76561198000000001',
      lastTransitionAt: new Date().toISOString(),
      retryCount: 0,
    }),
    getTradeManager: () =>
      ready
        ? {
            createOffer: vi.fn(() => offerQueue.shift() ?? offers[offers.length - 1]),
          }
        : null,
    acceptTradeConfirmation: confirm,
  } as unknown as BotSession;
}

function makeBotManager(bot: BotSession | null): BotManager {
  return {
    selectBot: vi.fn(() => bot),
  } as unknown as BotManager;
}

const baseRequest: SendTradeOfferRequest = {
  transactionId: 'tx-1',
  direction: 'BOT_TO_BUYER',
  partnerSteamId: '76561198000000999',
  items: [{ assetid: 'a1', appid: 730, contextid: '2' }],
};

const recordedWebhook = vi.fn().mockResolvedValue(undefined);

beforeEach(() => {
  vi.clearAllMocks();
  recordedWebhook.mockResolvedValue(undefined);
});

describe('TradeOfferService.sendOffer', () => {
  it('SELLER_TO_BOT uses addTheirItem and does not call mobile confirmation', async () => {
    const offer = makeOffer({ sendResults: [{ ok: true, id: 'offer-1', status: 'sent' }] });
    const bot = makeBot([offer]);
    const confirm = bot.acceptTradeConfirmation as unknown as ReturnType<typeof vi.fn>;
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
    });

    const result = await service.sendOffer({
      ...baseRequest,
      direction: 'SELLER_TO_BOT',
    });

    expect(offer.addTheirItem).toHaveBeenCalledWith(baseRequest.items[0]);
    expect(offer.addMyItem).not.toHaveBeenCalled();
    expect(confirm).not.toHaveBeenCalled();
    expect(result.status).toBe('sent');
    expect(result.offerId).toBe('offer-1');
    expect(recordedWebhook).toHaveBeenCalledOnce();
    const [, payload] = recordedWebhook.mock.calls[0];
    expect(payload.event).toBe('trade_offer.sent');
    expect(payload.data).toMatchObject({
      direction: 'SELLER_TO_BOT',
      offerId: 'offer-1',
      status: 'sent',
    });
  });

  it('BOT_TO_BUYER uses addMyItem and auto-confirms when status is pending', async () => {
    const offer = makeOffer({ sendResults: [{ ok: true, id: 'offer-2', status: 'pending' }] });
    const bot = makeBot([offer]);
    const confirm = bot.acceptTradeConfirmation as unknown as ReturnType<typeof vi.fn>;
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
    });

    const result = await service.sendOffer(baseRequest);

    expect(offer.addMyItem).toHaveBeenCalledWith(baseRequest.items[0]);
    expect(offer.addTheirItem).not.toHaveBeenCalled();
    expect(confirm).toHaveBeenCalledWith('offer-2');
    expect(result.status).toBe('confirmed');
    expect(result.offerId).toBe('offer-2');
    const [, payload] = recordedWebhook.mock.calls[0];
    expect(payload.data.status).toBe('confirmed');
  });

  it('BOT_TO_SELLER_REFUND uses addMyItem and auto-confirms', async () => {
    const offer = makeOffer({ sendResults: [{ ok: true, id: 'offer-3', status: 'pending' }] });
    const bot = makeBot([offer]);
    const confirm = bot.acceptTradeConfirmation as unknown as ReturnType<typeof vi.fn>;
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
    });

    await service.sendOffer({ ...baseRequest, direction: 'BOT_TO_SELLER_REFUND' });

    expect(offer.addMyItem).toHaveBeenCalled();
    expect(confirm).toHaveBeenCalledWith('offer-3');
  });

  it('does not call mobile confirmation when send status is already sent', async () => {
    const offer = makeOffer({ sendResults: [{ ok: true, id: 'offer-4', status: 'sent' }] });
    const bot = makeBot([offer]);
    const confirm = bot.acceptTradeConfirmation as unknown as ReturnType<typeof vi.fn>;
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
    });

    await service.sendOffer(baseRequest);

    expect(confirm).not.toHaveBeenCalled();
    const [, payload] = recordedWebhook.mock.calls[0];
    expect(payload.data.status).toBe('sent');
  });

  it('still emits sent webhook even if mobile confirmation fails (20s checker is fallback)', async () => {
    const offer = makeOffer({ sendResults: [{ ok: true, id: 'offer-5', status: 'pending' }] });
    const failingConfirm = vi.fn().mockRejectedValue(new Error('confirm failed'));
    const bot = makeBot([offer], { confirm: failingConfirm });
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
    });

    const result = await service.sendOffer(baseRequest);

    expect(failingConfirm).toHaveBeenCalledWith('offer-5');
    expect(result.status).toBe('pending');
    const [, payload] = recordedWebhook.mock.calls[0];
    expect(payload.event).toBe('trade_offer.sent');
    expect(payload.data.status).toBe('pending');
  });

  it('retries transient eresult (84 RateLimitExceeded) with 08 §2.7 backoff', async () => {
    const transient = Object.assign(new Error('rate limited'), { eresult: 84 }) as TradeOfferError;
    const offer = makeOffer({
      sendResults: [
        { ok: false, error: transient },
        { ok: false, error: transient },
        { ok: true, id: 'offer-6', status: 'sent' },
      ],
    });
    const bot = makeBot([offer]);
    const sleep = vi.fn().mockResolvedValue(undefined);
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
      backoffMs: [5_000, 15_000, 45_000],
      sleep,
    });

    const result = await service.sendOffer(baseRequest);

    expect(offer.send).toHaveBeenCalledTimes(3);
    expect(sleep).toHaveBeenNthCalledWith(1, 5_000);
    expect(sleep).toHaveBeenNthCalledWith(2, 15_000);
    expect(result.status).toBe('sent');
    expect(result.attempts).toBe(3);
  });

  it('retries transient network code (ECONNRESET)', async () => {
    const netErr = Object.assign(new Error('reset'), { code: 'ECONNRESET' }) as TradeOfferError;
    const offer = makeOffer({
      sendResults: [
        { ok: false, error: netErr },
        { ok: true, id: 'offer-7', status: 'sent' },
      ],
    });
    const bot = makeBot([offer]);
    const sleep = vi.fn().mockResolvedValue(undefined);
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
      backoffMs: [5_000],
      sleep,
    });

    const result = await service.sendOffer(baseRequest);

    expect(result.status).toBe('sent');
    expect(sleep).toHaveBeenCalledWith(5_000);
  });

  it('does not retry permanent eresult (15 AccessDenied)', async () => {
    const permanent = Object.assign(new Error('access denied'), { eresult: 15 }) as TradeOfferError;
    const offer = makeOffer({ sendResults: [{ ok: false, error: permanent }] });
    const bot = makeBot([offer]);
    const sleep = vi.fn().mockResolvedValue(undefined);
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
      sleep,
    });

    const result = await service.sendOffer(baseRequest);

    expect(offer.send).toHaveBeenCalledTimes(1);
    expect(sleep).not.toHaveBeenCalled();
    expect(result.status).toBe('failed');
    expect(result.retryable).toBe(false);
    const [, payload] = recordedWebhook.mock.calls[0];
    expect(payload.event).toBe('trade_offer.failed');
    expect(payload.data).toMatchObject({
      reason: 'access denied',
      eresult: 15,
      retryable: false,
    });
  });

  it('marks failed with retryable=true when transient retries exhaust', async () => {
    const transient = Object.assign(new Error('timeout'), { eresult: 16 }) as TradeOfferError;
    const offer = makeOffer({
      sendResults: [
        { ok: false, error: transient },
        { ok: false, error: transient },
        { ok: false, error: transient },
        { ok: false, error: transient },
      ],
    });
    const bot = makeBot([offer]);
    const sleep = vi.fn().mockResolvedValue(undefined);
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
      backoffMs: [1, 1, 1],
      sleep,
    });

    const result = await service.sendOffer(baseRequest);

    expect(offer.send).toHaveBeenCalledTimes(4);
    expect(result.status).toBe('failed');
    expect(result.retryable).toBe(true);
    const [, payload] = recordedWebhook.mock.calls[0];
    expect(payload.data.retryable).toBe(true);
  });

  it('emits trade_offer.failed when no bot is READY', async () => {
    const service = new TradeOfferService(makeBotManager(null), {
      webhookSender: recordedWebhook,
    });

    const result = await service.sendOffer(baseRequest);

    expect(result.status).toBe('failed');
    expect(result.retryable).toBe(true);
    const [, payload] = recordedWebhook.mock.calls[0];
    expect(payload.event).toBe('trade_offer.failed');
    expect(payload.data.reason).toMatch(/No READY bots/);
  });

  it('rejects request with empty items', async () => {
    const offer = makeOffer({ sendResults: [{ ok: true, id: 'x', status: 'sent' }] });
    const bot = makeBot([offer]);
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
    });

    await expect(service.sendOffer({ ...baseRequest, items: [] })).rejects.toThrow(/non-empty/);
    expect(offer.send).not.toHaveBeenCalled();
    expect(recordedWebhook).not.toHaveBeenCalled();
  });

  it('sets message on offer when provided', async () => {
    const offer = makeOffer({ sendResults: [{ ok: true, id: 'offer-msg', status: 'sent' }] });
    const bot = makeBot([offer]);
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
    });

    await service.sendOffer({ ...baseRequest, message: 'Skinora escrow' });

    expect(offer.setMessage).toHaveBeenCalledWith('Skinora escrow');
  });

  it('rejects items whose addItem call returns false', async () => {
    const offer = makeOffer({ sendResults: [{ ok: true, id: 'x', status: 'sent' }] });
    (offer.addMyItem as ReturnType<typeof vi.fn>).mockReturnValueOnce(false);
    const bot = makeBot([offer]);
    const service = new TradeOfferService(makeBotManager(bot), {
      webhookSender: recordedWebhook,
    });

    const result = await service.sendOffer(baseRequest);

    expect(result.status).toBe('failed');
    expect(result.retryable).toBe(false);
    expect(offer.send).not.toHaveBeenCalled();
    const [, payload] = recordedWebhook.mock.calls[0];
    expect(payload.data.reason).toMatch(/Item rejected/);
  });
});
