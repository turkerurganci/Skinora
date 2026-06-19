"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { Footer } from "@/components/layout/Footer";

export interface LegalPageProps {
  /** Translation namespace for this page, e.g. "legal.privacy". */
  namespace: "legal.privacy" | "legal.terms" | "legal.support";
  /** Ordered section ids resolved against `${namespace}.sections.${id}.{heading,body}`. */
  sectionKeys: readonly string[];
}

/**
 * Shared public legal/help page shell (WP13). Renders a localized title,
 * a pre-launch draft notice, an intro, and a fixed ordered list of sections.
 * The structured copy lives in i18n placeholders; the authoritative legal
 * text is authored in WP17 (content-authoring).
 */
export function LegalPage({ namespace, sectionKeys }: LegalPageProps) {
  const t = useTranslations(namespace);
  const tCommon = useTranslations("legal");
  const locale = useLocale();

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

        <p className="mt-4 rounded-md border border-yellow-200 bg-yellow-50 p-3 text-sm text-yellow-900">
          {tCommon("draftNotice")}
        </p>

        <p className="mt-6 text-base leading-relaxed text-gray-700">{t("intro")}</p>

        <div className="mt-8 space-y-8">
          {sectionKeys.map((key) => (
            <section key={key}>
              <h2 className="text-xl font-semibold text-gray-900">
                {t(`sections.${key}.heading`)}
              </h2>
              <p className="mt-2 text-base leading-relaxed text-gray-700">
                {t(`sections.${key}.body`)}
              </p>
            </section>
          ))}
        </div>
      </main>

      <Footer />
    </div>
  );
}
