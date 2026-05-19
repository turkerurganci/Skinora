"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export interface CopyButtonProps {
  value: string;
  className?: string;
  label?: string;
}

export function CopyButton({ value, className, label }: CopyButtonProps) {
  const t = useTranslations("common");
  const [copied, setCopied] = useState(false);

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard API unavailable (insecure context, permission denied) —
      // surface silently; user can still see the value next to the button.
    }
  }

  return (
    <button
      type="button"
      onClick={handleCopy}
      className={cn(
        "inline-flex items-center gap-1 rounded-md border border-gray-300 bg-white px-2 py-1 text-xs text-gray-700 hover:bg-gray-50",
        className,
      )}
      aria-label={label ?? t("copy")}
    >
      {copied ? (
        <>
          <svg
            xmlns="http://www.w3.org/2000/svg"
            className="h-3.5 w-3.5 text-green-600"
            viewBox="0 0 20 20"
            fill="currentColor"
            aria-hidden="true"
          >
            <path
              fillRule="evenodd"
              d="M16.704 5.29a1 1 0 010 1.42l-7.5 7.5a1 1 0 01-1.42 0l-3.5-3.5a1 1 0 011.42-1.42L8.5 12.08l6.79-6.79a1 1 0 011.42 0z"
              clipRule="evenodd"
            />
          </svg>
          {t("copied")}
        </>
      ) : (
        <>
          <svg
            xmlns="http://www.w3.org/2000/svg"
            className="h-3.5 w-3.5"
            viewBox="0 0 20 20"
            fill="currentColor"
            aria-hidden="true"
          >
            <path d="M7 2a2 2 0 00-2 2v10a2 2 0 002 2h6a2 2 0 002-2V6.414A2 2 0 0014.414 5L12 2.586A2 2 0 0010.586 2H7z" />
            <path d="M5 18a2 2 0 01-2-2V6a2 2 0 012-2h.5a.5.5 0 010 1H5a1 1 0 00-1 1v10a1 1 0 001 1h8a1 1 0 001-1v-.5a.5.5 0 011 0v.5a2 2 0 01-2 2H5z" />
          </svg>
          {t("copy")}
        </>
      )}
    </button>
  );
}
