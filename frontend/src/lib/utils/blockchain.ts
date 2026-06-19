/**
 * Tronscan explorer base for TRC-20 transactions (WP13 — "Tronscan URL sabit").
 * Centralised here so the explorer host is defined once and can be pointed at a
 * testnet explorer via env in non-production environments.
 */
export const TRONSCAN_TX_BASE_URL =
  process.env.NEXT_PUBLIC_TRONSCAN_TX_BASE_URL ?? "https://tronscan.org/#/transaction/";

export function tronscanTxUrl(txHash: string): string {
  return `${TRONSCAN_TX_BASE_URL}${txHash}`;
}
