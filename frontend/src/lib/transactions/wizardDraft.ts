import { BuyerIdentificationMethod, StablecoinType } from "@/types/enums";
import type { SteamInventoryItem } from "@/lib/api/steam";

/**
 * WP2b — sessionStorage draft for the new-transaction wizard
 * (`wizard-hard-refresh-resets`, owner decision 2026-08-24).
 *
 * WP13 already mirrors the wizard *step* in the URL, but deliberately left the
 * form *data* in memory because it holds a live-inventory item object and a
 * wallet address — neither is URL-safe. The consequence was that a hard refresh
 * dropped everything and the step clamp sent the seller back to picking an item
 * from scratch. This module supplies the missing half: the data survives the
 * refresh, so the URL step it lands on is actually reachable.
 *
 * `sessionStorage`, not `localStorage`: a half-finished listing is scoped to the
 * tab the seller is working in. It must not resurrect days later in another tab,
 * and it must not follow a shared link.
 */

const STORAGE_KEY = "skinora.newTransaction.draft.v1";

export interface WizardDraft {
  item: SteamInventoryItem | null;
  stablecoin: StablecoinType;
  price: string;
  paymentTimeoutHours: number;
  method: BuyerIdentificationMethod;
  buyerSteamId: string;
  sellerWalletAddress: string;
  walletConfirmed: boolean;
}

/**
 * Reads the draft, or returns `null` when there is nothing usable.
 *
 * Every failure mode collapses to `null` on purpose — a private window that
 * throws on access, a quota-cleared store, a payload left behind by an older
 * build. Starting the wizard fresh is always a valid outcome; throwing here
 * would break the page over a convenience feature.
 */
export function readWizardDraft(): WizardDraft | null {
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return null;

    const parsed: unknown = JSON.parse(raw);
    if (!isWizardDraft(parsed)) return null;
    return parsed;
  } catch {
    return null;
  }
}

export function writeWizardDraft(draft: WizardDraft): void {
  try {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(draft));
  } catch {
    // Storage unavailable or full — the wizard keeps working from memory.
  }
}

export function clearWizardDraft(): void {
  try {
    window.sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    // Nothing to do — a draft we cannot remove is also one we cannot read.
  }
}

/**
 * Structural check before the draft is trusted. The stored payload is attacker-
 * reachable in the sense that anything in the tab can write to sessionStorage,
 * and it also outlives a deploy, so a shape from an older build must be
 * rejected rather than spread into component state.
 */
function isWizardDraft(value: unknown): value is WizardDraft {
  if (typeof value !== "object" || value === null) return false;
  const d = value as Record<string, unknown>;

  return (
    (d.item === null || isInventoryItem(d.item))
    && typeof d.stablecoin === "string"
    && Object.values(StablecoinType).includes(d.stablecoin as StablecoinType)
    && typeof d.price === "string"
    && typeof d.paymentTimeoutHours === "number"
    && Number.isFinite(d.paymentTimeoutHours)
    && typeof d.method === "string"
    && Object.values(BuyerIdentificationMethod).includes(d.method as BuyerIdentificationMethod)
    && typeof d.buyerSteamId === "string"
    && typeof d.sellerWalletAddress === "string"
    && typeof d.walletConfirmed === "boolean"
  );
}

function isInventoryItem(value: unknown): value is SteamInventoryItem {
  if (typeof value !== "object" || value === null) return false;
  const i = value as Record<string, unknown>;
  // assetId + tradeable are the two fields the wizard's own gating reads
  // (isStep1Valid, and the createTransaction payload); a payload missing either
  // cannot drive the wizard, so it is not a draft worth restoring.
  return typeof i.assetId === "string" && typeof i.tradeable === "boolean";
}
