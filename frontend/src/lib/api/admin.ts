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
 * AD10 — Admin Steam-account monitoring (S18, 07 §9.10).
 * Wire format mirrors the backend `AdminSteamAccountsResponse`
 * (`Skinora.Steam/Application/Admin/AdminSteamBotDtos.cs`, T63). The per-account
 * shape is the shared {@link AdminSteamAccount} already consumed by the S12
 * dashboard. `warningMessage` is a server-built Turkish summary, non-null when
 * at least one bot is not ACTIVE. NOTE (T103 deferred, owner-approved Option A):
 * `recoveryTransactionCount` / `failoverStatus` / `restrictionReason` are
 * forward-deferred to the T69 bot-health/failover pipeline — the backend still
 * reports `0` / `"NONE"` / `null` for every row, so the S18 recovery queue
 * renders structurally but stays empty until that pipeline feeds AD10.
 * ────────────────────────────────────────────────────────────────────────── */

/** AD10 envelope (07 §9.10). */
export interface AdminSteamAccountsResponse {
  accounts: AdminSteamAccount[];
  warningMessage: string | null;
}

export function getAdminSteamAccounts(): Promise<AdminSteamAccountsResponse> {
  return apiClient<AdminSteamAccountsResponse>("/admin/steam-accounts");
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
  // Account-flag columns (07 §9.2, 04 §8.2) — populated only for ACCOUNT_LEVEL
  // rows; null on transaction flags.
  signalSummary: string | null;
  linkedAccountCount: number | null;
  activeTransactionCount: number | null;
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

/** Supporting-signal entry (07 §9.3) — IP_ADDRESS / DEVICE_FINGERPRINT / SOURCE_ADDRESS. */
export interface MultiAccountSupportingSignal {
  type: string;
  value: string;
  linkedAccounts: MultiAccountLinkedAccount[];
}

export interface MultiAccountFlagDetail {
  matchType: string;
  matchValue: string;
  linkedAccounts: MultiAccountLinkedAccount[];
  supportingSignals: MultiAccountSupportingSignal[];
}

/** Role of the flagged user in a {@link FlagActiveTransaction} (07 §9.3). */
export type FlagTransactionRole = "SELLER" | "BUYER";

/** One active (non-terminal) transaction of the flagged user (AD3 — 04 §8.3). */
export interface FlagActiveTransaction {
  id: string;
  status: TransactionStatus;
  itemName: string;
  price: number;
  stablecoin: StablecoinType;
  role: FlagTransactionRole;
  isOnHold: boolean;
  createdAt: string;
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
  activeTransactions: FlagActiveTransaction[];
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

/* ──────────────────────────────────────────────────────────────────────────
 * AD6 / AD7 + AD19 / AD19b / AD19c — Admin transaction list + detail (S15 / S16).
 * Wire format mirrors `PagedResult<AdminTransactionListItemDto>` (07 §9.6) and
 * `AdminTransactionDetailDto` (07 §9.7), plus the AD19 / AD19b / AD19c lifecycle
 * responses (07 §9.20–§9.22, T59). Enums serialize as strings
 * (`JsonStringEnumConverter`); `decimal` money fields serialize as JSON numbers.
 * ────────────────────────────────────────────────────────────────────────── */

/**
 * Coarse S15 "Durum" group (04 §8.4). The single `status` filter cannot express
 * the multi-status ACTIVE / CANCELLED buckets, so AD6 resolves the group
 * server-side (07 §9.6, T101 backend addition). ACTIVE = non-terminal (includes
 * FLAGGED, matches the AD1 dashboard `activeTransactions` counter).
 */
export type AdminTransactionStatusGroup = "ACTIVE" | "COMPLETED" | "CANCELLED" | "FLAGGED";

/** RESUME or CANCEL — the AD19c release-hold action (07 §9.22). */
export type EmergencyHoldReleaseAction = "RESUME" | "CANCEL";

/** Buyer/seller view shared by AD6 + AD7 (07 §9.6 / §9.7). */
export interface AdminTransactionParty {
  steamId: string;
  displayName: string;
  avatarUrl: string | null;
}

/** One row of the AD6 list (07 §9.6). */
export interface AdminTransactionListItem {
  id: string;
  itemName: string;
  itemImageUrl: string | null;
  price: number;
  stablecoin: StablecoinType;
  status: TransactionStatus;
  seller: AdminTransactionParty;
  buyer: AdminTransactionParty | null;
  createdAt: string;
  completedAt: string | null;
}

/** AD6 page envelope — `PagedResult<T>` (07 §2.4 / §9.6). */
export interface AdminTransactionListResponse {
  items: AdminTransactionListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface AdminTransactionListQuery {
  status?: TransactionStatus;
  statusGroup?: AdminTransactionStatusGroup;
  stablecoin?: StablecoinType;
  dateFrom?: string;
  dateTo?: string;
  minAmount?: number;
  maxAmount?: number;
  search?: string;
  sortBy?: string;
  sortOrder?: "asc" | "desc";
  page?: number;
  pageSize?: number;
}

// ── AD7 detail sub-records (07 §9.7) ─────────────────────────────────────────

export interface AdminTxStatusHistory {
  fromStatus: TransactionStatus | null;
  toStatus: TransactionStatus;
  changedAt: string;
  trigger: string;
}

export interface AdminTxPaymentDetail {
  paymentAddress: string | null;
  receivedAmount: number;
  receivedTxHash: string | null;
  blockConfirmations: number;
  confirmedAt: string | null;
}

export interface AdminTxSellerPayoutDetail {
  grossAmount: number;
  commission: number;
  gasFee: number | null;
  gasFeeFromCommission: number;
  gasFeeFromSeller: number;
  netAmount: number;
  txHash: string | null;
  sentAt: string | null;
}

export interface AdminTxRefundDetail {
  originalAmount: number;
  gasFee: number | null;
  netRefundAmount: number;
  refundAddress: string | null;
  txHash: string | null;
  refundedAt: string | null;
}

export interface AdminTxNotification {
  type: string;
  recipient: string;
  channels: string[];
  sentAt: string;
}

export interface AdminTxDispute {
  id: string;
  type: string;
  status: string;
  autoCheckResult: string | null;
  escalatedAt: string;
  closedAt: string | null;
}

export interface AdminTxFlag {
  id: string;
  type: string;
  reviewStatus: string;
  adminNote: string | null;
  reviewedAt: string | null;
}

/** Derived from current state (07 §9.7). */
export interface AdminTxAdminActions {
  canApproveFlag: boolean;
  canRejectFlag: boolean;
  canCancel: boolean;
}

/** AD7 detail body (07 §9.7). */
export interface AdminTransactionDetail {
  id: string;
  status: TransactionStatus;
  itemName: string;
  itemImageUrl: string | null;
  itemExterior: string | null;
  itemInspectLink: string | null;
  price: number;
  stablecoin: StablecoinType;
  commissionRate: number;
  commissionAmount: number;
  totalAmount: number;
  paymentTimeoutMinutes: number;
  seller: AdminTransactionParty;
  buyer: AdminTransactionParty | null;
  createdAt: string;
  acceptedAt: string | null;
  itemEscrowedAt: string | null;
  paymentReceivedAt: string | null;
  itemDeliveredAt: string | null;
  completedAt: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
  isOnHold: boolean;
  emergencyHoldAt: string | null;
  emergencyHoldReason: string | null;
  statusHistory: AdminTxStatusHistory[];
  paymentDetail: AdminTxPaymentDetail | null;
  sellerPayoutDetail: AdminTxSellerPayoutDetail | null;
  refundDetail: AdminTxRefundDetail | null;
  notificationHistory: AdminTxNotification[];
  disputeHistory: AdminTxDispute[];
  flagHistory: AdminTxFlag[];
  adminActions: AdminTxAdminActions;
}

// ── AD19 / AD19b / AD19c lifecycle responses (07 §9.20–§9.22) ────────────────

export interface AdminCancelTransactionResult {
  status: TransactionStatus;
  cancelledAt: string;
  itemReturned: boolean;
  paymentRefunded: boolean;
}

export interface ApplyEmergencyHoldResult {
  status: string;
  frozenAt: string;
  previousStatus: TransactionStatus;
}

export interface ReleaseEmergencyHoldResult {
  status: TransactionStatus;
  releasedAt: string;
  action: EmergencyHoldReleaseAction;
  itemReturned: boolean | null;
  paymentRefunded: boolean | null;
}

export function listAdminTransactions(
  query: AdminTransactionListQuery,
): Promise<AdminTransactionListResponse> {
  const params = new URLSearchParams();
  if (query.status) params.set("status", query.status);
  if (query.statusGroup) params.set("statusGroup", query.statusGroup);
  if (query.stablecoin) params.set("stablecoin", query.stablecoin);
  if (query.dateFrom) params.set("dateFrom", query.dateFrom);
  if (query.dateTo) params.set("dateTo", query.dateTo);
  if (query.minAmount !== undefined) params.set("minAmount", String(query.minAmount));
  if (query.maxAmount !== undefined) params.set("maxAmount", String(query.maxAmount));
  if (query.search) params.set("search", query.search);
  if (query.sortBy) params.set("sortBy", query.sortBy);
  if (query.sortOrder) params.set("sortOrder", query.sortOrder);
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  const qs = params.toString();
  return apiClient<AdminTransactionListResponse>(`/admin/transactions${qs ? `?${qs}` : ""}`);
}

export function getAdminTransaction(id: string): Promise<AdminTransactionDetail> {
  return apiClient<AdminTransactionDetail>(`/admin/transactions/${encodeURIComponent(id)}`);
}

/** AD19 — admin cancel ("İşlemi İptal Et", 03 §8.7). `reason` must be ≥10 chars. */
export function cancelAdminTransaction(
  id: string,
  reason: string,
): Promise<AdminCancelTransactionResult> {
  return apiClient<AdminCancelTransactionResult>(
    `/admin/transactions/${encodeURIComponent(id)}/cancel`,
    { method: "POST", body: JSON.stringify({ reason }) },
  );
}

/** AD19b — apply emergency hold ("Emergency Hold Uygula", 03 §8.8). `reason` ≥10 chars. */
export function applyEmergencyHold(id: string, reason: string): Promise<ApplyEmergencyHoldResult> {
  return apiClient<ApplyEmergencyHoldResult>(
    `/admin/transactions/${encodeURIComponent(id)}/emergency-hold`,
    { method: "POST", body: JSON.stringify({ reason }) },
  );
}

/**
 * AD19c — release an emergency hold ("Hold Kaldır", 03 §8.8). `action` = RESUME
 * (continue) or CANCEL (cancel the transaction); `note` must be ≥1 char.
 */
export function releaseEmergencyHold(
  id: string,
  action: EmergencyHoldReleaseAction,
  note: string,
): Promise<ReleaseEmergencyHoldResult> {
  return apiClient<ReleaseEmergencyHoldResult>(
    `/admin/transactions/${encodeURIComponent(id)}/release-hold`,
    { method: "POST", body: JSON.stringify({ action, note }) },
  );
}

/* ──────────────────────────────────────────────────────────────────────────
 * AD8 / AD9 — Admin system-settings management (S17).
 * Wire format mirrors the backend `SettingsListResponse` / `SettingItemDto`
 * (07 §9.8) and the AD9 update response (07 §9.9). The `SystemSettingsCatalog`
 * (58 keys) is the source of truth for which keys are returned — keys absent
 * from the catalog are omitted. `category` is the lowercase API dialect; the
 * DTO carries no impact-scope field, so the UI derives it from `category`
 * (04 §8.6 — see `lib/admin/settingsCatalog`). `value` is `null` for keys that
 * have not been configured yet (06 §3.17 `IsConfigured = false`).
 * ────────────────────────────────────────────────────────────────────────── */

/** API valueType (07 §9.8) — `int`/`decimal` collapse to `number`. */
export type AdminSettingValueType = "number" | "boolean" | "string";

/** One setting row of the AD8 list (07 §9.8). */
export interface AdminSettingItem {
  key: string;
  value: string | null;
  category: string;
  label: string;
  description: string | null;
  unit: string | null;
  valueType: AdminSettingValueType;
}

/** AD8 envelope (07 §9.8). */
export interface AdminSettingsListResponse {
  settings: AdminSettingItem[];
}

/** AD9 success body (07 §9.9). */
export interface UpdateSettingResult {
  key: string;
  value: string;
  updatedAt: string;
}

export function listAdminSettings(): Promise<AdminSettingsListResponse> {
  return apiClient<AdminSettingsListResponse>("/admin/settings");
}

/** AD9 — update a single setting by key (07 §9.9). Backend validates `value`. */
export function updateAdminSetting(key: string, value: string): Promise<UpdateSettingResult> {
  return apiClient<UpdateSettingResult>(`/admin/settings/${encodeURIComponent(key)}`, {
    method: "PUT",
    body: JSON.stringify({ value }),
  });
}
