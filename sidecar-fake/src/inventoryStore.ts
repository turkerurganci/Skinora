import { fakeAssetId } from './ids.js';

/**
 * Drivable Steam inventory + trade-hold state (T137).
 *
 * The fake used to answer `GET /api/inventory/:steamId` with ONE constant list
 * regardless of the steamId, so the seller and the buyer saw the same items.
 * P2P delivery is inferred from exactly that difference — the asset leaves the
 * seller and a copy of its class appears at the buyer (02 §9.2, 06 §3.5) — so a
 * steamId-blind inventory cannot simulate a delivery at all, only a permanent
 * "nothing changed".
 *
 * This module gives every steamId its own holdings and lets a test drive them:
 * seed an inventory, move an asset from one inventory to another (the P2P trade
 * the platform is not a party to), or make a profile unreadable. Both Express
 * surfaces run in one process (`index.ts`), so this module-level state is
 * shared between the backend-facing routes (5100) and the control surface
 * (5200) — the same arrangement the retired trade-accept control used.
 *
 * Not modelled: trade lock / cooldown. A traded item is 7 days untradable on
 * real Steam (T122 runbook §6.1), but no consumer reads lock state — the
 * delivery service documents at length why it must not (an anonymous read
 * carries no expiry, so the signal is unmeasurable) — and simulating a field
 * nobody may read would invite a test to assert on it.
 */

/** 08 §2.3 three-valued readability. */
export type InventoryVisibility = 'PUBLIC' | 'PRIVATE' | 'UNAVAILABLE';

const VISIBILITIES: readonly InventoryVisibility[] = ['PUBLIC', 'PRIVATE', 'UNAVAILABLE'];

/** One inventory item in the wire shape the backend parses (07 §6.1). */
export interface FakeInventoryItem {
  assetId: string;
  classId: string;
  instanceId: string;
  name: string;
  marketHashName: string;
  type: string;
  exterior: string;
  iconUrl: string;
  tradable: boolean;
  marketable: boolean;
}

/**
 * Named item templates. A test seeds `{ catalog: 'AK47_REDLINE' }` instead of
 * repeating ten fields, and the two entries keep the asset/class ids the
 * pre-T137 constant inventory used so existing seeds stay recognizable.
 *
 * Both are tradable + marketable so the create-flow eligibility checks pass.
 */
export const ITEM_CATALOG: Readonly<Record<string, FakeInventoryItem>> = {
  AK47_REDLINE: {
    assetId: '11111111001',
    classId: '310776767',
    instanceId: '302028390',
    name: 'AK-47 | Redline (Field-Tested)',
    marketHashName: 'AK-47 | Redline (Field-Tested)',
    type: 'Rifle',
    exterior: 'Field-Tested',
    iconUrl: '',
    tradable: true,
    marketable: true,
  },
  AWP_ASIIMOV: {
    assetId: '11111111002',
    classId: '310777458',
    instanceId: '302028390',
    name: 'AWP | Asiimov (Field-Tested)',
    marketHashName: 'AWP | Asiimov (Field-Tested)',
    type: 'Sniper Rifle',
    exterior: 'Field-Tested',
    iconUrl: '',
    tradable: true,
    marketable: true,
  },
};

/** Every field a spec may set — the allow-list `resolveItem` copies through. */
const ITEM_FIELDS: readonly (keyof FakeInventoryItem)[] = [
  'assetId',
  'classId',
  'instanceId',
  'name',
  'marketHashName',
  'type',
  'exterior',
  'iconUrl',
  'tradable',
  'marketable',
];

/** What a test may send per item: a catalog name, explicit fields, or both. */
export interface InventoryItemSpec extends Partial<FakeInventoryItem> {
  catalog?: string;
}

export interface InventoryEntry {
  visibility: InventoryVisibility;
  items: FakeInventoryItem[];
}

export interface TradeHoldState {
  /** Mobile-authenticator flag (08 §2.2) — NOT the hold flag. */
  active: boolean;
  escrowEndDurationSeconds: number;
}

/**
 * An inventory nobody has driven. Readable and EMPTY, never "the default two
 * items": a steamId the test never seeded holds nothing, and answering with
 * items instead would hand every buyer a copy of the skin they are waiting for
 * — silently destroying the very baseline the delivery check measures against
 * (owner decision, T137).
 */
const DEFAULT_ENTRY: InventoryEntry = { visibility: 'PUBLIC', items: [] };

/** MA verified, no Steam escrow hold — the pre-T137 constant, now per-steamId. */
export const DEFAULT_TRADE_HOLD: TradeHoldState = { active: true, escrowEndDurationSeconds: 0 };

const inventories = new Map<string, InventoryEntry>();
const tradeHolds = new Map<string, TradeHoldState>();

/** Raised for a malformed control-surface request (mapped to HTTP 400). */
export class InventoryControlError extends Error {}

function copyItem(item: FakeInventoryItem): FakeInventoryItem {
  return { ...item };
}

function copyEntry(entry: InventoryEntry): InventoryEntry {
  return { visibility: entry.visibility, items: entry.items.map(copyItem) };
}

function parseVisibility(value: unknown): InventoryVisibility {
  if (typeof value !== 'string') {
    throw new InventoryControlError('visibility must be a string');
  }
  const upper = value.trim().toUpperCase() as InventoryVisibility;
  if (!VISIBILITIES.includes(upper)) {
    throw new InventoryControlError(
      `unknown visibility '${value}' (expected ${VISIBILITIES.join(' / ')})`,
    );
  }
  return upper;
}

/**
 * Resolve one spec into a full item. A `catalog` name supplies the base and any
 * explicit field overrides it, so `{ catalog: 'AK47_REDLINE', assetId: 'X' }`
 * is a second copy of the same class — the shape 06 §3.5 counts.
 */
export function resolveItem(spec: InventoryItemSpec): FakeInventoryItem {
  if (spec === null || typeof spec !== 'object') {
    throw new InventoryControlError('each item must be an object');
  }
  const { catalog, ...overrides } = spec;
  let base: FakeInventoryItem;
  if (catalog === undefined) {
    base = {
      assetId: '',
      classId: '',
      instanceId: '',
      name: '',
      marketHashName: '',
      type: '',
      exterior: '',
      iconUrl: '',
      tradable: true,
      marketable: true,
    };
  } else {
    const template = ITEM_CATALOG[catalog];
    if (!template) {
      throw new InventoryControlError(
        `unknown catalog item '${catalog}' (known: ${Object.keys(ITEM_CATALOG).join(', ')})`,
      );
    }
    base = copyItem(template);
  }

  // Copy field by field off a fixed allow-list rather than iterating the
  // caller's keys: an unknown key is a typo the test must hear about (a silently
  // ignored `assetid` would seed the wrong inventory and fail somewhere else
  // entirely), and iterating attacker-shaped keys is how `__proto__` ends up
  // assigned instead of a field.
  const item: FakeInventoryItem = { ...base };
  const target = item as unknown as Record<string, unknown>;
  for (const [key, value] of Object.entries(overrides)) {
    if (!ITEM_FIELDS.includes(key as keyof FakeInventoryItem)) {
      throw new InventoryControlError(
        `unknown item field '${key}' (known: ${ITEM_FIELDS.join(', ')}, catalog)`,
      );
    }
    if (value === undefined) continue;
    target[key] = value;
  }

  if (!item.assetId) {
    throw new InventoryControlError('assetId is required (no catalog default applied)');
  }
  if (!item.classId) {
    // The class is what a delivery delta is counted over (06 §3.5); an item
    // without one would be invisible to the very check this store exists for.
    throw new InventoryControlError(`item ${item.assetId} needs a classId`);
  }
  return item;
}

/** Seed (replace) one steamId's holdings and/or readability. */
export function setInventory(
  steamId: string,
  opts: { items?: unknown; visibility?: unknown } = {},
): InventoryEntry {
  if (!steamId) {
    throw new InventoryControlError('steamId is required');
  }
  const current = inventories.get(steamId) ?? DEFAULT_ENTRY;
  const visibility =
    opts.visibility === undefined ? current.visibility : parseVisibility(opts.visibility);

  let items = current.items.map(copyItem);
  if (opts.items !== undefined) {
    if (!Array.isArray(opts.items)) {
      throw new InventoryControlError('items must be an array');
    }
    items = (opts.items as InventoryItemSpec[]).map(resolveItem);
    const seen = new Set<string>();
    for (const item of items) {
      if (seen.has(item.assetId)) {
        throw new InventoryControlError(`duplicate assetId ${item.assetId} in one inventory`);
      }
      seen.add(item.assetId);
    }
  }

  const entry: InventoryEntry = { visibility, items };
  inventories.set(steamId, entry);
  return copyEntry(entry);
}

/** Current holdings — an undriven steamId reads as PUBLIC and empty. */
export function getInventory(steamId: string): InventoryEntry {
  return copyEntry(inventories.get(steamId) ?? DEFAULT_ENTRY);
}

export type TradeResult =
  | { ok: true; newAssetId: string; item: FakeInventoryItem }
  | { ok: false; error: string };

/**
 * Move one asset between two inventories — the seller→buyer trade the platform
 * never sees (02 §2.1). The asset id ROTATES on arrival because Steam issues a
 * new one on every trade (06 §8.4); class and instance are preserved, which is
 * what makes the buyer-side class-count delta land while the seller-side lookup
 * of the ORIGINAL asset id correctly stops finding it.
 *
 * Visibility is untouched: whether an inventory can be read is a property of
 * the profile, not of the items in it, and a trade into a private profile is
 * exactly the case the delivery check has to stay honest about.
 */
export function simulateTrade(
  fromSteamId: string,
  toSteamId: string,
  assetId: string,
): TradeResult {
  if (!fromSteamId || !toSteamId || !assetId) {
    return { ok: false, error: 'fromSteamId, toSteamId and assetId are required' };
  }
  if (fromSteamId === toSteamId) {
    return { ok: false, error: 'fromSteamId and toSteamId must differ' };
  }

  const from = inventories.get(fromSteamId) ?? DEFAULT_ENTRY;
  const index = from.items.findIndex((it) => it.assetId === assetId);
  if (index < 0) {
    return { ok: false, error: `${fromSteamId} does not hold asset ${assetId}` };
  }

  const moved = copyItem(from.items[index]);
  const remaining = from.items.filter((_, i) => i !== index);
  inventories.set(fromSteamId, { visibility: from.visibility, items: remaining });

  const newAssetId = fakeAssetId(`trade:${fromSteamId}->${toSteamId}:${assetId}`);
  const arrived: FakeInventoryItem = { ...moved, assetId: newAssetId };
  const to = inventories.get(toSteamId) ?? DEFAULT_ENTRY;
  const toItems = to.items.filter((it) => it.assetId !== newAssetId).map(copyItem);
  toItems.push(arrived);
  inventories.set(toSteamId, { visibility: to.visibility, items: toItems });

  return { ok: true, newAssetId, item: copyItem(arrived) };
}

/** Drive the 08 §2.2 MA/trade-hold probe for one steamId. */
export function setTradeHold(steamId: string, opts: Partial<TradeHoldState> = {}): TradeHoldState {
  if (!steamId) {
    throw new InventoryControlError('steamId is required');
  }
  const current = tradeHolds.get(steamId) ?? DEFAULT_TRADE_HOLD;
  const active = opts.active === undefined ? current.active : opts.active;
  if (typeof active !== 'boolean') {
    throw new InventoryControlError('active must be a boolean');
  }
  const seconds =
    opts.escrowEndDurationSeconds === undefined
      ? current.escrowEndDurationSeconds
      : opts.escrowEndDurationSeconds;
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) {
    throw new InventoryControlError('escrowEndDurationSeconds must be a non-negative number');
  }
  const state: TradeHoldState = { active, escrowEndDurationSeconds: seconds };
  tradeHolds.set(steamId, state);
  return { ...state };
}

export function getTradeHold(steamId: string): TradeHoldState {
  return { ...(tradeHolds.get(steamId) ?? DEFAULT_TRADE_HOLD) };
}

/** Drop every driven inventory and trade hold — the between-scenario reset. */
export function resetSteamState(): void {
  inventories.clear();
  tradeHolds.clear();
}

export interface InventoryHttpResponse {
  status: number;
  body: Record<string, unknown>;
}

/**
 * The `GET /api/inventory/:steamId` answer, status code included.
 *
 * Status codes mirror the real sidecar byte for byte (08 §2.3 → 200 / 422
 * INVENTORY_PRIVATE / 503 STEAM_UNAVAILABLE, `sidecar-steam/src/api/routes.ts`)
 * and carry `visibility` in the body ALONGSIDE them. Collapsing every outcome
 * onto 200 would leave the backend's status-code branch — the one an older
 * consumer relies on — unexercised by every e2e run.
 */
export function inventoryResponse(steamId: string): InventoryHttpResponse {
  const entry = getInventory(steamId);
  switch (entry.visibility) {
    case 'PRIVATE':
      return {
        status: 422,
        body: {
          visibility: entry.visibility,
          code: 'INVENTORY_PRIVATE',
          error: `Steam inventory for ${steamId} is private`,
        },
      };
    case 'UNAVAILABLE':
      return {
        status: 503,
        body: {
          visibility: entry.visibility,
          code: 'STEAM_UNAVAILABLE',
          error: `Steam inventory for ${steamId} is temporarily unavailable`,
        },
      };
    default:
      return {
        status: 200,
        body: {
          visibility: entry.visibility,
          items: entry.items,
          totalCount: entry.items.length,
          tradeableCount: entry.items.filter((it) => it.tradable).length,
        },
      };
  }
}
