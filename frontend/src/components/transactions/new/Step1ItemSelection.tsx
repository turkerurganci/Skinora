"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { EmptyState, ErrorState, ItemCard, Skeleton, type ItemCardItem } from "@/components/common";
import type { SteamInventoryItem } from "@/lib/api/steam";

const INITIAL_VISIBLE = 50;
const PAGE_INCREMENT = 50;

export interface Step1ItemSelectionProps {
  inventory: SteamInventoryItem[] | undefined;
  totalCount: number | undefined;
  tradeableCount: number | undefined;
  isLoading: boolean;
  isError: boolean;
  errorCode: string | null;
  selectedAssetId: string | null;
  onSelect: (item: SteamInventoryItem) => void;
  onRetry: () => void;
}

function toCardItem(item: SteamInventoryItem): ItemCardItem {
  return {
    steamItemId: item.assetId,
    name: item.name,
    type: item.type,
    wear: item.wear,
    imageUrl: item.imageUrl,
    tradeable: item.tradeable,
  };
}

export function Step1ItemSelection({
  inventory,
  totalCount,
  tradeableCount,
  isLoading,
  isError,
  errorCode,
  selectedAssetId,
  onSelect,
  onRetry,
}: Step1ItemSelectionProps) {
  const t = useTranslations("newTransaction.step1");
  const [query, setQuery] = useState("");
  const [visibleCount, setVisibleCount] = useState(INITIAL_VISIBLE);
  const [prevQuery, setPrevQuery] = useState(query);
  const sentinelRef = useRef<HTMLDivElement | null>(null);

  // Reset the pagination window inline (React's "store the previous prop"
  // pattern) so the user lands at the top of the new view whenever the
  // search filter changes. Replaces an effect-with-setState that ESLint
  // (react-hooks/set-state-in-effect) flags as a cascading render.
  if (query !== prevQuery) {
    setPrevQuery(query);
    setVisibleCount(INITIAL_VISIBLE);
  }

  // Filter by case-insensitive name match.
  const filtered = useMemo(() => {
    if (!inventory) return [];
    const trimmed = query.trim().toLowerCase();
    if (!trimmed) return inventory;
    return inventory.filter((item) => item.name.toLowerCase().includes(trimmed));
  }, [inventory, query]);

  useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel) return;
    if (visibleCount >= filtered.length) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setVisibleCount((c) => Math.min(c + PAGE_INCREMENT, filtered.length));
        }
      },
      { rootMargin: "120px" },
    );
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [visibleCount, filtered.length]);

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-10 w-full" />
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {Array.from({ length: 8 }).map((_, i) => (
            <Skeleton key={i} className="h-44" />
          ))}
        </div>
      </div>
    );
  }

  if (isError) {
    const isPrivate = errorCode === "INVENTORY_PRIVATE";
    return (
      <ErrorState
        title={isPrivate ? t("error.privateTitle") : t("error.title")}
        message={isPrivate ? t("error.privateMessage") : t("error.message")}
        onRetry={isPrivate ? undefined : onRetry}
      />
    );
  }

  if (!inventory || inventory.length === 0) {
    return <EmptyState title={t("empty.title")} description={t("empty.description")} />;
  }

  const visible = filtered.slice(0, visibleCount);
  const hasMore = visibleCount < filtered.length;

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <h2 className="text-lg font-semibold text-gray-900">{t("title")}</h2>
        {typeof tradeableCount === "number" && (
          <p className="text-sm text-gray-600">
            {t("counts", { tradeable: tradeableCount, total: totalCount ?? 0 })}
          </p>
        )}
      </div>

      <input
        type="search"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder={t("searchPlaceholder")}
        className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
        aria-label={t("searchPlaceholder")}
      />

      {filtered.length === 0 ? (
        <EmptyState title={t("noMatch.title")} description={t("noMatch.description", { query })} />
      ) : (
        <>
          <div
            role="radiogroup"
            aria-label={t("title")}
            className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4"
          >
            {visible.map((item) => {
              const isSelected = selectedAssetId === item.assetId;
              const disabled = !item.tradeable;
              return (
                <div
                  key={item.assetId}
                  className={disabled ? "pointer-events-none opacity-60" : ""}
                  title={disabled ? t("nonTradeableTooltip") : undefined}
                >
                  <ItemCard
                    variant="selectable"
                    item={toCardItem(item)}
                    selected={isSelected && !disabled}
                    onSelect={disabled ? undefined : () => onSelect(item)}
                  />
                </div>
              );
            })}
          </div>
          {hasMore && (
            <div ref={sentinelRef} className="flex justify-center py-4" aria-hidden="true">
              <Skeleton className="h-8 w-32" />
            </div>
          )}
        </>
      )}
    </div>
  );
}
