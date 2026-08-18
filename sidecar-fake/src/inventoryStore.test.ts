import { describe, it, expect, beforeEach } from 'vitest';
import {
  DEFAULT_TRADE_HOLD,
  InventoryControlError,
  ITEM_CATALOG,
  getInventory,
  getTradeHold,
  inventoryResponse,
  resetSteamState,
  resolveItem,
  setInventory,
  setTradeHold,
  simulateTrade,
} from './inventoryStore.js';

const SELLER = '76561190000000010';
const BUYER = '76561190000000020';

describe('inventoryStore', () => {
  beforeEach(() => resetSteamState());

  describe('defaults', () => {
    it('reads an undriven steamId as PUBLIC and EMPTY', () => {
      // The whole point of T137: a steamId nobody seeded must not hand the
      // caller a copy of the traded skin, otherwise the buyer-side baseline is
      // poisoned before the delivery even happens.
      expect(getInventory(BUYER)).toEqual({ visibility: 'PUBLIC', items: [] });
      const res = inventoryResponse(BUYER);
      expect(res.status).toBe(200);
      expect(res.body).toEqual({
        visibility: 'PUBLIC',
        items: [],
        totalCount: 0,
        tradeableCount: 0,
      });
    });

    it('reports MA-verified / no-hold for an undriven steamId', () => {
      expect(getTradeHold(SELLER)).toEqual(DEFAULT_TRADE_HOLD);
      expect(getTradeHold(SELLER).active).toBe(true);
    });
  });

  describe('resolveItem', () => {
    it('expands a catalog name into the full item', () => {
      expect(resolveItem({ catalog: 'AK47_REDLINE' })).toEqual(ITEM_CATALOG.AK47_REDLINE);
    });

    it('lets explicit fields override the catalog base', () => {
      const item = resolveItem({ catalog: 'AK47_REDLINE', assetId: '999' });
      expect(item.assetId).toBe('999');
      // A second copy of the SAME class — what a 06 §3.5 count delta is made of.
      expect(item.classId).toBe(ITEM_CATALOG.AK47_REDLINE.classId);
      expect(item.instanceId).toBe(ITEM_CATALOG.AK47_REDLINE.instanceId);
    });

    it('accepts a fully explicit item with no catalog', () => {
      const item = resolveItem({ assetId: '1', classId: '2', name: 'Custom' });
      expect(item.assetId).toBe('1');
      expect(item.classId).toBe('2');
      expect(item.tradable).toBe(true);
    });

    it('rejects an unknown catalog name', () => {
      expect(() => resolveItem({ catalog: 'NOPE' })).toThrow(InventoryControlError);
    });

    it('rejects an item without an assetId or without a classId', () => {
      expect(() => resolveItem({ classId: '2' })).toThrow(/assetId is required/);
      expect(() => resolveItem({ assetId: '1' })).toThrow(/needs a classId/);
    });

    it('rejects an unknown field instead of silently dropping it', () => {
      // A silently ignored typo would seed the wrong inventory and surface as a
      // failure three steps later, in a different file.
      expect(() =>
        resolveItem({ catalog: 'AK47_REDLINE', assetid: '9' } as unknown as never),
      ).toThrow(/unknown item field 'assetid'/);
    });

    it('does not let a spec key reach the prototype', () => {
      // Built via JSON.parse because that is the real path (express body
      // parser): unlike an object literal, JSON.parse makes `__proto__` an OWN
      // property, so it would survive the rest-spread and land in the copy loop.
      const hostile = JSON.parse('{"catalog":"AK47_REDLINE","__proto__":{"polluted":true}}');
      expect(() => resolveItem(hostile)).toThrow(/unknown item field/);
      expect(({} as Record<string, unknown>).polluted).toBeUndefined();
    });
  });

  describe('setInventory', () => {
    it('seeds items and reports them back', () => {
      const entry = setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }] });
      expect(entry.items).toHaveLength(1);
      expect(getInventory(SELLER).items[0].assetId).toBe('11111111001');
    });

    it('replaces the previous holdings rather than appending', () => {
      setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }] });
      setInventory(SELLER, { items: [{ catalog: 'AWP_ASIIMOV' }] });
      expect(getInventory(SELLER).items.map((i) => i.assetId)).toEqual(['11111111002']);
    });

    it('keeps items when only visibility is driven, and vice versa', () => {
      setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }] });
      setInventory(SELLER, { visibility: 'PRIVATE' });
      expect(getInventory(SELLER).items).toHaveLength(1);
      expect(getInventory(SELLER).visibility).toBe('PRIVATE');

      setInventory(SELLER, { items: [] });
      expect(getInventory(SELLER).visibility).toBe('PRIVATE');
      expect(getInventory(SELLER).items).toEqual([]);
    });

    it('normalises visibility case and rejects an unknown value', () => {
      expect(setInventory(SELLER, { visibility: 'private' }).visibility).toBe('PRIVATE');
      expect(() => setInventory(SELLER, { visibility: 'HIDDEN' })).toThrow(/unknown visibility/);
    });

    it('rejects duplicate asset ids in one inventory', () => {
      expect(() =>
        setInventory(SELLER, {
          items: [{ catalog: 'AK47_REDLINE' }, { catalog: 'AK47_REDLINE' }],
        }),
      ).toThrow(/duplicate assetId/);
    });

    it('rejects a non-array items payload and a blank steamId', () => {
      expect(() => setInventory(SELLER, { items: 'nope' })).toThrow(/must be an array/);
      expect(() => setInventory('', { items: [] })).toThrow(/steamId is required/);
    });

    it('hands back a copy, so a caller cannot mutate the store', () => {
      const entry = setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }] });
      entry.items[0].assetId = 'tampered';
      expect(getInventory(SELLER).items[0].assetId).toBe('11111111001');
    });
  });

  describe('inventoryResponse', () => {
    it('serves PUBLIC as 200 with counts', () => {
      setInventory(SELLER, {
        items: [
          { catalog: 'AK47_REDLINE' },
          { catalog: 'AWP_ASIIMOV', assetId: '555', tradable: false },
        ],
      });
      const res = inventoryResponse(SELLER);
      expect(res.status).toBe(200);
      expect(res.body.totalCount).toBe(2);
      // tradeableCount counts only tradable items — the field the create-flow
      // eligibility check reads.
      expect(res.body.tradeableCount).toBe(1);
    });

    it('serves PRIVATE as 422 INVENTORY_PRIVATE with no items', () => {
      setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }], visibility: 'PRIVATE' });
      const res = inventoryResponse(SELLER);
      expect(res.status).toBe(422);
      expect(res.body.code).toBe('INVENTORY_PRIVATE');
      expect(res.body.visibility).toBe('PRIVATE');
      // An unreadable profile must never leak an items array: that collapse is
      // exactly what turns "private" into "the item is gone" one layer up.
      expect(res.body.items).toBeUndefined();
    });

    it('serves UNAVAILABLE as 503 STEAM_UNAVAILABLE with no items', () => {
      setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }], visibility: 'UNAVAILABLE' });
      const res = inventoryResponse(SELLER);
      expect(res.status).toBe(503);
      expect(res.body.code).toBe('STEAM_UNAVAILABLE');
      expect(res.body.items).toBeUndefined();
    });

    it('emits every field name the backend parses', () => {
      setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }] });
      const items = inventoryResponse(SELLER).body.items as Record<string, unknown>[];
      expect(Object.keys(items[0]).sort()).toEqual(
        [
          'assetId',
          'classId',
          'exterior',
          'iconUrl',
          'instanceId',
          'marketHashName',
          'marketable',
          'name',
          'tradable',
          'type',
        ].sort(),
      );
    });
  });

  describe('simulateTrade', () => {
    beforeEach(() => {
      setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }, { catalog: 'AWP_ASIIMOV' }] });
    });

    it('moves the asset out of the seller and into the buyer under a NEW id', () => {
      const result = simulateTrade(SELLER, BUYER, '11111111001');
      expect(result.ok).toBe(true);
      if (!result.ok) return;

      // Seller side: the ORIGINAL asset id is gone — the SELLER_ASSET_GONE half
      // of the 02 §9.2 evidence.
      expect(getInventory(SELLER).items.map((i) => i.assetId)).toEqual(['11111111002']);

      // Buyer side: one more copy of the same CLASS, under a rotated asset id
      // (06 §8.4) — the INVENTORY_DELTA half.
      const buyerItems = getInventory(BUYER).items;
      expect(buyerItems).toHaveLength(1);
      expect(buyerItems[0].assetId).toBe(result.newAssetId);
      expect(buyerItems[0].assetId).not.toBe('11111111001');
      expect(buyerItems[0].classId).toBe(ITEM_CATALOG.AK47_REDLINE.classId);
      expect(buyerItems[0].instanceId).toBe(ITEM_CATALOG.AK47_REDLINE.instanceId);
      expect(buyerItems[0].name).toBe(ITEM_CATALOG.AK47_REDLINE.name);
    });

    it('adds to the class count the buyer already had', () => {
      setInventory(BUYER, { items: [{ catalog: 'AK47_REDLINE', assetId: '777' }] });
      simulateTrade(SELLER, BUYER, '11111111001');
      const sameClass = getInventory(BUYER).items.filter(
        (i) => i.classId === ITEM_CATALOG.AK47_REDLINE.classId,
      );
      expect(sameClass).toHaveLength(2);
    });

    it('is deterministic in the id it mints', () => {
      const first = simulateTrade(SELLER, BUYER, '11111111001');
      resetSteamState();
      setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }] });
      const second = simulateTrade(SELLER, BUYER, '11111111001');
      expect(first.ok && second.ok && first.newAssetId === second.newAssetId).toBe(true);
    });

    it('supports the reverse leg — the seller pulling the trade back (T129)', () => {
      const forward = simulateTrade(SELLER, BUYER, '11111111001');
      expect(forward.ok).toBe(true);
      if (!forward.ok) return;

      const back = simulateTrade(BUYER, SELLER, forward.newAssetId);
      expect(back.ok).toBe(true);
      expect(getInventory(BUYER).items).toEqual([]);
      expect(getInventory(SELLER).items).toHaveLength(2);
    });

    it('refuses to move an asset the sender does not hold', () => {
      const result = simulateTrade(SELLER, BUYER, '404404404');
      expect(result).toEqual({ ok: false, error: `${SELLER} does not hold asset 404404404` });
      expect(getInventory(BUYER).items).toEqual([]);
    });

    it('refuses blank arguments and a self-trade', () => {
      expect(simulateTrade('', BUYER, '1').ok).toBe(false);
      expect(simulateTrade(SELLER, '', '1').ok).toBe(false);
      expect(simulateTrade(SELLER, BUYER, '').ok).toBe(false);
      expect(simulateTrade(SELLER, SELLER, '11111111001').ok).toBe(false);
    });

    it('leaves visibility alone on both sides', () => {
      setInventory(BUYER, { visibility: 'PRIVATE' });
      simulateTrade(SELLER, BUYER, '11111111001');
      // A delivery into a profile the platform cannot read is a real case the
      // delivery check has to stay honest about — the trade must not "fix" it.
      expect(getInventory(BUYER).visibility).toBe('PRIVATE');
      expect(getInventory(SELLER).visibility).toBe('PUBLIC');
    });
  });

  describe('setTradeHold', () => {
    it('drives the MA flag per steamId', () => {
      expect(setTradeHold(SELLER, { active: false })).toEqual({
        active: false,
        escrowEndDurationSeconds: 0,
      });
      expect(getTradeHold(SELLER).active).toBe(false);
      // Untouched steamIds keep the default.
      expect(getTradeHold(BUYER)).toEqual(DEFAULT_TRADE_HOLD);
    });

    it('drives the escrow duration and keeps the untouched half', () => {
      setTradeHold(SELLER, { escrowEndDurationSeconds: 604800 });
      expect(getTradeHold(SELLER)).toEqual({ active: true, escrowEndDurationSeconds: 604800 });
      setTradeHold(SELLER, { active: false });
      expect(getTradeHold(SELLER)).toEqual({ active: false, escrowEndDurationSeconds: 604800 });
    });

    it('rejects malformed input', () => {
      expect(() => setTradeHold('', { active: false })).toThrow(/steamId is required/);
      expect(() => setTradeHold(SELLER, { active: 'no' as unknown as boolean })).toThrow(
        /must be a boolean/,
      );
      expect(() => setTradeHold(SELLER, { escrowEndDurationSeconds: -1 })).toThrow(
        /non-negative number/,
      );
    });
  });

  describe('resetSteamState', () => {
    it('clears inventories and trade holds together', () => {
      setInventory(SELLER, { items: [{ catalog: 'AK47_REDLINE' }], visibility: 'PRIVATE' });
      setTradeHold(SELLER, { active: false });
      resetSteamState();
      expect(getInventory(SELLER)).toEqual({ visibility: 'PUBLIC', items: [] });
      expect(getTradeHold(SELLER)).toEqual(DEFAULT_TRADE_HOLD);
    });
  });
});
