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
 * Only the fields the S07 detail page consumes are exposed here. The full
 * profile DTO has more (notification preferences, language, telegram link
 * etc.) — they'll be added under their owning task (T93/T94/T95) rather
 * than pre-imported now.
 *
 * `refundWalletAddress` is the user's default refund address (T34/T33).
 * S07 buyer-side CREATED uses it to prefill the C11 wallet input + show a
 * "Değiştir" link; per-transaction override is forward-deferred to T-future
 * (T90 K4) since no backend override field exists today.
 */
export interface UserProfile {
  id: string;
  steamId: string;
  displayName: string;
  avatarUrl: string | null;
  sellerWalletAddress: string | null;
  refundWalletAddress: string | null;
}

export function getMyProfile(): Promise<UserProfile> {
  return apiClient<UserProfile>("/users/me");
}
