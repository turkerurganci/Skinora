import { apiClient } from "./client";

/**
 * Response body for P1 — GET /platform/stats (07 §10.1).
 */
export interface PlatformStats {
  totalCompletedTransactions: number;
  platformUptimePercent: number;
}

/**
 * P2 — GET /platform/maintenance response (07 §10.2).
 * `type` ∈ {PLANNED_MAINTENANCE, PLATFORM_MAINTENANCE, STEAM_OUTAGE, BLOCKCHAIN_DEGRADATION}
 * when `active=true`, otherwise null.
 */
export type MaintenanceType =
  | "PLANNED_MAINTENANCE"
  | "PLATFORM_MAINTENANCE"
  | "STEAM_OUTAGE"
  | "BLOCKCHAIN_DEGRADATION";

export interface PlatformMaintenance {
  active: boolean;
  type: MaintenanceType | null;
  message: string | null;
  plannedEnd: string | null;
}

export function getPlatformStats(): Promise<PlatformStats> {
  return apiClient<PlatformStats>("/platform/stats");
}

export function getPlatformMaintenance(): Promise<PlatformMaintenance> {
  return apiClient<PlatformMaintenance>("/platform/maintenance");
}
