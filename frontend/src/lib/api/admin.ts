import { apiClient } from "./client";

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
