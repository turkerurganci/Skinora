"use client";

import { useTranslations } from "next-intl";
import type { PanelRole } from "./helpers";

export interface InventoryHiddenNoticeProps {
  role: PanelRole;
}

/**
 * 04 §7.3 ACCEPTED note + 03 §2.3 step 3 / §3.5 note — the buyer's Steam
 * inventory could not be read when the seller confirmed readiness, so the
 * 02 §9.2 inventory-evidence path is closed for this transaction.
 *
 * Rendered from the detail envelope's `buyerInventoryVisible === false`
 * (07 §7.5), NOT from the one-shot confirm-ready reply, for two reasons that
 * both matter:
 *   • The condition is standing, not momentary — it holds until delivery — so a
 *     message that vanishes on the next reload would under-report it.
 *   • The obligation it creates is the BUYER's: without inventory evidence,
 *     their own "Teslim Aldım" is the only route to ITEM_DELIVERED, and a
 *     buyer who waits for automatic verification that can never come loses the
 *     transaction to the delivery timeout. The buyer never sees the seller's
 *     confirm-ready reply.
 *
 * Strict `=== false` at the call site: `undefined` means the read has not
 * happened yet (before the seller confirms), which is not the same claim.
 */
export function InventoryHiddenNotice({ role }: InventoryHiddenNoticeProps) {
  const t = useTranslations("transactionDetail.actions.inventoryHidden");
  return (
    <p
      className="rounded-md border border-orange-200 bg-orange-50 p-3 text-sm text-orange-900"
      role="status"
      data-testid={`inventory-hidden-${role}`}
    >
      {t(role)}
    </p>
  );
}
