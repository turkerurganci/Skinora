import { useTranslations } from "next-intl";
import type { TransactionDetailParty } from "@/lib/api/transactions";
import { UserCard, type UserCardUser } from "@/components/common";

export interface PartiesPanelProps {
  seller: TransactionDetailParty;
  buyer?: TransactionDetailParty | null;
}

function partyToUser(p: TransactionDetailParty): UserCardUser {
  return {
    steamId: p.steamId ?? "",
    username: p.displayName,
    avatarUrl: p.avatarUrl,
    reputationScore: p.reputationScore ?? null,
    completedTransactions: p.completedTransactionCount ?? 0,
    accountAgeText: "",
  };
}

/**
 * 04 §7.3 sabit layout — "Satıcı (C04) ←──→ Alıcı (C04)".
 * Buyer slot is empty until ACCEPTED (07 §7.5 — buyer kept null on the
 * CREATED surface). The placeholder slot still renders so the layout
 * doesn't collapse when the buyer hasn't joined yet.
 */
export function PartiesPanel({ seller, buyer }: PartiesPanelProps) {
  const t = useTranslations("transactionDetail.parties");
  return (
    <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
      <div>
        <p className="mb-1 text-xs font-medium uppercase text-gray-500">{t("seller")}</p>
        <UserCard user={partyToUser(seller)} variant="detailed" />
      </div>
      <div>
        <p className="mb-1 text-xs font-medium uppercase text-gray-500">{t("buyer")}</p>
        {buyer ? (
          <UserCard user={partyToUser(buyer)} variant="detailed" />
        ) : (
          <div className="flex h-full items-center justify-center rounded-lg border border-dashed border-gray-300 bg-gray-50 p-6 text-center text-sm text-gray-500">
            {t("buyerPending")}
          </div>
        )}
      </div>
    </div>
  );
}
