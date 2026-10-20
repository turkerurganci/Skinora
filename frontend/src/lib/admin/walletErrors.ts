import { ApiError } from "@/lib/api/client";
import { tDynamic } from "@/lib/i18n/dynamicKey";

/**
 * Maps a failed wallet / closure call to a localized message. The backend sends
 * a machine `error.code`; the copy lives in `adminWallets.errors.<CODE>`. A code
 * without an entry (or a non-API failure such as a network drop) falls back to
 * the generic message rather than the raw code.
 *
 * @param t The translator for the `adminWallets` namespace.
 */
export function walletErrorMessage(t: unknown, error: unknown): string {
  const generic = tDynamic(t, "errors.GENERIC", "");
  if (error instanceof ApiError) {
    return tDynamic(t, `errors.${error.code}`, generic);
  }
  return generic;
}
