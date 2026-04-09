/**
 * Format a USDT/USDC amount for display.
 */
export function formatAmount(amount: number, token: string = "USDT"): string {
  return `${amount.toFixed(2)} ${token}`;
}

/**
 * Format a date string for display.
 */
export function formatDate(date: string | Date, locale: string = "en"): string {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(date));
}
