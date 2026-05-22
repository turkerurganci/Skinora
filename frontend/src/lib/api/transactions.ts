import { apiClient } from "./client";
import type { PagedResult } from "@/types/api";
import type {
  BuyerIdentificationMethod,
  StablecoinType,
  TransactionStatus,
} from "@/types/enums";
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

// ---------- GET /transactions/eligibility (07 §7.3) ----------

export interface EligibilityConcurrentLimit {
  current: number;
  max: number;
}

export interface EligibilityCancelCooldown {
  active: boolean;
  expiresAt: string | null;
}

export interface EligibilityNewAccountLimit {
  isNewAccount: boolean;
  current: number | null;
  max: number | null;
}

/**
 * Eligibility envelope returned by `GET /transactions/eligibility` (07 §7.3).
 *
 * `reasons` is the canonical source for the S06 "Form Öncesi Engeller" panel
 * (04 §7.2): each string is one of the codes defined by the backend
 * `TransactionErrorCodes.EligibilityReasons` static class — i.e.
 * MOBILE_AUTHENTICATOR_REQUIRED, ACCOUNT_FLAGGED, CANCEL_COOLDOWN_ACTIVE,
 * CONCURRENT_LIMIT_REACHED, NEW_ACCOUNT_LIMIT_REACHED,
 * PAYOUT_ADDRESS_COOLDOWN_ACTIVE, SELLER_WALLET_ADDRESS_MISSING.
 *
 * Backend omits the field with `WhenWritingNull` when the user is eligible,
 * so the client must treat `undefined` as "no blockers".
 */
export interface EligibilityResponse {
  eligible: boolean;
  mobileAuthenticatorActive: boolean;
  concurrentLimit: EligibilityConcurrentLimit;
  cancelCooldown: EligibilityCancelCooldown;
  newAccountLimit: EligibilityNewAccountLimit;
  reasons?: string[];
}

export function getEligibility(): Promise<EligibilityResponse> {
  return apiClient<EligibilityResponse>("/transactions/eligibility");
}

// ---------- GET /transactions/params (07 §7.4) ----------

export interface PaymentTimeoutWindow {
  minHours: number;
  maxHours: number;
  defaultHours: number;
}

/**
 * Form parameters envelope returned by `GET /transactions/params` (07 §7.4).
 *
 * `minPrice` / `maxPrice` are strings to preserve scale-2 decimal fidelity
 * across the JSON boundary. `commissionRate` is a fraction (0.02 → 2%).
 */
export interface TransactionParamsResponse {
  minPrice: string;
  maxPrice: string;
  commissionRate: number;
  paymentTimeout: PaymentTimeoutWindow;
  openLinkEnabled: boolean;
  supportedStablecoins: StablecoinType[];
}

export function getTransactionParams(): Promise<TransactionParamsResponse> {
  return apiClient<TransactionParamsResponse>("/transactions/params");
}

// ---------- POST /transactions (07 §7.2) ----------

/**
 * Request body for `POST /transactions` (07 §7.2). `buyerSteamId` is only
 * required when `buyerIdentificationMethod === STEAM_ID`; for `OPEN_LINK` the
 * field is omitted entirely (backend rejects the combination).
 */
export interface CreateTransactionRequest {
  itemAssetId: string;
  stablecoin: StablecoinType;
  price: string;
  paymentTimeoutHours: number;
  buyerIdentificationMethod: BuyerIdentificationMethod;
  buyerSteamId?: string;
  sellerWalletAddress: string;
}

/**
 * Response body for `POST /transactions` (07 §7.2). When the transaction is
 * auto-flagged by fraud rules the response contains `status: "FLAGGED"` +
 * `flagReason: "PRICE_DEVIATION"`; the form treats this as a successful
 * creation and redirects to the detail page where the FLAGGED banner shows.
 */
export interface CreateTransactionResponse {
  id: string;
  status: TransactionStatus;
  inviteUrl: string;
  createdAt: string;
  flagReason?: string;
}

export function createTransaction(
  body: CreateTransactionRequest,
): Promise<CreateTransactionResponse> {
  return apiClient<CreateTransactionResponse>("/transactions", {
    method: "POST",
    body: JSON.stringify(body),
  });
}
