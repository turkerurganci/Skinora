import { useTranslations } from "next-intl";
import { StatusBadge, type ExtendedStatus } from "@/components/common";

export interface DetailHeaderProps {
  id: string;
  status: ExtendedStatus;
}

/**
 * 04 §7.3 sabit layout — başlık satırı: "İşlem #<short-id>" + StatusBadge.
 * Short ID is the first 8 chars of the GUID — full ID still shown in the
 * tooltip for support / dispute lookups.
 */
export function DetailHeader({ id, status }: DetailHeaderProps) {
  const t = useTranslations("transactionDetail.header");
  const shortId = id.slice(0, 8);
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <h1 className="text-2xl font-semibold text-gray-900" title={id}>
        {t("title", { id: shortId })}
      </h1>
      <StatusBadge status={status} testId="tx-status-badge" />
    </div>
  );
}
