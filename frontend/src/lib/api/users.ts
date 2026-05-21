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
