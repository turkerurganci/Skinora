import { describe, it, expect, vi } from 'vitest';
import {
  LimitedAccountService,
  SteamProfileUnreadableError,
  DEFAULT_LIMITED_ACCOUNT_SAMPLES,
  DEFAULT_LIMITED_ACCOUNT_SAMPLE_DELAY_MS,
  type TextFetchLike,
} from './LimitedAccountService.js';
import {
  InMemoryLimitedAccountCache,
  LIMITED_ACCOUNT_CACHE_TTL_SECONDS,
} from '../cache/LimitedAccountCache.js';

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

/** Gerçek cevabın iskeleti — alan sırası ve komşuları 2026-09-14 ölçümünden. */
function profileXml(limited: '0' | '1'): string {
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><profile><steamID64>${STEAM_ID}</steamID64><privacyState>public</privacyState><vacBanned>0</vacBanned><tradeBanState>None</tradeBanState><isLimitedAccount>${limited}</isLimitedAccount><memberSince>April 30, 2020</memberSince></profile>`;
}

function okText(body: string): ReturnType<TextFetchLike> {
  return Promise.resolve({ ok: true, status: 200, text: () => Promise.resolve(body) });
}

/** Testler gerçek zamanı beklemesin diye aralık yutulur; çağrıldığı ayrıca doğrulanır. */
function serviceWith(fetchFn: TextFetchLike, overrides = {}) {
  return new LimitedAccountService({
    fetchFn,
    sleepFn: () => Promise.resolve(),
    ...overrides,
  });
}

describe('LimitedAccountService', () => {
  it('varsayılanlar ölçülmüş yordamla aynı kalır', () => {
    // Sabitler test tarafında YENİDEN BİLDİRİLMEZ, import edilir — bu depoda
    // bir tur, testin kendi kopyaladığı sabit bayatladığı için sessizce
    // boşalmıştı (11 ret vakası v1'e yazıp v2'den okudu ve hepsi geçti).
    expect(DEFAULT_LIMITED_ACCOUNT_SAMPLES).toBe(3);
    expect(DEFAULT_LIMITED_ACCOUNT_SAMPLE_DELAY_MS).toBe(3_000);
    expect(LIMITED_ACCOUNT_CACHE_TTL_SECONDS).toBe(86_400);
  });

  it('tek bir 1 okuması anında bağlayıcıdır — teyit beklemez', async () => {
    const fetchFn = vi.fn<TextFetchLike>(() => okText(profileXml('1')));
    const sut = serviceWith(fetchFn);

    const result = await sut.isLimited(STEAM_ID);

    expect(result).toEqual({ limited: true, samples: 1, source: 'live' });
    expect(fetchFn).toHaveBeenCalledTimes(1);
  });

  it('0 okuması ancak N ardışık örnekten sonra kabul edilir', async () => {
    const fetchFn = vi.fn<TextFetchLike>(() => okText(profileXml('0')));
    const sut = serviceWith(fetchFn);

    const result = await sut.isLimited(STEAM_ID);

    expect(result).toEqual({
      limited: false,
      samples: DEFAULT_LIMITED_ACCOUNT_SAMPLES,
      source: 'live',
    });
    expect(fetchFn).toHaveBeenCalledTimes(DEFAULT_LIMITED_ACCOUNT_SAMPLES);
  });

  it('bayat 0 zinciri sonradan gelen 1 ile bozulur — ölçülen yanlış alarmın tam vakası', async () => {
    // 2026-09-02: uç iki kez "limit kalktı" dedi, ardışık örnekleme yalanladı.
    const fetchFn = vi
      .fn<TextFetchLike>()
      .mockReturnValueOnce(okText(profileXml('0')))
      .mockReturnValueOnce(okText(profileXml('1')));
    const sut = serviceWith(fetchFn);

    const result = await sut.isLimited(STEAM_ID);

    expect(result.limited).toBe(true);
    expect(result.samples).toBe(2);
  });

  it('örnekler arasında bekler — aralıksız örnekleme aynı bayat cevabı okur', async () => {
    const sleepFn = vi.fn(() => Promise.resolve());
    const sut = new LimitedAccountService({
      fetchFn: () => okText(profileXml('0')),
      sleepFn,
    });

    await sut.isLimited(STEAM_ID);

    expect(sleepFn).toHaveBeenCalledTimes(DEFAULT_LIMITED_ACCOUNT_SAMPLES - 1);
    expect(sleepFn).toHaveBeenCalledWith(DEFAULT_LIMITED_ACCOUNT_SAMPLE_DELAY_MS);
  });

  describe('arıza cevap gibi görünüyor — üçü de HTTP 200 ile döner (2026-09-14 ölçümü)', () => {
    it('olmayan SteamID64: 200 + HTML sayfası → fail-closed', async () => {
      const html = '<!DOCTYPE html>\n<html class=" responsive DesktopUI" lang="en"><head>…';
      const sut = serviceWith(() => okText(html));

      await expect(sut.isLimited(STEAM_ID)).rejects.toMatchObject({
        code: 'STEAM_PROFILE_UNREADABLE',
        bodyShape: 'html',
      });
    });

    it('bozuk id: 200 + <error> XML’i → fail-closed', async () => {
      const errorXml =
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><response><error><![CDATA[Failed loading profile data, please try again later.]]></error></response>';
      const sut = serviceWith(() => okText(errorXml));

      await expect(sut.isLimited(STEAM_ID)).rejects.toMatchObject({
        code: 'STEAM_PROFILE_UNREADABLE',
        bodyShape: 'profile_error',
      });
    });

    it('alan taşımayan geçerli profil → fail-closed, "limited değil" SAYILMAZ', async () => {
      const withoutField =
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><profile><steamID64>1</steamID64><privacyState>private</privacyState></profile>';
      const sut = serviceWith(() => okText(withoutField));

      await expect(sut.isLimited(STEAM_ID)).rejects.toBeInstanceOf(SteamProfileUnreadableError);
      await expect(sut.isLimited(STEAM_ID)).rejects.toMatchObject({ bodyShape: 'field_missing' });
    });

    it('taşıma hatası → fail-closed', async () => {
      const sut = serviceWith(() => Promise.reject(new Error('ECONNRESET')));

      await expect(sut.isLimited(STEAM_ID)).rejects.toMatchObject({ bodyShape: 'transport' });
    });

    it('cevap gövdesi okunamazsa → fail-closed', async () => {
      const sut = serviceWith(() =>
        Promise.resolve({
          ok: true,
          status: 200,
          text: () => Promise.reject(new Error('stream closed')),
        }),
      );

      await expect(sut.isLimited(STEAM_ID)).rejects.toMatchObject({ bodyShape: 'transport' });
    });
  });

  it('son tarih aşılırsa fail-closed olur — backend timeout’una düşmez', async () => {
    let clock = 0;
    const sut = new LimitedAccountService({
      fetchFn: () => okText(profileXml('0')),
      sleepFn: () => {
        clock += 60_000; // kuyrukta beklemiş gibi
        return Promise.resolve();
      },
      now: () => clock,
      deadlineMs: 20_000,
    });

    await expect(sut.isLimited(STEAM_ID)).rejects.toMatchObject({ bodyShape: 'deadline' });
  });

  describe('önbellek yalnız TEMİZ sonucu tutar', () => {
    it('doğrulanmış temiz sonuç saklanır ve ikinci çağrı Steam’e hiç gitmez', async () => {
      const fetchFn = vi.fn<TextFetchLike>(() => okText(profileXml('0')));
      const cache = new InMemoryLimitedAccountCache();
      const sut = serviceWith(fetchFn, { cache });

      await sut.isLimited(STEAM_ID);
      const second = await sut.isLimited(STEAM_ID);

      expect(second).toEqual({ limited: false, samples: 0, source: 'cache' });
      expect(fetchFn).toHaveBeenCalledTimes(DEFAULT_LIMITED_ACCOUNT_SAMPLES);
    });

    it('kısıtlı sonuç SAKLANMAZ — 5 USD’yi harcayan kullanıcı anında açılır', async () => {
      const fetchFn = vi
        .fn<TextFetchLike>()
        .mockReturnValueOnce(okText(profileXml('1')))
        .mockReturnValue(okText(profileXml('0')));
      const cache = new InMemoryLimitedAccountCache();
      const sut = serviceWith(fetchFn, { cache });

      expect((await sut.isLimited(STEAM_ID)).limited).toBe(true);
      expect(cache.size()).toBe(0);
      expect((await sut.isLimited(STEAM_ID)).limited).toBe(false);
    });

    it('arıza SAKLANMAZ — dakikalık kesinti bir güne yayılmaz', async () => {
      const cache = new InMemoryLimitedAccountCache();
      const fetchFn = vi
        .fn<TextFetchLike>()
        .mockReturnValueOnce(okText('<html>down</html>'))
        .mockReturnValue(okText(profileXml('0')));
      const sut = serviceWith(fetchFn, { cache });

      await expect(sut.isLimited(STEAM_ID)).rejects.toBeInstanceOf(SteamProfileUnreadableError);
      expect(cache.size()).toBe(0);
      expect((await sut.isLimited(STEAM_ID)).limited).toBe(false);
    });

    it('invalidate temiz kaydı siler', async () => {
      const cache = new InMemoryLimitedAccountCache();
      const fetchFn = vi.fn<TextFetchLike>(() => okText(profileXml('0')));
      const sut = serviceWith(fetchFn, { cache });

      await sut.isLimited(STEAM_ID);
      await sut.invalidate(STEAM_ID);
      await sut.isLimited(STEAM_ID);

      expect(fetchFn).toHaveBeenCalledTimes(DEFAULT_LIMITED_ACCOUNT_SAMPLES * 2);
    });
  });

  it('her örnek kuyruğa AYRI görev olarak girer — limitleyici 3 saymalı, 1 değil', async () => {
    const enqueue = vi.fn(<T>(fn: () => Promise<T>) => fn());
    const sut = serviceWith(() => okText(profileXml('0')), { queue: { enqueue } });

    await sut.isLimited(STEAM_ID);

    expect(enqueue).toHaveBeenCalledTimes(DEFAULT_LIMITED_ACCOUNT_SAMPLES);
  });

  it('sonuç sayacı her dalda tam bir kez artar', async () => {
    const onOutcome = vi.fn();
    const cache = new InMemoryLimitedAccountCache();

    await serviceWith(() => okText(profileXml('1')), { onOutcome }).isLimited(STEAM_ID);
    expect(onOutcome).toHaveBeenLastCalledWith('limited');

    const clean = serviceWith(() => okText(profileXml('0')), { onOutcome, cache });
    await clean.isLimited(STEAM_ID);
    expect(onOutcome).toHaveBeenLastCalledWith('clean');
    await clean.isLimited(STEAM_ID);
    expect(onOutcome).toHaveBeenLastCalledWith('cache_hit');

    await expect(
      serviceWith(() => okText('<html>x</html>'), { onOutcome }).isLimited(STEAM_ID),
    ).rejects.toThrow();
    expect(onOutcome).toHaveBeenLastCalledWith('unreadable');
  });

  it('profil URL’i ?xml=1 ile çağrılır ve anahtar/başlık taşımaz', async () => {
    const fetchFn = vi.fn<TextFetchLike>(() => okText(profileXml('1')));
    const sut = serviceWith(fetchFn);

    await sut.isLimited(STEAM_ID);

    expect(fetchFn).toHaveBeenCalledWith(
      `https://steamcommunity.com/profiles/${STEAM_ID}?xml=1`,
      expect.objectContaining({ method: 'GET' }),
    );
    expect(fetchFn.mock.calls[0][1]).not.toHaveProperty('headers');
  });
});
