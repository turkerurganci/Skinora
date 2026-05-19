"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export interface PaginationProps {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  className?: string;
}

function buildPageList(current: number, total: number): (number | "...")[] {
  if (total <= 7) {
    return Array.from({ length: total }, (_, i) => i + 1);
  }
  const pages: (number | "...")[] = [1];
  if (current > 3) pages.push("...");
  const start = Math.max(2, current - 1);
  const end = Math.min(total - 1, current + 1);
  for (let p = start; p <= end; p++) pages.push(p);
  if (current < total - 2) pages.push("...");
  pages.push(total);
  return pages;
}

export function Pagination({ currentPage, totalPages, onPageChange, className }: PaginationProps) {
  const t = useTranslations("pagination");
  if (totalPages <= 1) return null;

  const pages = buildPageList(currentPage, totalPages);
  const canPrev = currentPage > 1;
  const canNext = currentPage < totalPages;

  return (
    <nav aria-label={t("ariaLabel")} className={cn("flex items-center gap-1", className)}>
      <button
        type="button"
        onClick={() => canPrev && onPageChange(currentPage - 1)}
        disabled={!canPrev}
        className="rounded-md border border-gray-300 px-2 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-40"
        aria-label={t("previous")}
      >
        «
      </button>
      {pages.map((p, i) =>
        p === "..." ? (
          <span key={`gap-${i}`} className="px-2 text-sm text-gray-400">
            …
          </span>
        ) : (
          <button
            key={p}
            type="button"
            onClick={() => onPageChange(p)}
            aria-current={p === currentPage ? "page" : undefined}
            className={cn(
              "min-w-[2rem] rounded-md border px-2 py-1 text-sm",
              p === currentPage
                ? "border-blue-500 bg-blue-50 font-semibold text-blue-700"
                : "border-gray-300 text-gray-700 hover:bg-gray-50",
            )}
          >
            {p}
          </button>
        ),
      )}
      <button
        type="button"
        onClick={() => canNext && onPageChange(currentPage + 1)}
        disabled={!canNext}
        className="rounded-md border border-gray-300 px-2 py-1 text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-40"
        aria-label={t("next")}
      >
        »
      </button>
    </nav>
  );
}
