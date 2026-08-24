"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { useAuthStore } from "@/lib/stores/auth-store";
import { LanguageSelector } from "@/components/common";
import { useRouter } from "next/navigation";
import { signOut } from "@/lib/auth/signOut";
import { cn } from "@/lib/utils/cn";

export interface HeaderProps {
  className?: string;
  unreadNotifications?: number;
}

export function Header({ className, unreadNotifications = 0 }: HeaderProps) {
  const t = useTranslations("nav");
  const locale = useLocale();
  const router = useRouter();
  const displayName = useAuthStore((s) => s.displayName);
  const avatarUrl = useAuthStore((s) => s.avatarUrl);

  const href = (path: string) => `/${locale}${path}`;

  // F7a — `Session-NoSignOutInMainHeader`. Sıradan kullanıcı oturumunu
  // arayüzden kapatamıyordu: logout()'un üç çağıranı da bu başlığın dışındaydı
  // (admin başlığı, askıya alınmış başlık, hesap kapatma). Ortak bilgisayarda
  // gerçek bir sorun. `nav.signOut` dört dilde zaten vardı; eksik olan düğmeydi.
  async function handleSignOut() {
    await signOut();
    router.replace(href("/"));
  }

  return (
    <header
      className={cn(
        "flex items-center justify-between border-b border-gray-200 bg-white px-4 py-3",
        className,
      )}
    >
      <Link
        href={href("/dashboard")}
        className="text-lg font-semibold text-gray-900"
        aria-label="Skinora"
      >
        Skinora
      </Link>

      <nav className="flex items-center gap-3" aria-label={t("primary")}>
        <Link
          href={href("/notifications")}
          className="relative inline-flex items-center rounded-md px-2 py-1 text-sm text-gray-700 hover:bg-gray-50"
          aria-label={t("notifications")}
        >
          <span aria-hidden="true">🔔</span>
          <span className="ml-1 hidden sm:inline">{t("notifications")}</span>
          {unreadNotifications > 0 && (
            <span
              className="absolute -right-1 -top-1 inline-flex h-4 min-w-[1rem] items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-semibold text-white"
              aria-label={`${unreadNotifications} ${t("unread")}`}
            >
              {unreadNotifications > 99 ? "99+" : unreadNotifications}
            </span>
          )}
        </Link>

        <Link
          href={href("/profile")}
          className="inline-flex items-center gap-2 rounded-md px-2 py-1 text-sm text-gray-700 hover:bg-gray-50"
          aria-label={t("profile")}
        >
          {avatarUrl ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={avatarUrl}
              alt=""
              className="h-6 w-6 rounded-full border border-gray-200 object-cover"
            />
          ) : (
            <span
              className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-gray-200 text-xs text-gray-600"
              aria-hidden="true"
            >
              {displayName ? displayName.charAt(0).toUpperCase() : "?"}
            </span>
          )}
          <span className="hidden sm:inline">{t("profile")}</span>
        </Link>

        <LanguageSelector />

        <Link
          href={href("/settings")}
          className="inline-flex items-center rounded-md px-2 py-1 text-sm text-gray-700 hover:bg-gray-50"
          aria-label={t("settings")}
        >
          <span aria-hidden="true">⚙️</span>
          <span className="ml-1 hidden sm:inline">{t("settings")}</span>
        </Link>

        <button
          type="button"
          onClick={() => void handleSignOut()}
          data-testid="sign-out"
          className="inline-flex items-center rounded-md px-2 py-1 text-sm text-gray-700 hover:bg-gray-50"
          aria-label={t("signOut")}
        >
          <span aria-hidden="true">🚪</span>
          <span className="ml-1 hidden sm:inline">{t("signOut")}</span>
        </button>
      </nav>
    </header>
  );
}
