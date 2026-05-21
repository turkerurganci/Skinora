import { useTranslations } from "next-intl";

export function SuspendedBanner() {
  const t = useTranslations("dashboard.suspended");
  return (
    <div
      role="alert"
      className="rounded-md border border-orange-300 bg-orange-50 px-4 py-3 text-sm text-orange-900"
    >
      {t("banner")}
    </div>
  );
}
