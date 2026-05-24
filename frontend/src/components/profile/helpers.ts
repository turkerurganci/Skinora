import { formatNumber, formatPercent as formatPercentBase } from "@/lib/utils/format";

/**
 * Mask a TRC-20 wallet address as `TXyz...abc` for display (04 §7.4).
 * Full address must remain visible behind a "Tüm Adresi Göster" toggle.
 */
export function maskWalletAddress(address: string): string {
  if (address.length < 12) return address;
  return `${address.slice(0, 6)}...${address.slice(-4)}`;
}

/**
 * Reputation score / cancel rate / success rate may be null when the user
 * has no completed transactions (06 §3.1). Backend returns null in that
 * case; UI renders an em-dash placeholder. When present, decimal separator
 * follows the active locale (04 §10.3).
 */
export function formatPercent(value: number | null, locale?: string): string {
  if (value === null) return "—";
  return formatPercentBase(value, locale, 1);
}

export function formatScore(value: number | null, locale?: string): string {
  if (value === null) return "—";
  return formatNumber(value, locale, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}
