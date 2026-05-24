"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { CountdownTimer, StatusBadge } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import { formatDateTime, formatStablecoin } from "@/lib/utils/format";
import type { TransactionListItem } from "@/lib/api/transactions";

const AVATAR_PLACEHOLDER =
  "data:image/svg+xml;utf8,%3Csvg%20xmlns%3D'http%3A//www.w3.org/2000/svg'%20viewBox%3D'0%200%2040%2040'%3E%3Ccircle%20cx%3D'20'%20cy%3D'20'%20r%3D'20'%20fill%3D'%23e5e7eb'/%3E%3Ccircle%20cx%3D'20'%20cy%3D'16'%20r%3D'7'%20fill%3D'%239ca3af'/%3E%3Cpath%20d%3D'M8%2034c0-7%205-12%2012-12s12%205%2012%2012'%20fill%3D'%239ca3af'/%3E%3C/svg%3E";

const ITEM_PLACEHOLDER =
  "data:image/svg+xml;utf8,%3Csvg%20xmlns%3D'http%3A//www.w3.org/2000/svg'%20viewBox%3D'0%200%2064%2064'%3E%3Crect%20width%3D'64'%20height%3D'64'%20fill%3D'%23f3f4f6'/%3E%3Cpath%20d%3D'M16%2044l8-12%208%208%208-12%208%2016z'%20fill%3D'%239ca3af'/%3E%3C/svg%3E";

function shortenId(id: string): string {
  return `#${id.slice(0, 8)}`;
}

export interface TransactionRowProps {
  item: TransactionListItem;
  readOnly?: boolean;
  className?: string;
}

export function TransactionRow({ item, readOnly, className }: TransactionRowProps) {
  const t = useTranslations("dashboard.row");
  const locale = useLocale();
  const detailHref = `/${locale}/transactions/${item.id}`;

  const counterparty = item.counterparty;
  const warningSeconds = item.activeTimeout
    ? Math.max(
        60,
        Math.round(
          (item.activeTimeout.remainingSeconds * item.activeTimeout.warningThresholdPercent) / 100,
        ),
      )
    : 0;

  const rowClasses = cn(
    "flex flex-col gap-3 rounded-lg border border-gray-200 bg-white p-4 transition-shadow",
    !readOnly && "hover:border-blue-300 hover:shadow-sm focus-within:border-blue-400",
    "sm:flex-row sm:items-center sm:gap-4",
    className,
  );

  const content = (
    <>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src={item.itemImageUrl ?? ITEM_PLACEHOLDER}
        alt=""
        aria-hidden="true"
        className="h-12 w-12 flex-none rounded border border-gray-200 bg-gray-50 object-contain"
      />

      <div className="min-w-0 flex-1">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="truncate text-sm font-semibold text-gray-900">{item.itemName}</p>
            <p className="mt-0.5 text-xs text-gray-500">{shortenId(item.id)}</p>
          </div>
          <StatusBadge status={item.status} className="flex-none" />
        </div>

        <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-gray-600">
          <span className="font-medium text-gray-900 tabular-nums">
            {formatStablecoin(item.price, item.stablecoin)}
          </span>

          {counterparty ? (
            <span className="inline-flex items-center gap-1.5">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={counterparty.avatarUrl ?? AVATAR_PLACEHOLDER}
                alt=""
                aria-hidden="true"
                className="h-4 w-4 rounded-full"
              />
              <span className="max-w-[12rem] truncate">{counterparty.displayName}</span>
            </span>
          ) : (
            <span className="italic text-gray-400">{t("noCounterparty")}</span>
          )}

          <span className="text-gray-500">{formatDateTime(item.createdAt, locale)}</span>
        </div>
      </div>

      {item.activeTimeout && (
        <div className="flex-none sm:text-right">
          <CountdownTimer
            deadline={item.activeTimeout.expiresAt}
            warningThresholdSeconds={warningSeconds}
            format="verbose"
          />
        </div>
      )}
    </>
  );

  if (readOnly) {
    return (
      <div className={rowClasses} aria-disabled="true">
        {content}
      </div>
    );
  }

  return (
    <Link
      href={detailHref}
      className={cn(
        rowClasses,
        "outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2",
      )}
      aria-label={t("rowAriaLabel", { id: shortenId(item.id), itemName: item.itemName })}
    >
      {content}
    </Link>
  );
}
