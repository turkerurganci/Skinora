import { apiClient } from "./client";
import {
  EmergencyHoldReleaseAction,
  DisputeResolutionOutcome,
  DisputeStatus,
  DisputeType,
  BlockchainNetwork,
  StablecoinType,
  TransactionStatus,
} from "@/types/enums";

/**
 * AD1 — `GET /admin/dashboard` (07 §9.1). Wire format matches the backend
 * `AdminDashboardResponse` record (T63 `Skinora.API/Services/AdminDashboardDtos.cs`).
 * Enums serialize as strings via `JsonStringEnumConverter`.
 */

export type AdminFlagType =
  | "PRICE_DEVIATION"
  | "HIGH_VOLUME"
  | "ABNORMAL_BEHAVIOR"
  | "MULTI_ACCOUNT"
  | "SANCTIONS_MATCH"
  // T129 — settlement-window delivery reversal (02 §4.5.1).
  | "DELIVERY_REVERSED";

export type AdminFlagReviewStatus = "PENDING" | "APPROVED" | "REJECTED";

export interface AdminDashboardSummaryCards {
  activeTransactions: number;
  pendingFlags: number;
  dailyCompleted: number;
  weeklyCompleted: number;
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

/** Inventory read outcome reported by the settlement check (T129). */
export type FlagInventoryVisibility = "Public" | "Private" | "Unavailable";

/**
 * `flagDetail` for DELIVERY_REVERSED (T129 — 02 §4.5.1). The flag is
 * ACCOUNT_LEVEL, so the row carries no `transactionId` (06 §3.12) and the
 * reversed transaction is named here instead. Mirrors the backend
 * `DeliveryReversedFlagDetail` record.
 */
export interface DeliveryReversedFlagDetail {
  transactionId: string;
  itemName: string | null;
  itemAssetId: string | null;
  deliveredBuyerAssetId: string | null;
  itemDeliveredAt: string | null;
  payoutEligibleAt: string | null;
  detectedAt: string;
  buyerVisibility: FlagInventoryVisibility | null;
  sellerVisibility: FlagInventoryVisibility | null;
  /** Buyer's observed count of the traded item class (count route only). */
  observedClassCount: number | null;
  /** The count the delivery established — baseline + 1. */
  expectedClassCount: number | null;
  detail: string | null;
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
  /** AD2 sort column: createdAt (default), type, reviewStatus (07 §9.2). */
  sortBy?: string;
  /** asc | desc (default desc). */
  sortOrder?: string;
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
  if (query.sortBy) params.set("sortBy", query.sortBy);
  if (query.sortOrder) params.set("sortOrder", query.sortOrder);
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
 * AD27 / AD28 / AD29 — Admin dispute resolution (WP5 / T58, 07 §9.x).
 * Closes the ESCALATED dead-end: list the queue, inspect a dispute, resolve it
 * in favor of the seller (uphold) or the buyer (unwind → REFUNDED + refund).
 * ────────────────────────────────────────────────────────────────────────── */

/** Party summary (buyer / seller) on an admin dispute row. */
export interface AdminDisputeParty {
  userId: string;
  steamId: string | null;
  displayName: string;
}

/** One row of the AD27 dispute queue (07 §9.x). */
export interface AdminDisputeListItem {
  id: string;
  transactionId: string;
  type: DisputeType;
  status: DisputeStatus;
  itemName: string;
  transactionStatus: TransactionStatus;
  openedBy: AdminDisputeParty;
  createdAt: string;
}

/** AD27 page envelope (PagedResult). */
export interface AdminDisputeListResponse {
  items: AdminDisputeListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Embedded transaction view returned by AD28 (07 §9.x). */
export interface AdminDisputeTransaction {
  id: string;
  status: TransactionStatus;
  itemName: string;
  price: number;
  stablecoin: StablecoinType;
  isOnHold: boolean;
  hasActiveDispute: boolean;
  seller: AdminDisputeParty;
  buyer: AdminDisputeParty | null;
}

/** AD28 detail body (07 §9.x). */
export interface AdminDisputeDetail {
  id: string;
  type: DisputeType;
  status: DisputeStatus;
  systemCheckResult: string | null;
  /**
   * T130 — the Steam name of the item that actually arrived on a WRONG_ITEM
   * auto-escalation (07 §9.30). The server omits the field entirely on every
   * other dispute, so `undefined` means "not this kind of case" rather than
   * "unknown" — the row is not rendered at all.
   */
  deliveredItemName?: string;
  userDescription: string | null;
  adminId: string | null;
  adminNote: string | null;
  /** T131 — recorded justification of a past ruling that overrode a proven delivery. */
  resolutionOverrideReason?: string;
  /**
   * T131 — server-computed: does a BUYER_FAVOR ruling on this dispute need an
   * override reason (03 §6.4)? The rule lives in the service; the client only
   * renders the answer, so the two cannot drift apart.
   */
  buyerFavorRequiresOverride: boolean;
  resolvedAt: string | null;
  createdAt: string;
  updatedAt: string;
  transaction: AdminDisputeTransaction;
}

/** AD29 success body (07 §9.x). */
export interface AdminResolveDisputeResult {
  id: string;
  status: DisputeStatus;
  transactionStatus: TransactionStatus;
  resolvedAt: string;
  buyerRefunded: boolean;
}

export interface AdminDisputeListQuery {
  status?: DisputeStatus;
  type?: DisputeType;
  page?: number;
  pageSize?: number;
}

export function listAdminDisputes(query: AdminDisputeListQuery): Promise<AdminDisputeListResponse> {
  const params = new URLSearchParams();
  if (query.status) params.set("status", query.status);
  if (query.type) params.set("type", query.type);
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  const qs = params.toString();
  return apiClient<AdminDisputeListResponse>(`/admin/disputes${qs ? `?${qs}` : ""}`);
}

export function getAdminDispute(id: string): Promise<AdminDisputeDetail> {
  return apiClient<AdminDisputeDetail>(`/admin/disputes/${encodeURIComponent(id)}`);
}

export function resolveAdminDispute(
  id: string,
  outcome: DisputeResolutionOutcome,
  adminNote: string,
  overrideReason?: string,
): Promise<AdminResolveDisputeResult> {
  return apiClient<AdminResolveDisputeResult>(`/admin/disputes/${encodeURIComponent(id)}/resolve`, {
    method: "POST",
    body: JSON.stringify({ outcome, adminNote, overrideReason }),
  });
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

/**
 * RESUME or CANCEL — the AD19c release-hold action (07 §9.22).
 *
 * WP6a — re-exported from `@/types/enums` rather than redeclared here, so the
 * FE↔C# parity guard (which only reads `enums.ts`) covers it. Existing
 * importers keep working unchanged.
 */
export { EmergencyHoldReleaseAction };

/** Buyer/seller view shared by AD6 + AD7 (07 §9.6 / §9.7). */
export interface AdminTransactionParty {
  steamId: string;
  displayName: string;
  avatarUrl: string | null;
  /**
   * Composite reputation score (06 §3.1) — AD7 detail only (07 §9.7 /
   * 04 §8.5 "Taraf Detayları — skor"); absent on the AD6 list snapshot.
   */
  reputationScore?: number | null;
}

/** One row of the AD6 list (07 §9.6). */
export interface AdminTransactionListItem {
  id: string;
  itemName: string;
  itemImageUrl: string | null;
  price: number;
  stablecoin: StablecoinType;
  /** T157 — chosen payment network; `null` until the buyer accepts. */
  network: BlockchainNetwork | null;
  status: TransactionStatus;
  seller: AdminTransactionParty;
  buyer: AdminTransactionParty | null;
  createdAt: string;
  completedAt: string | null;
  cancelledAt: string | null;
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
  /** T157 — filter by payment network. */
  network?: BlockchainNetwork;
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
  content: string | null;
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
  /** T157 — chosen payment network; `null` before the buyer accepts (read as TRON). */
  network: BlockchainNetwork | null;
  commissionRate: number;
  commissionAmount: number;
  totalAmount: number;
  paymentTimeoutMinutes: number;
  seller: AdminTransactionParty;
  buyer: AdminTransactionParty | null;
  createdAt: string;
  acceptedAt: string | null;
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
  paymentRefunded: boolean | null;
}

export function listAdminTransactions(
  query: AdminTransactionListQuery,
): Promise<AdminTransactionListResponse> {
  const params = new URLSearchParams();
  if (query.status) params.set("status", query.status);
  if (query.statusGroup) params.set("statusGroup", query.statusGroup);
  if (query.stablecoin) params.set("stablecoin", query.stablecoin);
  if (query.network) params.set("network", query.network);
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
  /**
   * False for deployment-configured rows the panel renders read-only — the
   * platform wallet addresses (05 §3.3, owner decision 2026-09-16). Optional
   * so a response from an older backend still parses as editable.
   */
  isEditable?: boolean;
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

/* ──────────────────────────────────────────────────────────────────────────
 * AD11–AD17 — Admin role & permission management (S19).
 * Wire format mirrors the backend `RolesListResponse` / `RoleSummaryDto` /
 * `RoleDetailDto` (07 §9.11–§9.14, `Skinora.Admin/Application/Roles`) and the
 * AD15/AD17 user-role flow (`PagedResult<AdminUserListItemDto>` + `AssignRole*`,
 * 07 §9.15/§9.18). `availablePermissions` is the source of truth for which
 * permissions render and in what order — the UI localizes each label by `key`
 * (the server label is Turkish-fixed; see `lib/admin/permissionCatalog`).
 * `isSuperAdmin` is a backend addition (not in the 07 §9.11 JSON): super-admin
 * roles are rendered read-only so MANAGE_ROLES can't be stripped by accident.
 * ────────────────────────────────────────────────────────────────────────── */

/** One entry of the AD11 `availablePermissions` catalog (07 §9.11). */
export interface AvailablePermission {
  key: string;
  label: string;
}

/** One row of the AD11 roles list (07 §9.11). */
export interface AdminRoleSummary {
  id: string;
  name: string;
  description: string | null;
  isSuperAdmin: boolean;
  permissions: string[];
  assignedUserCount: number;
  createdAt: string;
}

/** AD11 envelope (07 §9.11). */
export interface AdminRolesResponse {
  roles: AdminRoleSummary[];
  availablePermissions: AvailablePermission[];
}

/** AD12 / AD13 request body (07 §9.12 / §9.13 — identical shape). */
export interface RoleWriteRequest {
  name: string;
  description: string | null;
  permissions: string[];
}

/** AD12 / AD13 success body (07 §9.12 / §9.13). */
export interface AdminRoleDetail {
  id: string;
  name: string;
  description: string | null;
  isSuperAdmin: boolean;
  permissions: string[];
  createdAt: string;
}

export function listAdminRoles(): Promise<AdminRolesResponse> {
  return apiClient<AdminRolesResponse>("/admin/roles");
}

/** AD12 — create a role (07 §9.12). Errors: 409 ROLE_NAME_EXISTS, 400 VALIDATION_ERROR/INVALID_PERMISSION. */
export function createAdminRole(request: RoleWriteRequest): Promise<AdminRoleDetail> {
  return apiClient<AdminRoleDetail>("/admin/roles", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/** AD13 — update a role (07 §9.13). Errors: AD12 + 404 ROLE_NOT_FOUND. */
export function updateAdminRole(id: string, request: RoleWriteRequest): Promise<AdminRoleDetail> {
  return apiClient<AdminRoleDetail>(`/admin/roles/${encodeURIComponent(id)}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

/** AD14 — delete a role (07 §9.14). Errors: 404 ROLE_NOT_FOUND, 422 ROLE_HAS_USERS. */
export function deleteAdminRole(id: string): Promise<void> {
  return apiClient<void>(`/admin/roles/${encodeURIComponent(id)}`, { method: "DELETE" });
}

/** Inline role summary on an AD15 user row / AD17 result (07 §9.15 / §9.18). */
export interface AdminUserAssignedRole {
  id: string;
  name: string;
}

/** One row of the AD15 user list (07 §9.15). */
export interface AdminUserListItem {
  id: string;
  steamId: string;
  displayName: string;
  avatarUrl: string | null;
  role: AdminUserAssignedRole | null;
}

/** AD15 page envelope — `PagedResult<T>` (07 §2.4 / §9.15). */
export interface AdminUserListResponse {
  items: AdminUserListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface AdminUserListQuery {
  search?: string;
  roleId?: string;
  page?: number;
  pageSize?: number;
}

/** AD17 success body (07 §9.18). `role`/`assignedAt` are null when the role is cleared. */
export interface AssignRoleResult {
  userId: string;
  role: AdminUserAssignedRole | null;
  assignedAt: string | null;
}

/** AD15 — paginated admin-user list for the S19 role-assignment section (07 §9.15). */
export function listAdminUsers(query: AdminUserListQuery): Promise<AdminUserListResponse> {
  const params = new URLSearchParams();
  if (query.search) params.set("search", query.search);
  if (query.roleId) params.set("roleId", query.roleId);
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  const qs = params.toString();
  return apiClient<AdminUserListResponse>(`/admin/users${qs ? `?${qs}` : ""}`);
}

/** AD17 — assign or clear a user's role (07 §9.18). `roleId = null` removes the role. */
export function assignUserRole(userId: string, roleId: string | null): Promise<AssignRoleResult> {
  return apiClient<AssignRoleResult>(`/admin/users/${encodeURIComponent(userId)}/role`, {
    method: "PUT",
    body: JSON.stringify({ roleId }),
  });
}

/* ──────────────────────────────────────────────────────────────────────────
 * AD16 / AD16b — Admin user detail (S20).
 * Wire format mirrors `AdminUserDetailDto` (07 §9.16) and the AD16b per-user
 * transaction list (07 §9.17 — `PagedResult<AdminTransactionListItemDto>`,
 * identical to AD6 so it reuses `TransactionListTable`). Enums serialize as
 * strings; `reputationScore` is a JSON number or null; `totalVolume` is a
 * decimal string (or null when the user has no completed transaction).
 * `flagHistory[].transactionId` is null for ACCOUNT_LEVEL flags (06 §3.12).
 * `walletHistory` carries current addresses (`current: true`) plus previous
 * addresses (`current: false`, newest first) recorded on each change (T105b).
 * ────────────────────────────────────────────────────────────────────────── */

export type AdminAccountStatus = "ACTIVE" | "SUSPENDED" | "DEACTIVATED" | "DELETED";
export type AdminWalletEntryType = "seller" | "buyer";

/** Profile block of AD16 (07 §9.16 / 04 §8.9.1). */
export interface AdminUserDetailProfile {
  id: string;
  steamId: string;
  displayName: string;
  avatarUrl: string | null;
  accountStatus: AdminAccountStatus;
  accountAge: string;
  createdAt: string;
  reputationScore: number | null;
  isSuspended: boolean;
  suspendedAt: string | null;
  suspensionReason: string | null;
  suspensionExpiresAt: string | null;
  /** Non-terminal transaction count + emergency-hold flag → 04 §8.9.1 badges. */
  activeTransactionCount: number;
  hasTransactionOnHold: boolean;
  /**
   * Reputation breakdown (04 §8.9.1) — the counters that build `reputationScore`.
   * `cancelRate` is the complement of `successfulTransactionRate`; both are
   * fractions 0..1 and both null when the rate is null.
   */
  completedTransactionCount: number;
  successfulTransactionRate: number | null;
  cancelRate: number | null;
}

/** Statistics block of AD16 (07 §9.16 / 04 §8.9.2). */
export interface AdminUserDetailStats {
  totalTransactions: number;
  completedTransactions: number;
  cancelledTransactions: number;
  flaggedTransactions: number;
  successfulTransactionRate: number | null;
  totalVolume: string | null;
  lastTransactionAt: string | null;
}

/** One wallet-address row (04 §8.9.3). `current: false` = a previous address. */
export interface AdminUserWalletEntry {
  type: AdminWalletEntryType;
  address: string;
  setAt: string | null;
  current: boolean;
}

/** One flag-history row (04 §8.9.5). `transactionId` null = account-level. */
export interface AdminUserFlagEntry {
  id: string;
  type: AdminFlagType;
  transactionId: string | null;
  reviewStatus: AdminFlagReviewStatus;
  createdAt: string;
}

/** One dispute-history row (04 §8.9.6). */
export interface AdminUserDisputeEntry {
  id: string;
  type: DisputeType;
  transactionId: string;
  status: DisputeStatus;
  createdAt: string;
}

/** One frequent-counterparty row — wash-trading signal (04 §8.9.7). */
export interface AdminUserCounterparty {
  steamId: string;
  displayName: string;
  transactionCount: number;
  lastTransactionAt: string | null;
}

/** AD16 body — `AdminUserDetailDto` (07 §9.16). */
export interface AdminUserDetail {
  profile: AdminUserDetailProfile;
  stats: AdminUserDetailStats;
  walletHistory: AdminUserWalletEntry[];
  flagHistory: AdminUserFlagEntry[];
  disputeHistory: AdminUserDisputeEntry[];
  frequentCounterparties: AdminUserCounterparty[];
}

/** AD16 — user detail for S20 (07 §9.16). `steamId` is path-encoded. */
export function getAdminUserDetail(steamId: string): Promise<AdminUserDetail> {
  return apiClient<AdminUserDetail>(`/admin/users/${encodeURIComponent(steamId)}`);
}

/** AD16b — a user's transaction history (07 §9.17), reusing the AD6 list shape. */
export function getAdminUserTransactions(
  steamId: string,
  page = 1,
  pageSize = 20,
): Promise<AdminTransactionListResponse> {
  const params = new URLSearchParams();
  params.set("page", String(page));
  params.set("pageSize", String(pageSize));
  return apiClient<AdminTransactionListResponse>(
    `/admin/users/${encodeURIComponent(steamId)}/transactions?${params.toString()}`,
  );
}

/* ──────────────────────────────────────────────────────────────────────────
 * AD18 — Admin audit log list (S21). Wire format mirrors
 * `PagedResult<AuditLogListItemDto>` (07 §9.19). `category` + `action` serialize
 * as enum strings; `detail` is an opaque JSON object (the backend forwards the
 * audit row's stored value verbatim). `search` spans the entity id AND the
 * actor/subject user's Steam ID / display name (04 §8.10 "Kullanıcı" filter —
 * T106 backend addition).
 * ────────────────────────────────────────────────────────────────────────── */

/** The three audit categories (07 §9.19 / 06 §2.19). */
export type AdminAuditCategory = "FUND_MOVEMENT" | "ADMIN_ACTION" | "SECURITY_EVENT";

/** Actor / subject reference (07 §9.19). `steamId` is null for the SYSTEM account. */
export interface AuditLogParticipant {
  steamId: string | null;
  displayName: string;
}

/** One row of the AD18 list (07 §9.19). */
export interface AdminAuditLogItem {
  id: string;
  category: AdminAuditCategory;
  action: string;
  actor: AuditLogParticipant;
  subject: AuditLogParticipant | null;
  transactionId: string | null;
  detail: unknown | null;
  createdAt: string;
}

/** AD18 page envelope — `PagedResult<T>` (07 §2.4 / §9.19). */
export interface AdminAuditLogResponse {
  items: AdminAuditLogItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface AdminAuditLogQuery {
  category?: AdminAuditCategory;
  dateFrom?: string;
  dateTo?: string;
  search?: string;
  transactionId?: string;
  page?: number;
  pageSize?: number;
}

export function listAdminAuditLogs(query: AdminAuditLogQuery): Promise<AdminAuditLogResponse> {
  const params = new URLSearchParams();
  if (query.category) params.set("category", query.category);
  if (query.dateFrom) params.set("dateFrom", query.dateFrom);
  if (query.dateTo) params.set("dateTo", query.dateTo);
  if (query.search) params.set("search", query.search);
  if (query.transactionId) params.set("transactionId", query.transactionId);
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  const qs = params.toString();
  return apiClient<AdminAuditLogResponse>(`/admin/audit-logs${qs ? `?${qs}` : ""}`);
}

/* ──────────────────────────────────────────────────────────────────────────
 * AD38 / AD20 — Admin wallets overview + hot→cold transfer (T157).
 * One entry per network, in enum order. `decimal` money fields on the rows
 * serialize as JSON numbers; balances carry decimal STRINGS so no precision is
 * lost. `address: null` = not configured / unknown; `balances: null` =
 * unavailable (never zero).
 * ────────────────────────────────────────────────────────────────────────── */

export type PlatformWalletRole = "HOT" | "COLD" | "FUEL";
export type WalletBalancesStatus = "OK" | "UNAVAILABLE" | "NOT_CONFIGURED";
export type ColdTransferToken = "USDT" | "USDC";

export interface WalletBalance {
  symbol: string;
  amount: string;
}

export interface PlatformWallet {
  role: PlatformWalletRole;
  address: string | null;
  configuredAddress: string | null;
  /** The configured hot address differs from the one the signing service uses. */
  addressMismatch: boolean;
  balances: WalletBalance[] | null;
  nativeFloor: string | null;
  belowNativeFloor: boolean | null;
}

export interface OutboundQueueSummary {
  pendingCount: number;
  oldestPendingAt: string | null;
  inFlightCount: number;
}

export interface ColdTransfer {
  id: number;
  token: ColdTransferToken;
  amount: number;
  fromAddress: string;
  toAddress: string;
  txHash: string;
  initiatedBy: string;
  createdAt: string;
}

export type OutboundRowStatus = "DETECTED" | "PENDING" | "CONFIRMED" | "FAILED";

export interface OutboundRow {
  id: string;
  transactionId: string;
  /** SELLER_PAYOUT | BUYER_REFUND | EXCESS_REFUND | WRONG_TOKEN_REFUND | INCORRECT_AMOUNT_REFUND | LATE_PAYMENT_REFUND | SWEEP | REFUND_FEE_SWEEP */
  type: string;
  status: OutboundRowStatus;
  token: string;
  amount: number;
  toAddress: string;
  txHash: string | null;
  retryCount: number;
  createdAt: string;
}

/**
 * T160c — refund network fees withheld from buyers and where they are. Decimal
 * strings: stablecoin amounts in USDT terms, chain costs in the network's
 * native asset (`nativeAsset`).
 */
export interface RefundFeeBooks {
  /** What buyers paid for their refunds' chain fees (income). */
  withheld: string;
  /** In the hot wallet (kept by hot-wallet refunds or carried by a confirmed sweep / collection). */
  collected: string;
  /** Carried by a sweep / collection that is not final yet. */
  inTransit: string;
  /** Still on deposits (waiting for a sweep, or not worth collecting yet). */
  onDeposits: string;
  /**
   * T160c düzeltme turu — native asset the PLATFORM spent for those refunds
   * (expense): TRON the TRX sent to the deposit (activation, burn top-up) +
   * those sends' fees; Ethereum the fuel ETH + the fuel's fee (a hot-wallet
   * refund: its receipt fee). Unmeasured rows count with their receipt fee.
   */
  refundChainCost: string;
  /** Same, for the separate collections (the sale's own sweep is not counted). */
  collectionChainCost: string;
  /** USDT value of `refundChainCost`, each row at its confirmation price (measured rows only). */
  refundChainCostUsdt: string;
  /** USDT value of `collectionChainCost`, same rule. */
  collectionChainCostUsdt: string;
  /** `withheld` − both USDT expenses; negative = a loss. An upper bound while `costUnmeasured` > 0. */
  netUsdt: string;
  /** Refund / collection rows whose spend or its USDT value is not recorded. */
  costUnmeasured: number;
}

export interface NetworkWallets {
  network: BlockchainNetwork;
  serviceConfigured: boolean;
  degraded: boolean;
  nativeAsset: string;
  balancesStatus: WalletBalancesStatus;
  balancesBlock: number | null;
  wallets: PlatformWallet[];
  outbound: OutboundQueueSummary;
  recentColdTransfers: ColdTransfer[];
  recentOutbound: OutboundRow[];
  refundFees: RefundFeeBooks;
}

export interface AdminWalletsResponse {
  networks: NetworkWallets[];
}

export interface HotToColdTransferRequest {
  amount: number;
  token: ColdTransferToken;
  network: BlockchainNetwork;
}

export interface HotToColdTransferResult {
  coldTransferId: number;
  txHash: string;
  amount: string;
  token: ColdTransferToken;
  fromAddress: string;
  toAddress: string;
  network: BlockchainNetwork;
}

export function getAdminWallets(): Promise<AdminWalletsResponse> {
  return apiClient<AdminWalletsResponse>("/admin/wallets");
}

/** AD20 — move funds from the hot wallet to the fixed cold wallet of one network. */
export function hotToColdTransfer(
  request: HotToColdTransferRequest,
): Promise<HotToColdTransferResult> {
  return apiClient<HotToColdTransferResult>("/admin/wallets/hot-to-cold-transfer", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/* ──────────────────────────────────────────────────────────────────────────
 * Outbound-row closure tool (T157; T172a — TRON and the row a database restore
 * left behind) — two-person tool that binds an on-chain transaction hash to an
 * outbound row that already went out on chain. It never sends money.
 * ────────────────────────────────────────────────────────────────────────── */

export type OutboundClosureStatus = "PROPOSED" | "APPROVED" | "REJECTED";

export interface OutboundClosureCandidate {
  blockchainTransactionId: string;
  transactionId: string;
  /** T172a — "TRON" | "ETHEREUM". */
  network: string;
  /**
   * T172a — "PENDING", or "FAILED" for a row closed by hand after a database
   * restore (POST_RESTORE_DUPLICATE) that the closure brings back to the real
   * payment.
   */
  rowStatus: OutboundRowStatus;
  type: string;
  /** The stablecoin the transaction expected. */
  token: string;
  /**
   * T157 düzeltme turu — the stablecoin the row moves on chain (a wrong-token
   * refund returns the token that arrived); null when it cannot be resolved,
   * and then the tool refuses the row.
   */
  chainToken: string | null;
  amount: number;
  fromAddress: string;
  toAddress: string;
  retryCount: number;
  createdAt: string;
  hasLiveClosure: boolean;
}

export interface OutboundClosure {
  id: string;
  status: OutboundClosureStatus;
  /** T172a — "TRON" | "ETHEREUM". */
  network: string;
  blockchainTransactionId: string;
  transactionId: string;
  rowType: string;
  token: string;
  /** See {@link OutboundClosureCandidate.chainToken}. */
  chainToken: string | null;
  amount: number;
  fromAddress: string;
  toAddress: string;
  rowStatus: OutboundRowStatus;
  txHash: string;
  reason: string;
  proposedByAdminId: string;
  proposedByName: string;
  proposedAt: string;
  decidedByAdminId: string | null;
  decidedByName: string | null;
  decidedAt: string | null;
  decisionNote: string | null;
}

export interface ProposeOutboundClosureRequest {
  blockchainTransactionId: string;
  txHash: string;
  reason: string;
}

function closureQuery(params: Record<string, string | undefined>): string {
  const pairs = Object.entries(params).filter(
    (entry): entry is [string, string] => entry[1] !== undefined && entry[1] !== "",
  );
  return pairs.length === 0
    ? ""
    : `?${pairs.map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join("&")}`;
}

/** T172a — `network` narrows the list to one network's panel. */
export function listOutboundClosureCandidates(
  network?: string,
): Promise<OutboundClosureCandidate[]> {
  return apiClient<OutboundClosureCandidate[]>(
    `/admin/wallets/outbound-closures/candidates${closureQuery({ network })}`,
  );
}

export function listOutboundClosures(
  status?: OutboundClosureStatus,
  network?: string,
): Promise<OutboundClosure[]> {
  return apiClient<OutboundClosure[]>(
    `/admin/wallets/outbound-closures${closureQuery({ status, network })}`,
  );
}

export function proposeOutboundClosure(
  request: ProposeOutboundClosureRequest,
): Promise<OutboundClosure> {
  return apiClient<OutboundClosure>("/admin/wallets/outbound-closures", {
    method: "POST",
    body: JSON.stringify(request),
  });
}

/** The approver must retype the hash — `txHash` is compared server-side. */
export function approveOutboundClosure(id: string, txHash: string): Promise<OutboundClosure> {
  return apiClient<OutboundClosure>(
    `/admin/wallets/outbound-closures/${encodeURIComponent(id)}/approve`,
    { method: "POST", body: JSON.stringify({ txHash }) },
  );
}

/** Reject, or withdraw one's own proposal. `note` is required. */
export function rejectOutboundClosure(id: string, note: string): Promise<OutboundClosure> {
  return apiClient<OutboundClosure>(
    `/admin/wallets/outbound-closures/${encodeURIComponent(id)}/reject`,
    { method: "POST", body: JSON.stringify({ note }) },
  );
}

/* ──────────────────────────────────────────────────────────────────────────
 * Outbound approvals (T169 — 07 §9.35 AD40/AD41). While the launch gate
 * `outbound.auto_send_enabled` is false, every seller payout and buyer refund
 * waits here for an admin approval; the sweep never does. Approving sends
 * nothing itself — the dispatcher sends the row on its next tick, unchanged.
 * ────────────────────────────────────────────────────────────────────────── */

export interface OutboundApprovalRow {
  blockchainTransactionId: string;
  transactionId: string;
  network: string;
  type: string;
  /** The stablecoin the transaction expected. */
  token: string;
  /** The stablecoin the row moves on chain (a wrong-token refund: the one that arrived). */
  chainToken: string | null;
  amount: number;
  /** Network fee already deducted (refund) or charged to the seller (payout); null when none. */
  gasFee: number | null;
  toAddress: string;
  retryCount: number;
  createdAt: string;
}

export interface OutboundApprovalQueue {
  /** The gate's state: true = rows leave without approval. */
  autoSendEnabled: boolean;
  rows: OutboundApprovalRow[];
  /** T169a — every network's deposit refund daily limit and its last 24 hours. */
  depositRefundLimits?: DepositRefundLimit[];
  /** T169a — refunds the deposit refund daily limit holds right now (AD42 releases one). */
  limitHeldRows?: DepositRefundHeldRow[];
}

/** T169a — one network's deposit refund daily limit (08 §3.3c). */
export interface DepositRefundLimit {
  network: string;
  limitUsdt: number;
  usedLast24hUsdt: number;
}

/** T169a — a refund waiting because deposit refunds reached the network's 24-hour limit. */
export interface DepositRefundHeldRow {
  blockchainTransactionId: string;
  transactionId: string;
  network: string;
  type: string;
  chainToken: string | null;
  amount: number;
  gasFee: number | null;
  toAddress: string;
  heldAt: string;
  approvedAt: string | null;
  createdAt: string;
}

export interface DepositRefundLimitRelease {
  blockchainTransactionId: string;
  network: string;
  type: string;
  amount: number;
  toAddress: string;
  limitUsdt: number;
  usedLast24hUsdt: number;
  releasedAt: string;
  releasedByAdminId: string;
}

export interface OutboundApproval {
  blockchainTransactionId: string;
  network: string;
  type: string;
  amount: number;
  toAddress: string;
  approvedAt: string;
  approvedByAdminId: string;
}

export function listOutboundApprovals(): Promise<OutboundApprovalQueue> {
  return apiClient<OutboundApprovalQueue>("/admin/wallets/outbound-approvals");
}

export function approveOutboundTransfer(
  blockchainTransactionId: string,
): Promise<OutboundApproval> {
  return apiClient<OutboundApproval>(
    `/admin/wallets/outbound-approvals/${encodeURIComponent(blockchainTransactionId)}/approve`,
    { method: "POST" },
  );
}

/** T169a AD42 — let one held refund go past its network's deposit refund daily limit. */
export function releaseDepositRefundLimit(
  blockchainTransactionId: string,
): Promise<DepositRefundLimitRelease> {
  return apiClient<DepositRefundLimitRelease>(
    `/admin/wallets/outbound-approvals/${encodeURIComponent(blockchainTransactionId)}/release-daily-limit`,
    { method: "POST" },
  );
}
