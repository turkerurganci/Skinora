import { apiClient } from "./client";
import type { PagedResult } from "@/types/api";
import type { BuyerIdentificationMethod, StablecoinType, TransactionStatus } from "@/types/enums";
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

// ---------- GET /transactions/:id (07 §7.5, T5 / T90 detail page) ----------

export interface TransactionDetailItem {
  assetId?: string;
  name: string;
  type?: string;
  imageUrl?: string;
  wear?: string;
}

export interface TransactionDetailParty {
  steamId?: string;
  displayName: string;
  avatarUrl?: string;
  reputationScore?: number | null;
  completedTransactionCount?: number;
}

/**
 * Timeout block (07 §7.5). When `frozen` is true, the countdown stops
 * decrementing and 04 §7.3 EMERGENCY_HOLD / maintenance freeze banners
 * take over. `warningThresholdPercent` is the boundary (1-100) at which
 * the C02 timer flips red.
 */
export interface TransactionDetailTimeout {
  type: string;
  expiresAt: string;
  remainingSeconds: number;
  warningThresholdPercent?: number | null;
  frozen: boolean;
  frozenReason?: string | null;
  frozenAt?: string | null;
}

export interface TransactionDetailPayment {
  address: string;
  expectedAmount: string;
  stablecoin: StablecoinType;
  network: string;
  status?: string | null;
  txHash?: string | null;
  confirmedAt?: string | null;
}

export interface TransactionDetailSellerPayout {
  grossAmount: string;
  gasFee: string;
  gasFeeFromCommission: string;
  gasFeeFromSeller: string;
  netAmount: string;
  walletAddress: string;
  txHash: string;
  sentAt: string;
}

export interface TransactionDetailRefund {
  originalAmount: string;
  gasFee: string;
  netRefundAmount: string;
  refundAddress: string;
  txHash?: string | null;
  refundedAt?: string | null;
}

export interface TransactionDetailCancelInfo {
  cancelledBy: string;
  reason: string;
  cancelledAt: string;
  itemReturned: boolean;
  paymentRefunded: boolean;
}

export interface TransactionDetailFlagInfo {
  flagType: string;
  message: string;
}

export interface TransactionDetailHoldInfo {
  previousStatus: string;
  reason: string;
  frozenAt: string;
  message: string;
}

export interface TransactionDetailDispute {
  id: string;
  type: string;
  status: string;
  autoCheckResult?: string | null;
  canSubmitTxHash: boolean;
  canEscalate: boolean;
  createdAt: string;
}

export interface TransactionDetailInviteInfo {
  inviteUrl: string;
  buyerRegistered: boolean;
  buyerNotified: boolean;
}

/**
 * Payment edge case event (07 §7.5 paymentEvents). The 04 §7.3 banner copy
 * branches on `type`; INCORRECT_AMOUNT/EXCESS surface received vs expected
 * amounts, WRONG_TOKEN/LATE_PAYMENT are diagnostic-only.
 */
export interface TransactionDetailPaymentEvent {
  type: "INCORRECT_AMOUNT" | "EXCESS_AMOUNT" | "WRONG_TOKEN" | "LATE_PAYMENT" | string;
  receivedAmount?: string | null;
  expectedAmount?: string | null;
  refundTxHash?: string | null;
  occurredAt: string;
}

/**
 * Authoritative action surface (07 §7.5 availableActions). The detail page
 * mirrors these flags onto the conditional buttons in 04 §7.3 — the client
 * never re-derives them locally, server is the source of truth (mobile
 * authenticator, cooldown, Steam ID match etc. all roll into these).
 *
 * `requiresLogin` is only set on the public surface; authenticated callers
 * receive the four boolean flags instead.
 */
export interface TransactionDetailAvailableActions {
  canAccept: boolean;
  canCancel?: boolean | null;
  canDispute?: boolean | null;
  canEscalate?: boolean | null;
  requiresLogin?: boolean | null;
}

/**
 * Full T5 response (07 §7.5). Fields beyond `id/status/item/price/stablecoin/seller`
 * are conditional — see the spec table. `userRole` is null for the public
 * unauthenticated surface; the page uses that as the public/auth switch.
 */
export interface TransactionDetailResponse {
  id: string;
  status: ExtendedStatus;
  userRole?: "seller" | "buyer" | null;
  item: TransactionDetailItem;
  price: string;
  stablecoin: StablecoinType;
  commissionRate?: number | null;
  commissionAmount?: string | null;
  totalAmount?: string | null;
  seller: TransactionDetailParty;
  buyer?: TransactionDetailParty | null;
  timeout?: TransactionDetailTimeout | null;
  payment?: TransactionDetailPayment | null;
  sellerPayout?: TransactionDetailSellerPayout | null;
  refund?: TransactionDetailRefund | null;
  cancelInfo?: TransactionDetailCancelInfo | null;
  flagInfo?: TransactionDetailFlagInfo | null;
  holdInfo?: TransactionDetailHoldInfo | null;
  dispute?: TransactionDetailDispute | null;
  inviteInfo?: TransactionDetailInviteInfo | null;
  paymentEvents?: TransactionDetailPaymentEvent[] | null;
  escrowBotAssetId?: string | null;
  deliveredBuyerAssetId?: string | null;
  // WP12 backend / WP13 FE — Steam trade-offer deep link. Populated only in the
  // two TRADE_OFFER_SENT_TO_* states (null on the public surface and elsewhere).
  steamTradeOfferUrl?: string | null;
  availableActions: TransactionDetailAvailableActions;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export function getTransactionDetail(id: string): Promise<TransactionDetailResponse> {
  return apiClient<TransactionDetailResponse>(`/transactions/${encodeURIComponent(id)}`);
}

/**
 * Resolve an OPEN_LINK invite by its opaque token (07 §7.5a, F-INVITE-01).
 * Returns the same {@link TransactionDetailResponse} shape as the id route:
 * unauthenticated callers get the trimmed public surface (`userRole: null`,
 * `requiresLogin: true`); an authenticated token holder who is not yet a
 * party becomes a prospective buyer (`userRole: "buyer"`, `canAccept: true`).
 * Acceptance still goes through the id-based {@link acceptTransaction}.
 */
export function getTransactionByInvite(token: string): Promise<TransactionDetailResponse> {
  return apiClient<TransactionDetailResponse>(
    `/transactions/by-invite/${encodeURIComponent(token)}`,
  );
}

// ---------- POST /transactions/:id/accept (07 §7.6) ----------

export interface AcceptTransactionRequest {
  refundWalletAddress: string;
  /**
   * T119a — mandatory as of v3.0 (07 §7.6). In the P2P model the seller sends
   * the item straight to this address (02 §2.2 step 6), so it is collected at
   * acceptance time. Must belong to the accepting buyer's own Steam account,
   * otherwise the backend answers 400 INVALID_TRADE_URL.
   */
  steamTradeUrl: string;
}

export interface AcceptTransactionResponse {
  status: TransactionStatus;
  acceptedAt: string;
}

export function acceptTransaction(
  id: string,
  body: AcceptTransactionRequest,
): Promise<AcceptTransactionResponse> {
  return apiClient<AcceptTransactionResponse>(`/transactions/${encodeURIComponent(id)}/accept`, {
    method: "POST",
    body: JSON.stringify(body),
  });
}

// ---------- POST /transactions/:id/cancel (07 §7.7) ----------

export interface CancelTransactionRequest {
  reason: string;
}

export interface CancelTransactionResponse {
  status: TransactionStatus;
  cancelledAt: string;
  itemReturned: boolean;
  paymentRefunded: boolean;
}

export function cancelTransaction(
  id: string,
  body: CancelTransactionRequest,
): Promise<CancelTransactionResponse> {
  return apiClient<CancelTransactionResponse>(`/transactions/${encodeURIComponent(id)}/cancel`, {
    method: "POST",
    body: JSON.stringify(body),
  });
}
