"use client";

import { useTranslations } from "next-intl";
import { OPERATOR_FIELD_ORDER, OPERATOR_INFO, SUPPORT_EMAIL } from "@/lib/legal/operator";

/**
 * T168 — "who runs Skinora" block at the foot of every legal page, read from
 * the single operator record (`lib/legal/operator.ts`). Empty fields show the
 * "company details to be added" marker — the block is never hidden, so a
 * missing identity is visible rather than silently absent.
 */
export function OperatorInfoBlock() {
  const t = useTranslations("legal.operator");

  return (
    <section
      id="operator"
      className="mt-10 scroll-mt-4 rounded-md border border-gray-200 bg-gray-50 p-4"
      data-testid="legal-operator-block"
    >
      <h2 className="text-base font-semibold text-gray-900">{t("heading")}</h2>
      <dl className="mt-3 grid grid-cols-1 gap-x-4 gap-y-2 text-sm sm:grid-cols-[12rem_1fr]">
        {OPERATOR_FIELD_ORDER.map((field) => {
          const value = OPERATOR_INFO[field].trim();
          return (
            <div key={field} className="contents">
              <dt className="font-medium text-gray-600">{t(`fields.${field}`)}</dt>
              <dd
                className={value === "" ? "italic text-amber-700" : "text-gray-900"}
                data-testid={`operator-${field}`}
              >
                {value === "" ? t("pending") : value}
              </dd>
            </div>
          );
        })}
        <div className="contents">
          <dt className="font-medium text-gray-600">{t("fields.email")}</dt>
          <dd className="text-gray-900">
            <a href={`mailto:${SUPPORT_EMAIL}`} className="text-blue-700 hover:underline">
              {SUPPORT_EMAIL}
            </a>
          </dd>
        </div>
      </dl>
    </section>
  );
}
