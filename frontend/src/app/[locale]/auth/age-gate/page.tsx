"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { InfoScreen } from "@/components/auth";

/**
 * Bu ekrana BIRBIRIYLE ILGISIZ iki sebepten gelinir ve tek metin ikisine de
 * hizmet edemez (backlog `AgeGateMessageDescribesWrongRule`):
 *
 *  1. Kullanici TOS penceresindeki 18+ kutusunu reddetti. Burada "en az 18
 *     yasinda olmalisiniz" DOGRU metindir.
 *  2. Backend girisi engelledi cunku STEAM HESABI `auth.min_steam_account_age_days`
 *     esiginden gencti. Bunun kullanicinin yasiyla hicbir ilgisi yok
 *     (`SettingsBasedAgeGateCheck` yasa hic bakmaz, hesabin gun sayisina bakar).
 *
 * Olculdu (2026-08-28): 4 gunluk bir Steam hesabiyla girildi, ekran "18 yasinda
 * degilsiniz" dedi, hesabin sahibi 18 yasindan buyuktu. Kullanici ne yapmasi
 * gerektigini ogrenemiyordu — mesaj "yasini buyut" diyordu, cozum ise beklemekti.
 *
 * Ayrimi iki query parametresi tasir; yoksa 18+ metni varsayilan kalir (1. sebep).
 */
export default function AgeGatePage() {
  const t = useTranslations("auth.ageGate");
  const locale = useLocale();
  const searchParams = useSearchParams();

  const accountAgeDays = Number(searchParams.get("accountAgeDays"));
  const requiredDays = Number(searchParams.get("requiredDays"));
  const isAccountAgeBlock =
    Number.isFinite(accountAgeDays) &&
    Number.isFinite(requiredDays) &&
    searchParams.has("accountAgeDays") &&
    searchParams.has("requiredDays") &&
    requiredDays > 0;

  // Esik degistiyse ya da saat kaydiysa negatif cikabilir; kullaniciya "-2 gun
  // sonra deneyin" demektense en az 1 gosteriyoruz.
  const remainingDays = Math.max(1, requiredDays - accountAgeDays);

  return (
    <InfoScreen
      tone="danger"
      icon={isAccountAgeBlock ? "⏳" : "🔞"}
      title={isAccountAgeBlock ? t("accountAgeTitle") : t("title")}
      description={
        isAccountAgeBlock
          ? t("accountAgeDescription", {
              required: requiredDays,
              days: accountAgeDays,
              remaining: remainingDays,
            })
          : t("description")
      }
      actions={
        <Link
          href={`/${locale}`}
          className="inline-flex flex-1 items-center justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-gray-400 focus:ring-offset-2"
        >
          {t("backToHome")}
        </Link>
      }
    />
  );
}
