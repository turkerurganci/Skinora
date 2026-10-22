"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { useFormatter, useLocale, useTranslations } from "next-intl";
import { Footer } from "@/components/layout/Footer";
import { tDynamic } from "@/lib/i18n/dynamicKey";
import { LEGAL_TEXT_DATE, isOperatorInfoComplete } from "@/lib/legal/operator";
import { LegalBody } from "./LegalBody";
import { OperatorInfoBlock } from "./OperatorInfoBlock";
import { useOperatorValues } from "./useOperatorValues";

export interface LegalPageProps {
  /** Translation namespace for this page, e.g. "legal.privacy". */
  namespace: "legal.privacy" | "legal.terms" | "legal.support";
  /** Ordered section ids resolved against `${namespace}.sections.${id}.{heading,body}`. */
  sectionKeys: readonly string[];
  /**
   * T167 — optional content rendered under a section's body, keyed by section
   * id (the support page's MASAK section carries lists and live addresses that
   * a single paragraph cannot hold).
   */
  sectionExtras?: Readonly<Record<string, ReactNode>>;
  /** T168 — list the sections at the top (long documents: terms, privacy). */
  showContents?: boolean;
}

/**
 * Shared public legal/help page shell (WP13, completed in T168). Renders a
 * localized title, the DRAFT notice (while the operator's identity is not
 * filled in — owner decision 2026-10-21, `lib/legal/operator.ts`), the
 * "last updated" date, an intro, an optional contents list, the ordered
 * sections and the operator block.
 *
 * Every intro/body receives the operator values ({legalName}, {address},
 * {courtVenue}, {supportEmail}, …) so a text can name the operator; an empty
 * field shows the "company details to be added" marker. Bodies use the
 * {@link LegalBody} conventions (blank line = paragraph, `- ` = list item).
 */
export function LegalPage({
  namespace,
  sectionKeys,
  sectionExtras,
  showContents = false,
}: LegalPageProps) {
  const t = useTranslations(namespace);
  const tCommon = useTranslations("legal");
  const locale = useLocale();
  const format = useFormatter();
  const values = useOperatorValues();
  const draft = !isOperatorInfoComplete();

  const heading = (key: string) => tDynamic(t, `sections.${key}.heading`, key, values);

  return (
    <div className="flex min-h-screen flex-col bg-white">
      <header className="flex items-center justify-between border-b border-gray-200 px-4 py-3">
        <Link
          href={`/${locale}`}
          className="text-lg font-semibold text-gray-900"
          aria-label="Skinora"
        >
          Skinora
        </Link>
      </header>

      <main className="mx-auto w-full max-w-3xl flex-1 px-4 py-10">
        <Link
          href={`/${locale}`}
          className="text-sm text-gray-500 hover:text-gray-900 hover:underline"
        >
          ← {tCommon("backToHome")}
        </Link>

        <h1 className="mt-4 text-3xl font-bold text-gray-900">{t("title")}</h1>

        <p className="mt-2 text-sm text-gray-500" data-testid="legal-last-updated">
          {tCommon("lastUpdated", {
            date: format.dateTime(new Date(`${LEGAL_TEXT_DATE}T00:00:00Z`), {
              dateStyle: "long",
              timeZone: "UTC",
            }),
          })}
        </p>

        {draft && (
          <p
            className="mt-4 rounded-md border border-yellow-200 bg-yellow-50 p-3 text-sm text-yellow-900"
            role="note"
            data-testid="legal-draft-notice"
          >
            {tCommon("draftNotice")}
          </p>
        )}

        <div className="mt-6">
          <LegalBody text={t("intro", values)} />
        </div>

        {showContents && (
          <nav
            aria-label={tCommon("contents")}
            className="mt-6 rounded-md border border-gray-200 p-4"
          >
            <p className="text-sm font-semibold text-gray-900">{tCommon("contents")}</p>
            <ol className="mt-2 list-decimal space-y-1 pl-5 text-sm">
              {sectionKeys.map((key) => (
                <li key={key}>
                  <a href={`#${key}`} className="text-blue-700 hover:underline">
                    {heading(key)}
                  </a>
                </li>
              ))}
            </ol>
          </nav>
        )}

        <div className="mt-8 space-y-8">
          {sectionKeys.map((key) => (
            <section key={key} id={key} className="scroll-mt-4">
              <h2 className="text-xl font-semibold text-gray-900">{heading(key)}</h2>
              <LegalBody text={tDynamic(t, `sections.${key}.body`, key, values)} />
              {sectionExtras?.[key]}
            </section>
          ))}
        </div>

        <OperatorInfoBlock />
      </main>

      <Footer />
    </div>
  );
}
