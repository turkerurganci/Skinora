"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export type UserCardVariant = "compact" | "detailed";

export interface UserCardUser {
  steamId: string;
  username: string;
  avatarUrl?: string;
  reputationScore: number | null;
  completedTransactions: number;
  accountAgeText: string;
}

export interface UserCardProps {
  user: UserCardUser;
  variant: UserCardVariant;
  className?: string;
}

const AVATAR_PLACEHOLDER =
  "data:image/svg+xml;utf8,%3Csvg%20xmlns%3D'http%3A//www.w3.org/2000/svg'%20viewBox%3D'0%200%2040%2040'%3E%3Ccircle%20cx%3D'20'%20cy%3D'20'%20r%3D'20'%20fill%3D'%23e5e7eb'/%3E%3Ccircle%20cx%3D'20'%20cy%3D'16'%20r%3D'7'%20fill%3D'%239ca3af'/%3E%3Cpath%20d%3D'M8%2034c0-7%205-12%2012-12s12%205%2012%2012'%20fill%3D'%239ca3af'/%3E%3C/svg%3E";

function ReputationStars({ score }: { score: number | null }) {
  if (score === null) return null;
  const rounded = Math.round(score);
  return (
    <span className="inline-flex items-center gap-0.5" aria-label={`${score}/5`}>
      {Array.from({ length: 5 }).map((_, i) => (
        <svg
          key={i}
          className={cn("h-3.5 w-3.5", i < rounded ? "text-yellow-400" : "text-gray-300")}
          viewBox="0 0 20 20"
          fill="currentColor"
          aria-hidden="true"
        >
          <path d="M9.049 2.927c.3-.921 1.603-.921 1.902 0l1.518 4.674a1 1 0 00.95.69h4.914c.969 0 1.371 1.24.588 1.81l-3.976 2.888a1 1 0 00-.363 1.118l1.518 4.674c.3.922-.755 1.688-1.54 1.118l-3.976-2.888a1 1 0 00-1.176 0l-3.976 2.888c-.784.57-1.838-.196-1.539-1.118l1.518-4.674a1 1 0 00-.363-1.118L2.075 10.1c-.783-.57-.38-1.81.588-1.81h4.914a1 1 0 00.95-.69l1.518-4.674z" />
        </svg>
      ))}
    </span>
  );
}

export function UserCard({ user, variant, className }: UserCardProps) {
  const t = useTranslations("userCard");
  const [avatarSrc, setAvatarSrc] = useState(user.avatarUrl ?? AVATAR_PLACEHOLDER);

  if (variant === "compact") {
    return (
      <div className={cn("flex items-center gap-2", className)}>
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={avatarSrc}
          alt={user.username}
          onError={() => setAvatarSrc(AVATAR_PLACEHOLDER)}
          className="h-8 w-8 rounded-full"
        />
        <div className="min-w-0">
          <p className="truncate text-sm font-medium">{user.username}</p>
          <ReputationStars score={user.reputationScore} />
        </div>
      </div>
    );
  }

  return (
    <div
      className={cn(
        "flex flex-col items-center gap-3 rounded-lg border border-gray-200 bg-white p-4 text-center sm:flex-row sm:text-left",
        className,
      )}
    >
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src={avatarSrc}
        alt={user.username}
        onError={() => setAvatarSrc(AVATAR_PLACEHOLDER)}
        className="h-16 w-16 rounded-full"
      />
      <div className="flex-1 space-y-1">
        <h3 className="text-base font-semibold">{user.username}</h3>
        <ReputationStars score={user.reputationScore} />
        <p className="text-sm text-gray-600">
          {t("completedTransactions", { count: user.completedTransactions })}
        </p>
        <p className="text-sm text-gray-600">{t("accountAge", { age: user.accountAgeText })}</p>
      </div>
    </div>
  );
}
