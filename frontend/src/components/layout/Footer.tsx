"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { LanguageSelector } from "@/components/common";
import { cn } from "@/lib/utils/cn";

export interface FooterProps {
  className?: string;
}

export function Footer({ className }: FooterProps) {
  const t = useTranslations("footer");
  const locale = useLocale();

  const href = (path: string) => `/${locale}${path}`;

  return (
    <footer
      className={cn(
        "flex flex-col items-center justify-between gap-3 border-t border-gray-200 bg-white px-4 py-4 text-sm text-gray-600 sm:flex-row",
        className,
      )}
    >
      <div className="flex items-center gap-4">
        <Link href={href("/terms")} className="hover:text-gray-900 hover:underline">
          {t("tos")}
        </Link>
        <Link href={href("/privacy")} className="hover:text-gray-900 hover:underline">
          {t("privacy")}
        </Link>
        <Link href={href("/support")} className="hover:text-gray-900 hover:underline">
          {t("support")}
        </Link>
      </div>
      <LanguageSelector />
    </footer>
  );
}
