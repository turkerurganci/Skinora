"use client";

import { useTranslations } from "next-intl";
import { PayoutSourceNotice } from "./PayoutSourceNotice";

/**
 * T167 — the help page's MASAK section body (support page, `#masak`): the
 * buyer's and the seller's side, each a short list, plus the sources. Reuses
 * the screen copy (`masak.buyer`, `masak.seller`) so the help page and the
 * payment screens can never say two different things.
 */
export function MasakHelpSection() {
  const tBuyer = useTranslations("masak.buyer");
  const tHelp = useTranslations("masak.help");

  return (
    <div className="mt-4 space-y-6 text-base leading-relaxed text-gray-700">
      <div>
        <h3 className="text-lg font-semibold text-gray-900">{tHelp("buyerHeading")}</h3>
        <ul className="mt-2 list-disc space-y-1 pl-5">
          <li>{tBuyer("holdInAdvance")}</li>
          <li>{tHelp("paymentWindow")}</li>
          <li>{tBuyer("dailyLimit")}</li>
          <li>{tBuyer("declare")}</li>
          <li>{tBuyer("description")}</li>
        </ul>
      </div>
      <div>
        <h3 className="text-lg font-semibold text-gray-900">{tHelp("sellerHeading")}</h3>
        <div className="mt-2">
          <PayoutSourceNotice compact />
        </div>
      </div>
      <p className="text-sm text-gray-600" data-testid="masak-help-source">
        {tHelp("sources")}
      </p>
    </div>
  );
}
