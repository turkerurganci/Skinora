import type Redis from 'ioredis';
import type { InventoryResponse } from '../trade/InventoryService.js';
import { logger as defaultLogger, type Logger } from '../logger.js';

/**
 * Cache port for Steam inventory snapshots (08 §2.3 — Redis 2-minute TTL).
 * Two implementations live in this file so tests can run against an in-memory
 * fallback without spinning up Redis, while production code wires up
 * {@link RedisInventoryCache} via `ioredis`.
 */
export interface InventoryCache {
  get(steamId: string): Promise<InventoryResponse | null>;
  set(steamId: string, value: InventoryResponse): Promise<void>;
  delete(steamId: string): Promise<void>;
}

/** Cache TTL — 08 §2.3 explicit "2 dakika". Exported for cross-test assertions. */
export const INVENTORY_CACHE_TTL_SECONDS = 120;

/** Key prefix — namespaced under `skinora:` to coexist with the backend rate limiter. */
export const INVENTORY_CACHE_KEY_PREFIX = 'skinora:steam:inventory:';

export function inventoryCacheKey(steamId: string): string {
  return `${INVENTORY_CACHE_KEY_PREFIX}${steamId}`;
}

/**
 * Redis-backed cache for production / staging. Uses `SETEX` for atomic
 * write-with-TTL; failures (Redis down, network) are swallowed and logged so
 * the inventory endpoint stays available — degraded performance beats hard
 * failure (08 §2.3 cache is an optimization, not a correctness boundary).
 */
export class RedisInventoryCache implements InventoryCache {
  constructor(
    private readonly redis: Redis,
    private readonly ttlSeconds: number = INVENTORY_CACHE_TTL_SECONDS,
    private readonly log: Logger = defaultLogger,
  ) {}

  async get(steamId: string): Promise<InventoryResponse | null> {
    try {
      const raw = await this.redis.get(inventoryCacheKey(steamId));
      if (!raw) return null;
      return JSON.parse(raw) as InventoryResponse;
    } catch (err) {
      this.log.warn({ steamId, err: (err as Error).message }, 'Inventory cache GET failed');
      return null;
    }
  }

  async set(steamId: string, value: InventoryResponse): Promise<void> {
    try {
      await this.redis.setex(inventoryCacheKey(steamId), this.ttlSeconds, JSON.stringify(value));
    } catch (err) {
      this.log.warn({ steamId, err: (err as Error).message }, 'Inventory cache SET failed');
    }
  }

  async delete(steamId: string): Promise<void> {
    try {
      await this.redis.del(inventoryCacheKey(steamId));
    } catch (err) {
      this.log.warn({ steamId, err: (err as Error).message }, 'Inventory cache DEL failed');
    }
  }
}

/**
 * In-memory fallback for tests + dev runs without Redis. Honors the same TTL
 * semantics so test assertions on expiry behave like production. Entries are
 * GC'd on demand (next get) — no background sweep needed at this volume.
 */
export class InMemoryInventoryCache implements InventoryCache {
  private readonly store = new Map<string, { value: InventoryResponse; expiresAt: number }>();

  constructor(
    private readonly ttlMs: number = INVENTORY_CACHE_TTL_SECONDS * 1000,
    private readonly now: () => number = () => Date.now(),
  ) {}

  async get(steamId: string): Promise<InventoryResponse | null> {
    const entry = this.store.get(steamId);
    if (!entry) return null;
    if (entry.expiresAt <= this.now()) {
      this.store.delete(steamId);
      return null;
    }
    return entry.value;
  }

  async set(steamId: string, value: InventoryResponse): Promise<void> {
    this.store.set(steamId, { value, expiresAt: this.now() + this.ttlMs });
  }

  async delete(steamId: string): Promise<void> {
    this.store.delete(steamId);
  }

  /** Test helper — sidecar code does not call this in production paths. */
  size(): number {
    return this.store.size;
  }
}
