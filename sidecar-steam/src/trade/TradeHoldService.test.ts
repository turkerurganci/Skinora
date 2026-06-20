import { describe, it, expect, vi } from 'vitest';
import { TradeHoldService, SteamApiKeyMissingError, type FetchLike } from './TradeHoldService.js';
import { SteamApiError } from '../errors/SidecarError.js';

vi.mock('../logger.js', () => ({
  logger: {
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
    debug: vi.fn(),
    child: vi.fn().mockReturnThis(),
  },
}));

const STEAM_ID = '76561198000000001';
const TOKEN = 'abc123xyz';
const API_KEY = 'test-api-key';

function okJson(body: unknown): ReturnType<FetchLike> {
  return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) });
}

function escrowPayload(theirSeconds: number): unknown {
  return {
    response: {
      my_escrow: { escrow_end_duration_seconds: 0 },
      their_escrow: { escrow_end_duration_seconds: theirSeconds },
      both_escrow: { escrow_end_duration_seconds: theirSeconds },
    },
  };
}

describe('TradeHoldService', () => {
  it('maps a 0-second target escrow to active=true (MA active)', async () => {
    const fetchFn = vi.fn<FetchLike>(() => okJson(escrowPayload(0)));
    const sut = new TradeHoldService(API_KEY, undefined, fetchFn);

    const result = await sut.getTradeHold(STEAM_ID, TOKEN);

    expect(result).toEqual({ active: true, escrowEndDurationSeconds: 0 });
  });

  it('maps a 15-day target escrow to active=false (MA inactive)', async () => {
    const fifteenDays = 15 * 24 * 60 * 60;
    const fetchFn = vi.fn<FetchLike>(() => okJson(escrowPayload(fifteenDays)));
    const sut = new TradeHoldService(API_KEY, undefined, fetchFn);

    const result = await sut.getTradeHold(STEAM_ID, TOKEN);

    expect(result).toEqual({ active: false, escrowEndDurationSeconds: fifteenDays });
  });

  it('sends the key as the x-webapi-key header and target/token as query params', async () => {
    let capturedUrl = '';
    let capturedHeaders: Record<string, string> | undefined;
    const fetchFn = vi.fn<FetchLike>((url, init) => {
      capturedUrl = url;
      capturedHeaders = init?.headers;
      return okJson(escrowPayload(0));
    });
    const sut = new TradeHoldService(API_KEY, undefined, fetchFn);

    await sut.getTradeHold(STEAM_ID, TOKEN);

    expect(capturedUrl).toContain('IEconService/GetTradeHoldDurations/v1');
    expect(capturedUrl).toContain(`steamid_target=${STEAM_ID}`);
    expect(capturedUrl).toContain(`trade_offer_access_token=${TOKEN}`);
    // Secret travels in the header, never the query string (08 §2.2).
    expect(capturedUrl).not.toContain(API_KEY);
    expect(capturedHeaders?.['x-webapi-key']).toBe(API_KEY);
  });

  it('throws SteamApiKeyMissingError when the key is absent', async () => {
    const fetchFn = vi.fn<FetchLike>(() => okJson(escrowPayload(0)));
    const sut = new TradeHoldService('', undefined, fetchFn);

    await expect(sut.getTradeHold(STEAM_ID, TOKEN)).rejects.toBeInstanceOf(SteamApiKeyMissingError);
    expect(fetchFn).not.toHaveBeenCalled();
  });

  it('throws SteamApiError on a non-2xx upstream response', async () => {
    const fetchFn = vi.fn<FetchLike>(() =>
      Promise.resolve({ ok: false, status: 429, json: () => Promise.resolve({}) }),
    );
    const sut = new TradeHoldService(API_KEY, undefined, fetchFn);

    await expect(sut.getTradeHold(STEAM_ID, TOKEN)).rejects.toBeInstanceOf(SteamApiError);
  });

  it('throws SteamApiError on a transport failure', async () => {
    const fetchFn = vi.fn<FetchLike>(() => Promise.reject(new Error('ECONNREFUSED')));
    const sut = new TradeHoldService(API_KEY, undefined, fetchFn);

    await expect(sut.getTradeHold(STEAM_ID, TOKEN)).rejects.toBeInstanceOf(SteamApiError);
  });

  it('throws SteamApiError when their_escrow is missing from the payload', async () => {
    const fetchFn = vi.fn<FetchLike>(() => okJson({ response: { my_escrow: {} } }));
    const sut = new TradeHoldService(API_KEY, undefined, fetchFn);

    await expect(sut.getTradeHold(STEAM_ID, TOKEN)).rejects.toBeInstanceOf(SteamApiError);
  });
});
