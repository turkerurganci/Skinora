"use client";

import { useTranslations } from "next-intl";
import { CopyButton } from "@/components/common";

export interface ProfileHeaderProps {
  displayName: string;
  avatarUrl: string | null;
  steamId: string | null;
  accountAge: string;
  variant: "own" | "public";
}

/**
 * 04 §7.4 / §7.5 başlık bloğu. `variant=own` Steam ID + CopyButton
 * gösterir; `variant=public` Steam ID'yi gizler (S09 "Gösterilmeyenler"
 * listesi — 04 §7.5).
 *
 * Avatar fallback'ı: avatarUrl null ise initials çemberi (UserCard ile
 * aynı görsel paralel — burada inline tanımlı çünkü S08/S09 büyük avatar
 * istiyor).
 */
export function ProfileHeader({
  displayName,
  avatarUrl,
  steamId,
  accountAge,
  variant,
}: ProfileHeaderProps) {
  const t = useTranslations("profile.header");
  const initials = displayName.slice(0, 2).toUpperCase();

  return (
    <section className="flex flex-col items-center gap-3 rounded-lg border border-gray-200 bg-white p-6 sm:flex-row sm:items-start sm:gap-6">
      <div className="h-24 w-24 shrink-0 overflow-hidden rounded-full bg-gray-200">
        {avatarUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={avatarUrl}
            alt={displayName}
            className="h-full w-full object-cover"
          />
        ) : (
          <div className="flex h-full w-full items-center justify-center text-2xl font-semibold text-gray-500">
            {initials}
          </div>
        )}
      </div>
      <div className="flex flex-1 flex-col gap-2 text-center sm:text-left">
        <h1 className="text-2xl font-semibold text-gray-900">{displayName}</h1>
        {variant === "own" && steamId && (
          <div className="flex items-center justify-center gap-2 sm:justify-start">
            <span className="text-sm text-gray-600">
              {t("steamIdLabel")}
              <code className="ml-1 break-all rounded-md bg-gray-100 px-2 py-1 font-mono text-xs text-gray-800">
                {steamId}
              </code>
            </span>
            <CopyButton value={steamId} label={t("copySteamId")} />
          </div>
        )}
        <p className="text-sm text-gray-600">
          {t("accountAge", { age: accountAge })}
        </p>
      </div>
    </section>
  );
}
