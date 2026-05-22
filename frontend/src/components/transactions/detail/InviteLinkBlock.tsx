import { useTranslations } from "next-intl";
import type { TransactionDetailInviteInfo } from "@/lib/api/transactions";
import { CopyButton } from "@/components/common";

export interface InviteLinkBlockProps {
  inviteInfo: TransactionDetailInviteInfo;
}

/**
 * 04 §7.3 — CREATED + satıcı görünüm. Alıcı kayıtlıysa "bildirim gönderildi"
 * bilgisi; kayıt değilse kopyalanabilir davet linki (C12 placeholder).
 */
export function InviteLinkBlock({ inviteInfo }: InviteLinkBlockProps) {
  const t = useTranslations("transactionDetail.invite");
  if (inviteInfo.buyerRegistered) {
    return (
      <div className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900">
        {inviteInfo.buyerNotified ? t("buyerNotified") : t("buyerWillBeNotified")}
      </div>
    );
  }
  return (
    <section className="space-y-2 rounded-md border border-blue-200 bg-blue-50 p-3 text-sm">
      <p className="font-medium text-blue-900">{t("shareLinkLabel")}</p>
      <p className="text-blue-900">{t("description")}</p>
      <div className="flex flex-wrap items-center gap-2 rounded-md border border-blue-200 bg-white px-3 py-2 font-mono text-xs text-gray-900 break-all">
        <span className="flex-1">{inviteInfo.inviteUrl}</span>
        <CopyButton value={inviteInfo.inviteUrl} />
      </div>
    </section>
  );
}
