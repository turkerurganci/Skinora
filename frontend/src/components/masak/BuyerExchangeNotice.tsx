"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";

export interface BuyerExchangeNoticeProps {
  /**
   * `full` — the payment screen (SELLER_CONFIRMED, 04 §7.3): every rule the
   * buyer's exchange applies to this withdrawal. `short` — the accept form:
   * the one fact that must be known before committing (USDT has to be held
   * already), plus a link to the help page.
   */
  variant: "full" | "short";
}

/**
 * T167 — what a Turkish exchange does to the buyer's payment (MASAK Genel
 * Tebliği Sıra No: 29, m. 4; Tedbirler Yönetmeliği m. 24/A-6). From the
 * exchange's side a Skinora deposit address is a wallet not registered with
 * any crypto-asset service provider, so the withdrawal waits at least 48 hours
 * after the USDT was bought or deposited (72 on the first withdrawal), stablecoin
 * withdrawals are capped at 3,000 $ a day / 50,000 $ a month, the recipient has
 * to be declared and a 20+ character description given. The payment window is
 * far shorter than the wait, so USDT bought after accepting does not arrive in
 * time. Practical information, not legal advice — the exchange applies the
 * rules, not Skinora.
 */
export function BuyerExchangeNotice({ variant }: BuyerExchangeNoticeProps) {
  const t = useTranslations("masak.buyer");
  const locale = useLocale();
  const helpHref = `/${locale}/support#masak`;

  if (variant === "short") {
    return (
      <p
        className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900"
        role="note"
        data-testid="masak-buyer-short"
      >
        {t("short")}{" "}
        <Link href={helpHref} className="font-medium underline">
          {t("helpLink")}
        </Link>
      </p>
    );
  }

  return (
    <div
      className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900"
      role="note"
      aria-label={t("title")}
      data-testid="masak-buyer-full"
    >
      <p className="mb-1 font-medium">{t("title")}</p>
      <ul className="list-disc space-y-0.5 pl-5">
        <li className="font-semibold" data-testid="masak-buyer-hold">
          {t("holdInAdvance")}
        </li>
        <li data-testid="masak-buyer-limit">{t("dailyLimit")}</li>
        <li data-testid="masak-buyer-declare">{t("declare")}</li>
        <li>{t("description")}</li>
      </ul>
      <p className="mt-2 text-xs text-blue-800">
        {t("source")}{" "}
        <Link href={helpHref} className="font-medium underline">
          {t("helpLink")}
        </Link>
      </p>
    </div>
  );
}
