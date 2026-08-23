import { describe, it, expect, vi } from 'vitest';
import type { Logger } from '../logger.js';
import { HttpInventoryFetcher, PRIVATE_INVENTORY_MESSAGE } from './HttpInventoryFetcher.js';

/**
 * F2 — `UITour-InventoryClientRefusedBySteam`.
 *
 * Bu adaptör `steamcommunity` paketinin yerine geçiyor ve `InventoryService`
 * ona göre yazılmış olduğu için asıl iddia **gözlenebilir davranışın birebir
 * korunduğu**: sayfalama, description birleştirme, `total_inventory_count`'un
 * son sayfadan taşınması, gizli envanterin TAM olarak `PRIVATE_INVENTORY_MARKER`
 * dizgesiyle fırlatılması ve `getImageURL`'ün bir METOT olarak var olması.
 *
 * Son madde özellikle test ediliyor çünkü sessizce bozulurdu: `mapItem`
 * `typeof raw.getImageURL === 'function'` diye bakıyor ve düz JSON'da bu
 * koşul sağlanmayınca her item'ın görseli `null` olurdu — kırmızı test değil,
 * boş resimler şeklinde ortaya çıkardı.
 */

const silentLog = {
  info: vi.fn(),
  warn: vi.fn(),
  error: vi.fn(),
  debug: vi.fn(),
} as unknown as Logger;

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

function textResponse(text: string, status: number): Response {
  return {
    ok: false,
    status,
    json: async () => JSON.parse(text),
    text: async () => text,
  } as unknown as Response;
}

const PAGE_ONE = {
  success: 1,
  total_inventory_count: 2,
  assets: [{ assetid: 'A1', classid: 'C1', instanceid: '0', amount: '1' }],
  descriptions: [
    {
      classid: 'C1',
      instanceid: '0',
      name: 'AK-47 | Redline',
      market_hash_name: 'AK-47 | Redline (Field-Tested)',
      type: 'Rifle',
      icon_url: 'ICON1',
      tradable: 1,
      marketable: 1,
    },
  ],
  more_items: 1,
  last_assetid: 'A1',
};

const PAGE_TWO = {
  success: 1,
  total_inventory_count: 2,
  assets: [{ assetid: 'A2', classid: 'C2', instanceid: '5', amount: '1' }],
  descriptions: [
    { classid: 'C2', instanceid: '5', name: 'AWP | Asiimov', icon_url: 'ICON2', tradable: 0 },
  ],
};

function makeFetcher(responses: Response[]) {
  const fetchImpl = vi.fn();
  responses.forEach((r) => fetchImpl.mockResolvedValueOnce(r));
  const fetcher = new HttpInventoryFetcher({
    fetchImpl: fetchImpl as unknown as typeof fetch,
    sleep: async () => {},
    log: silentLog,
  });
  return { fetcher, fetchImpl };
}

describe('HttpInventoryFetcher (F2)', () => {
  it('sayfalar `more_items` / `last_assetid` ile zincirlenir ve birleşir', async () => {
    const { fetcher, fetchImpl } = makeFetcher([jsonResponse(PAGE_ONE), jsonResponse(PAGE_TWO)]);

    const result = await fetcher.fetch('765', 'english');

    expect(result.items).toHaveLength(2);
    expect(result.totalInventoryCount).toBe(2);
    // İkinci istek start_assetid taşımalı — taşımazsa aynı sayfa sonsuza dek
    // okunur ve kısa-okuma kapısı bunu yakalamaz.
    expect(String(fetchImpl.mock.calls[1][0])).toContain('start_assetid=A1');
    expect(String(fetchImpl.mock.calls[0][0])).not.toContain('start_assetid');
  });

  it('description alanları asset üstüne birleşir, asset alanları EZİLMEZ', async () => {
    const { fetcher } = makeFetcher([jsonResponse({ ...PAGE_ONE, more_items: 0 })]);

    const [item] = (await fetcher.fetch('765', 'english')).items as unknown as Record<
      string,
      unknown
    >[];

    expect(item.name).toBe('AK-47 | Redline');
    expect(item.market_hash_name).toBe('AK-47 | Redline (Field-Tested)');
    expect(item.assetid).toBe('A1'); // asset'ten
    expect(item.id).toBe('A1');
    expect(item.tradable).toBe(true); // 1 → boolean
  });

  it('getImageURL bir METOT olarak gelir ve kütüphaneyle aynı URL üretir', async () => {
    const { fetcher } = makeFetcher([jsonResponse({ ...PAGE_ONE, more_items: 0 })]);

    const [item] = (await fetcher.fetch('765', 'english')).items as unknown as {
      getImageURL: () => string | null;
    }[];

    expect(typeof item.getImageURL).toBe('function');
    expect(item.getImageURL()).toBe('https://steamcommunity-a.akamaihd.net/economy/image/ICON1/');
  });

  it('instanceid yoksa 0 kabul edilir ve description eşleşmesi bunu kullanır', async () => {
    const body = {
      success: 1,
      total_inventory_count: 1,
      assets: [{ assetid: 'A9', classid: 'C9', amount: '1' }],
      descriptions: [{ classid: 'C9', instanceid: '0', name: 'Sticker', icon_url: 'I9' }],
    };
    const { fetcher } = makeFetcher([jsonResponse(body)]);

    const [item] = (await fetcher.fetch('765', 'english')).items as unknown as Record<
      string,
      unknown
    >[];

    expect(item.instanceid).toBe('0');
    expect(item.name).toBe('Sticker');
  });

  it('403 + gövde `null` → PRIVATE dizgesi (InventoryService bu eşitliği arıyor)', async () => {
    const { fetcher } = makeFetcher([textResponse('null', 403)]);

    await expect(fetcher.fetch('765', 'english')).rejects.toThrow(PRIVATE_INVENTORY_MESSAGE);
  });

  it('403 ama gövde `null` DEĞİL → private sayılmaz', async () => {
    const { fetcher } = makeFetcher([textResponse('{"success":0}', 403)]);

    await expect(fetcher.fetch('765', 'english')).rejects.toThrow('HTTP error 403');
  });

  it('429 yeniden denenir ve sonunda başarılı olur', async () => {
    const { fetcher, fetchImpl } = makeFetcher([
      textResponse('rate limited', 429),
      jsonResponse({ ...PAGE_ONE, more_items: 0 }),
    ]);

    const result = await fetcher.fetch('765', 'english');

    expect(result.items).toHaveLength(1);
    expect(fetchImpl).toHaveBeenCalledTimes(2);
  });

  it('429 kalıcıysa rate-limit durumuyla raporlanır (sessiz boş liste DEĞİL)', async () => {
    const { fetcher, fetchImpl } = makeFetcher([
      textResponse('x', 429),
      textResponse('x', 429),
      textResponse('x', 429),
    ]);

    await expect(fetcher.fetch('765', 'english')).rejects.toThrow('HTTP error 429');
    expect(fetchImpl).toHaveBeenCalledTimes(3);
  });

  it('boş envanter → 0 item, hata yok', async () => {
    const { fetcher } = makeFetcher([jsonResponse({ success: 1, total_inventory_count: 0 })]);

    const result = await fetcher.fetch('765', 'english');

    expect(result.items).toEqual([]);
    expect(result.totalInventoryCount).toBe(0);
  });

  it('CS2 özel durumu: success ama assets yok → boş liste, sayı korunur', async () => {
    const { fetcher } = makeFetcher([jsonResponse({ success: 1, total_inventory_count: 7 })]);

    const result = await fetcher.fetch('765', 'english');

    expect(result.items).toEqual([]);
    expect(result.totalInventoryCount).toBe(7);
  });

  it('bozuk gövde → Malformed response', async () => {
    const { fetcher } = makeFetcher([jsonResponse({ success: 0 })]);

    await expect(fetcher.fetch('765', 'english')).rejects.toThrow('Malformed response');
  });

  it('asset_properties assetid ile eşlenir', async () => {
    const body = {
      success: 1,
      total_inventory_count: 1,
      assets: [{ assetid: 'A3', classid: 'C3', instanceid: '0', amount: '1' }],
      descriptions: [{ classid: 'C3', instanceid: '0', name: 'Knife', icon_url: 'I3' }],
      asset_properties: [
        { assetid: 'A3', asset_properties: [{ propertyid: 8, name: 'Wear Rating' }] },
      ],
    };
    const { fetcher } = makeFetcher([jsonResponse(body)]);

    const [item] = (await fetcher.fetch('765', 'english')).items as unknown as Record<
      string,
      unknown
    >[];

    expect(item.asset_properties).toEqual([{ propertyid: 8, name: 'Wear Rating' }]);
  });

  it('istek Referer taşır ve count=1000 kullanır', async () => {
    const { fetcher, fetchImpl } = makeFetcher([jsonResponse({ ...PAGE_ONE, more_items: 0 })]);

    await fetcher.fetch('765', 'english');

    const [url, init] = fetchImpl.mock.calls[0];
    expect(String(url)).toContain('count=1000');
    expect(String(url)).toContain('/inventory/765/730/2');
    expect((init as RequestInit).headers).toMatchObject({
      Referer: 'https://steamcommunity.com/profiles/765/inventory',
    });
  });
});
