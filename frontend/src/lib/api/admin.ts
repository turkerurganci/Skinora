import { apiClient } from "./client";
import { StablecoinType, TransactionStatus } from "@/types/enums";

/**
 * AD1 — `GET /admin/dashboard` (07 §9.1). Wire format matches the backend
 * `AdminDashboardResponse` record (T63 `Skinora.API/Services/AdminDashboardDtos.cs`).
 * Enums serialize as strings via `JsonStringEnumConverter`.
 */

export type AdminSteamAccountStatus = "ACTIVE" | "RESTRICTED" | "BANNED" | "OFFLINE";

export type AdminFlagType =
  | "PRICE_DEVIATION"
  | "HIGH_VOLUME"
  | "ABNORMAL_BEHAVIOR"
  | "MULTI_ACCOUNT"
  | "SANCTIONS_MATCH";

export type AdminFlagReviewStatus = "PENDING" | "APPROVED" | "REJECTED";

export interface AdminDashboardSummaryCards {
  activeTransactions: number;
  pendingFlags: number;
  dailyCompleted: number;
  weeklyCompleted: number;
}

/**
 * Mirrors `AdminSteamAccountDto` (AD10, 07 §9.10). The shared shape lets the
 * dashboard bot block reuse the AD10 projection 1:1 — confirmed in
 * `AdminDashboardService.GetAsync` which delegates to `AdminSteamBotQueryService`.
 */
export interface AdminSteamAccount {
  id: string;
  name: string;
  steamId: string;
  status: AdminSteamAccountStatus;
  escrowedItemCount: number;
  dailyTradeOfferCount: number;
  dailyTradeOfferLimit: number;
  lastHealthCheck: string | null;
  restrictionReason: string | null;
  failoverStatus: string;
  recoveryTransactionCount: number;
}

export interface AdminDashboardRecentFlag {
  id: string;
  transactionId: string | null;
  type: AdminFlagType;
  reviewStatus: AdminFlagReviewStatus;
  createdAt: string;
}

export interface AdminDashboardResponse {
  summaryCards: AdminDashboardSummaryCards;
  steamAccounts: AdminSteamAccount[];
  recentFlags: AdminDashboardRecentFlag[];
}

export function getAdminDashboard(): Promise<AdminDashboardResponse> {
  return apiClient<AdminDashboardResponse>("/admin/dashboard");
}

/* ──────────────────────────────────────────────────────────────────────────
 * AD2–AD5 + AD19d — Admin fraud-flag review (S13 / S14).
 * Wire format mirrors the backend `FraudFlagListResponse` / `FraudFlagDetailDto`
 * (T54 — 07 §9.2–§9.5) and the AD19d `HoldUserTransactionsResponse` (T100).
 * Enums serialize as strings (`JsonStringEnumConverter`); `decimal?` money
 * fields serialize as JSON numbers (no decimal→string converter is registered).
 * ────────────────────────────────────────────────────────────────────────── */

// 06 §2.21 FraudFlagScope.
export type AdminFlagScope = "ACCOUNT_LEVEL" | "TRANSACTION_PRE_CREATE";

/** Lightweight party view used by the AD2 list (07 §9.2). */
export interface AdminFlagParty {
  steamId: string;
  displayName: string;
  avatarUrl: string | null;
}

/** Rich party view used by the AD3 detail (07 §9.3) — adds trust signals. */
export interface AdminFlagPartyDetail extends AdminFlagParty {
  reputationScore: number | null;
  completedTransactionCount: number;
  accountAge: string;
}

/** One row of the AD2 flag list (07 §9.2). */
export interface AdminFlagListItem {
  id: string;
  transactionId: string | null;
  scope: AdminFlagScope;
  type: AdminFlagType;
  reviewStatus: AdminFlagReviewStatus;
  seller: AdminFlagParty | null;
  itemName: string | null;
  price: number | null;
  stablecoin: StablecoinType | null;
  marketPrice: number | null;
  createdAt: string;
}

/** AD2 page envelope — adds the `pendingCount` badge required by 07 §9.2. */
export interface AdminFlagListResponse {
  items: AdminFlagListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  pendingCount: number;
}

/** Embedded transaction view returned by AD3 (07 §9.3). */
export interface AdminFlagTransaction {
  id: string;
  status: TransactionStatus;
  itemName: string;
  itemImageUrl: string | null;
  price: number;
  stablecoin: StablecoinType;
  paymentTimeoutHours: number;
  createdAt: string;
}

// ── Type-specific `flagDetail` payloads (07 §9.3 table) ──────────────────────

export interface PriceDeviationFlagDetail {
  inputPrice: number;
  marketPrice: number;
  deviationPercent: number;
}

export interface HighVolumeFlagDetail {
  periodHours: number;
  transactionCount: number;
  totalVolume: number;
}

export interface AbnormalBehaviorFlagDetail {
  pattern: string;
  description: string;
}

export interface MultiAccountLinkedAccount {
  steamId: string;
  displayName: string;
}

export interface MultiAccountFlagDetail {
  matchType: string;
  matchValue: string;
  linkedAccounts: MultiAccountLinkedAccount[];
}

/** AD3 detail body (07 §9.3). `flagDetail` is narrowed by `type` at the call site. */
export interface AdminFlagDetail {
  id: string;
  userId: string;
  scope: AdminFlagScope;
  type: AdminFlagType;
  reviewStatus: AdminFlagReviewStatus;
  createdAt: string;
  flagDetail: unknown | null;
  transaction: AdminFlagTransaction | null;
  seller: AdminFlagPartyDetail | null;
  buyer: AdminFlagPartyDetail | null;
  historicalTransactionCount: number;
  reviewedBy: string | null;
  reviewedAt: string | null;
  adminNote: string | null;
}

/** AD4 / AD5 success body (07 §9.4 / §9.5). */
export interface AdminFlagReviewResult {
  reviewStatus: AdminFlagReviewStatus;
  transactionStatus: TransactionStatus | null;
  reviewedAt: string;
}

/** AD19d — bulk hold result (T100). */
export interface HoldUserTransactionsResult {
  heldCount: number;
  appliedAt: string;
  heldTransactionIds: string[];
}

export interface AdminFlagListQuery {
  scope?: AdminFlagScope;
  type?: AdminFlagType;
  reviewStatus?: AdminFlagReviewStatus;
  dateFrom?: string;
  dateTo?: string;
  page?: number;
  pageSize?: number;
}

export function listAdminFlags(query: AdminFlagListQuery): Promise<AdminFlagListResponse> {
  const params = new URLSearchParams();
  if (query.scope) params.set("scope", query.scope);
  if (query.type) params.set("type", query.type);
  if (query.reviewStatus) params.set("reviewStatus", query.reviewStatus);
  if (query.dateFrom) params.set("dateFrom", query.dateFrom);
  if (query.dateTo) params.set("dateTo", query.dateTo);
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  const qs = params.toString();
  return apiClient<AdminFlagListResponse>(`/admin/flags${qs ? `?${qs}` : ""}`);
}

export function getAdminFlag(id: string): Promise<AdminFlagDetail> {
  return apiClient<AdminFlagDetail>(`/admin/flags/${encodeURIComponent(id)}`);
}

export function approveAdminFlag(id: string, note?: string): Promise<AdminFlagReviewResult> {
  return apiClient<AdminFlagReviewResult>(`/admin/flags/${encodeURIComponent(id)}/approve`, {
    method: "POST",
    body: JSON.stringify({ note: note ?? null }),
  });
}

export function rejectAdminFlag(id: string, note?: string): Promise<AdminFlagReviewResult> {
  return apiClient<AdminFlagReviewResult>(`/admin/flags/${encodeURIComponent(id)}/reject`, {
    method: "POST",
    body: JSON.stringify({ note: note ?? null }),
  });
}

/**
 * AD19d — apply emergency hold to every active transaction of `userId`. Backs
 * the 04 §8.3 account-flag "Hold" action. `reason` must be ≥10 chars.
 */
export function holdUserTransactions(
  userId: string,
  reason: string,
): Promise<HoldUserTransactionsResult> {
  return apiClient<HoldUserTransactionsResult>(
    `/admin/transactions/hold-by-user/${encodeURIComponent(userId)}`,
    { method: "POST", body: JSON.stringify({ reason }) },
  );
}
