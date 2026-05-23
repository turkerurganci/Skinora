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
}

export function getMyProfile(): Promise<UserProfile> {
  return apiClient<UserProfile>("/users/me");
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
