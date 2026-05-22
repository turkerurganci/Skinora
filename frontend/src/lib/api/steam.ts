import { apiClient } from "./client";

/**
 * One Steam inventory entry returned by `GET /steam/inventory` (07 §6.1).
 * `wear` is omitted when the item has no float (knife sticker capsules etc.).
 * `imageUrl` may be omitted when Steam's CDN returns no icon — UI falls back
 * to the placeholder rendered by C03 ItemCard.
 */
export interface SteamInventoryItem {
  assetId: string;
  name: string;
  type?: string;
  imageUrl?: string;
  wear?: string;
  tradeable: boolean;
}

/**
 * Inventory envelope (07 §6.1). Backend ships the full list in one response —
 * S06 pagination is purely client-side via `IntersectionObserver`.
 */
export interface SteamInventoryResponse {
  items: SteamInventoryItem[];
  totalCount: number;
  tradeableCount: number;
}

export function getSteamInventory(): Promise<SteamInventoryResponse> {
  return apiClient<SteamInventoryResponse>("/steam/inventory");
}
