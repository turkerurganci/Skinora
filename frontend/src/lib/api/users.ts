import { apiClient } from "./client";

/**
 * Response body for U2 — GET /users/me/stats (07 §5.2).
 * Used by the dashboard quick stats panel (S05, 04 §7.1).
 *
 * `reputationScore` is null until the user has at least one completed
 * transaction — 06 §3.1 + T33 read-path returns null in that case so the
 * client can render an "—" placeholder instead of `0.0`.
 */
export interface UserStats {
  completedTransactionCount: number;
  successfulTransactionRate: number;
  reputationScore: number | null;
}

export function getUserStats(): Promise<UserStats> {
  return apiClient<UserStats>("/users/me/stats");
}

/**
 * Response body for U1 — GET /users/me (07 §5.1).
 *
 * Mirrors the backend `UserProfileDto` (T33). S08 (own profile, 04 §7.4)
 * consumes every field; S07 buyer-side CREATED only reads
 * `refundWalletAddress`.
 *
 * `reputationScore`, `successfulTransactionRate`, `cancelRate` are null
 * until the user has at least one completed transaction (06 §3.1).
 * `accountAge` is a backend-formatted Turkish string ("3 gün", "1 yıl") —
 * locale-aware mapping is T97 forward devir; S08/S09 currently surface it
 * verbatim (same pattern S07 uses).
 */
export interface UserProfile {
  id: string;
  steamId: string;
  displayName: string;
  avatarUrl: string | null;
  accountAge: string;
  createdAt: string;
  reputationScore: number | null;
  completedTransactionCount: number;
  successfulTransactionRate: number | null;
  cancelRate: number | null;
  sellerWalletAddress: string | null;
  refundWalletAddress: string | null;
  mobileAuthenticatorActive: boolean;
  /**
   * T119a — normalized Steam trade URL saved through U17
   * (PUT /users/me/settings/steam/trade-url). Prefills the mandatory
   * `steamTradeUrl` field of the accept form (07 §7.6); null when the buyer
   * has never saved one, in which case they type it during acceptance.
   */
  steamTradeUrl: string | null;
}

export function getMyProfile(): Promise<UserProfile> {
  return apiClient<UserProfile>("/users/me");
}

/**
 * Response body for U17 — PUT /users/me/settings/steam/trade-url (07 §5.16a).
 *
 * The backend answers in three shapes and they are **discriminable from the
 * body alone**, which matters because `apiClient` unwraps on `success` and
 * does not surface the HTTP status:
 *
 *   | Durum                    | HTTP | active | setupGuideUrl |
 *   |--------------------------|------|--------|---------------|
 *   | MA açık                  | 200  | true   | null          |
 *   | MA kapalı                | 200  | false  | **non-null**  |
 *   | Steam erişilemez (pending)| 503 | false  | null          |
 *
 * The 503 branch is an `ApiResponse.Ok` envelope (UsersController U17), so it
 * does NOT throw — `active === false && setupGuideUrl === null` is the pending
 * signal. `SidecarTradeHoldChecker` guarantees the guide URL is present on the
 * MA-off branch and absent when Steam could not be reached, which is what makes
 * this derivable rather than a guess.
 */
export interface TradeUrlUpdateResponse {
  tradeUrl: string;
  mobileAuthenticatorActive: boolean;
  setupGuideUrl: string | null;
}

/**
 * U17 — persist the Steam trade URL and refresh the Mobile Authenticator flag.
 *
 * This is the **only** writer of `User.MobileAuthenticatorVerified`, and until
 * F1 nothing in the UI called it — a new user could never become eligible to
 * create a transaction (`UITour-NoUiPathToVerifyMobileAuthenticator`).
 * Throws `ApiError` with code `INVALID_TRADE_URL` (422) on a malformed URL.
 */
export function updateSteamTradeUrl(tradeUrl: string): Promise<TradeUrlUpdateResponse> {
  return apiClient<TradeUrlUpdateResponse>("/users/me/settings/steam/trade-url", {
    method: "PUT",
    body: JSON.stringify({ tradeUrl }),
  });
}

/**
 * Response body for U5 — GET /users/{steamId} (07 §5.5).
 *
 * S09 (public profile, 04 §7.5) surface. Sensitive fields (wallet
 * addresses, cancelRate, full steamId beyond the path param) are not
 * returned by the backend.
 */
export interface PublicUserProfile {
  steamId: string;
  displayName: string;
  avatarUrl: string | null;
  accountAge: string;
  reputationScore: number | null;
  completedTransactionCount: number;
  successfulTransactionRate: number | null;
}

export function getPublicUserProfile(steamId: string): Promise<PublicUserProfile> {
  return apiClient<PublicUserProfile>(`/users/${encodeURIComponent(steamId)}`);
}

/**
 * Response body for U3/U4 — PUT /users/me/wallet/{seller,refund}
 * (07 §5.3, §5.4). `activeTransactionsUsingOldAddress` is surfaced so
 * S08 can show the "Aktif işlemleriniz mevcut eski adresle tamamlanacaktır"
 * notice (04 §7.4 step 7) when applicable.
 */
export interface UpdateWalletResponse {
  walletAddress: string;
  updatedAt: string;
  activeTransactionsUsingOldAddress: number;
}

/**
 * Re-auth token header sent for wallet changes when the user already has
 * an address on file (07 §4.7 / §5.3 "Ek Auth"). Absent on first-time
 * wallet creation since there is no previous value to protect.
 */
const REAUTH_HEADER = "X-ReAuth-Token";

function buildReAuthHeaders(reAuthToken: string | null): Record<string, string> {
  return reAuthToken ? { [REAUTH_HEADER]: reAuthToken } : {};
}

export function updateSellerWallet(
  walletAddress: string,
  reAuthToken: string | null,
): Promise<UpdateWalletResponse> {
  return apiClient<UpdateWalletResponse>("/users/me/wallet/seller", {
    method: "PUT",
    body: JSON.stringify({ walletAddress }),
    headers: buildReAuthHeaders(reAuthToken),
  });
}

export function updateRefundWallet(
  walletAddress: string,
  reAuthToken: string | null,
): Promise<UpdateWalletResponse> {
  return apiClient<UpdateWalletResponse>("/users/me/wallet/refund", {
    method: "PUT",
    body: JSON.stringify({ walletAddress }),
    headers: buildReAuthHeaders(reAuthToken),
  });
}
