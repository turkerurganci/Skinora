import { useTranslations } from "next-intl";

export default function LandingPage() {
  const t = useTranslations("landing");

  return (
    <div className="flex min-h-screen items-center justify-center">
      <div className="text-center">
        <h1 className="text-4xl font-bold">{t("title")}</h1>
        <p className="mt-4 text-lg text-gray-600">{t("subtitle")}</p>
      </div>
    </div>
  );
}
