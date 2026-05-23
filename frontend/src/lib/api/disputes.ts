import { apiClient } from "./client";
import type { DisputeType, DisputeStatus } from "@/types/enums";

// ---------- POST /transactions/:id/disputes (07 §7.8 — T8) ----------

/**
 * Auto-check section returned inside the open-dispute response (07 §7.8).
 * Structured object — distinct from the simpler `string?` field surfaced by
 * `TransactionDetailDispute.autoCheckResult` in 07 §7.5. Backend produces
 * Turkish-only messages; T97 will handle locale-aware backend messaging.
 */
export interface DisputeAutoCheckResult {
  resolved: boolean;
  message: string;
  canSubmitTxHash: boolean;
  canEscalate: boolean;
}

export interface OpenDisputeRequest {
  type: DisputeType;
}

export interface OpenDisputeResponse {
  id: string;
  type: DisputeType;
  status: DisputeStatus;
  autoCheckResult: DisputeAutoCheckResult;
  createdAt: string;
}

export function openDispute(
  transactionId: string,
  body: OpenDisputeRequest,
): Promise<OpenDisputeResponse> {
  return apiClient<OpenDisputeResponse>(
    `/transactions/${encodeURIComponent(transactionId)}/disputes`,
    {
      method: "POST",
      body: JSON.stringify(body),
    },
  );
}

// ---------- POST /transactions/:id/disputes/:disputeId/submit-txhash (07 §7.9 — T9) ----------

export interface SubmitTxHashRequest {
  txHash: string;
}

/**
 * Inner payload returned by submit-txhash (07 §7.9). Backend keeps this
 * intentionally narrower than the open-dispute response — once a dispute
 * exists, only the resolution outcome matters.
 */
export interface DisputeTxHashCheckResult {
  resolved: boolean;
  message: string;
}

export interface SubmitTxHashResponse {
  checkResult: DisputeTxHashCheckResult;
}

export function submitDisputeTxHash(
  transactionId: string,
  disputeId: string,
  body: SubmitTxHashRequest,
): Promise<SubmitTxHashResponse> {
  return apiClient<SubmitTxHashResponse>(
    `/transactions/${encodeURIComponent(transactionId)}/disputes/${encodeURIComponent(disputeId)}/submit-txhash`,
    {
      method: "POST",
      body: JSON.stringify(body),
    },
  );
}

// ---------- POST /transactions/:id/disputes/:disputeId/escalate (07 §7.10 — T10) ----------

export interface EscalateDisputeRequest {
  detail: string;
}

export interface EscalateDisputeResponse {
  status: DisputeStatus;
  escalatedAt: string;
  message: string;
}

export function escalateDispute(
  transactionId: string,
  disputeId: string,
  body: EscalateDisputeRequest,
): Promise<EscalateDisputeResponse> {
  return apiClient<EscalateDisputeResponse>(
    `/transactions/${encodeURIComponent(transactionId)}/disputes/${encodeURIComponent(disputeId)}/escalate`,
    {
      method: "POST",
      body: JSON.stringify(body),
    },
  );
}
