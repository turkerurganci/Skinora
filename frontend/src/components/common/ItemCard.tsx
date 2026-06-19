"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export type ItemCardVariant = "compact" | "detailed" | "selectable";

export interface ItemCardItem {
  steamItemId: string;
  name: string;
  type?: string;
  wear?: string;
  imageUrl?: string;
  tradeable: boolean;
}

export interface ItemCardProps {
  item: ItemCardItem;
  variant: ItemCardVariant;
  selected?: boolean;
  onSelect?: (item: ItemCardItem) => void;
  className?: string;
}

const PLACEHOLDER =
  "data:image/svg+xml;utf8,%3Csvg%20xmlns%3D'http%3A//www.w3.org/2000/svg'%20viewBox%3D'0%200%20120%2090'%3E%3Crect%20fill%3D'%23e5e7eb'%20width%3D'120'%20height%3D'90'/%3E%3Ctext%20x%3D'50%25'%20y%3D'50%25'%20text-anchor%3D'middle'%20fill%3D'%239ca3af'%20font-family%3D'sans-serif'%20font-size%3D'14'%20dy%3D'.3em'%3ECS2%3C/text%3E%3C/svg%3E";

export function ItemCard({ item, variant, selected, onSelect, className }: ItemCardProps) {
  const t = useTranslations("itemCard");
  const [imgSrc, setImgSrc] = useState(item.imageUrl ?? PLACEHOLDER);

  const tradeableBadge = (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium",
        item.tradeable ? "bg-green-100 text-green-800" : "bg-red-100 text-red-800",
      )}
    >
      {item.tradeable ? t("tradeable") : t("nonTradeable")}
    </span>
  );

  if (variant === "compact") {
    return (
      <div
        className={cn(
          "flex items-center gap-3 rounded-md border border-gray-200 bg-white p-2",
          className,
        )}
      >
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={imgSrc}
          alt={item.name}
          onError={() => setImgSrc(PLACEHOLDER)}
          className="h-10 w-14 flex-shrink-0 rounded object-cover"
        />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium">{item.name}</p>
        </div>
      </div>
    );
  }

  if (variant === "selectable") {
    return (
      <button
        type="button"
        onClick={() => onSelect?.(item)}
        aria-pressed={selected}
        className={cn(
          "flex w-full flex-col items-start gap-2 rounded-lg border-2 bg-white p-3 text-left transition-colors hover:border-blue-300",
          selected ? "border-blue-500 ring-2 ring-blue-200" : "border-gray-200",
          className,
        )}
      >
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={imgSrc}
          alt={item.name}
          onError={() => setImgSrc(PLACEHOLDER)}
          className="h-24 w-full rounded object-cover"
        />
        <p className="line-clamp-2 text-sm font-medium">{item.name}</p>
        {item.wear && <p className="text-xs text-gray-500">{item.wear}</p>}
        {tradeableBadge}
      </button>
    );
  }

  return (
    <div
      className={cn(
        "flex flex-col gap-3 rounded-lg border border-gray-200 bg-white p-4",
        className,
      )}
    >
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src={imgSrc}
        alt={item.name}
        onError={() => setImgSrc(PLACEHOLDER)}
        className="h-40 w-full rounded object-cover"
      />
      <div className="space-y-1">
        <h3 className="text-base font-semibold">{item.name}</h3>
        {item.type && (
          <p className="text-sm text-gray-600">
            {t("type")}: {item.type}
          </p>
        )}
        {item.wear && (
          <p className="text-sm text-gray-600">
            {t("wear")}: {item.wear}
          </p>
        )}
        {item.steamItemId && (
          <p className="text-sm text-gray-600">
            {t("assetId")}: <span className="font-mono text-xs">{item.steamItemId}</span>
          </p>
        )}
      </div>
      <div>{tradeableBadge}</div>
    </div>
  );
}
