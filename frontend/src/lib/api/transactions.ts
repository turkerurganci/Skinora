import { apiClient } from "./client";
import type { PagedResult } from "@/types/api";
import type { StablecoinType } from "@/types/enums";
import type { ExtendedStatus } from "@/components/common";

/**
 * Tab filter for T1 — `GET /transactions?tab=` (07 §7.1).
 */
export type TransactionListTab = "active" | "completed" | "cancelled";

/**
 * Active-timeout block (07 §7.1). The backend resolves the 06 §3.5 state →
 * deadline matrix server-side and emits `null` for terminal / matrix-blank
 * states — the client only renders a countdown when this is present.
 */
export interface TransactionListActiveTimeout {
  type: string;
  expiresAt: string;
  remainingSeconds: number;
  warningThresholdPercent: number;
}

/**
 * Counterparty snapshot (07 §7.1). Null when the other party has not joined
 * yet (OPEN_LINK pre-acceptance + seller-side CREATED rows with no buyer).
 * The backend suppresses the field entirely with `WhenWritingNull`, so the
 * client must treat `undefined` as "no counterparty yet".
 */
export interface TransactionListCounterparty {
  steamId: string;
  displayName: string;
  avatarUrl?: string;
}

/**
 * One row of the T1 list (07 §7.1). `status` is a string projection rather
 * than `TransactionStatus` because the backend overlays `EMERGENCY_HOLD` on
 * top of any active state — `ExtendedStatus` matches that union.
 *
 * `price` is a string ("100.00") to preserve scale-6 decimal semantics
 * across the JSON boundary; format helpers append the stablecoin label.
 */
export interface TransactionListItem {
  id: string;
  itemName: string;
  itemImageUrl?: string;
  status: ExtendedStatus;
  price: string;
  stablecoin: StablecoinType;
  counterparty?: TransactionListCounterparty;
  userRole: "seller" | "buyer";
  activeTimeout?: TransactionListActiveTimeout;
  createdAt: string;
}

export interface TransactionListQuery {
  tab: TransactionListTab;
  page?: number;
  pageSize?: number;
}

export function listTransactions(
  query: TransactionListQuery,
): Promise<PagedResult<TransactionListItem>> {
  const params = new URLSearchParams({ tab: query.tab });
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  return apiClient<PagedResult<TransactionListItem>>(`/transactions?${params.toString()}`);
}
