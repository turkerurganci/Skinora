import { apiClient } from "./client";
import type { BlockchainNetwork } from "@/types/enums";

/**
 * Response body for P1 — GET /platform/stats (07 §10.1).
 */
export interface PlatformStats {
  totalCompletedTransactions: number;
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

/**
 * P3 — GET /platform/payout-sources (07 §10.3, T167). The one address per
 * network every seller payout leaves from (the network's hot wallet). A seller
 * declares it to their Turkish exchange as the sender (MASAK — Tedbirler
 * Yönetmeliği m. 24/A-6). Only configured networks are listed; `address` is the
 * display form (EIP-55 on Ethereum).
 */
export interface PayoutSource {
  network: BlockchainNetwork;
  address: string;
}

export interface PlatformPayoutSources {
  sources: PayoutSource[];
}

export function getPayoutSources(): Promise<PlatformPayoutSources> {
  return apiClient<PlatformPayoutSources>("/platform/payout-sources");
}
