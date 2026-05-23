"use client";

import { useEffect, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { useAuthStore } from "@/lib/stores/auth-store";
import { useMyProfile } from "@/lib/hooks/useMyProfile";
import { ErrorState, Skeleton } from "@/components/common";
import { SuspendedBanner } from "@/components/dashboard";
import {
  ProfileHeader,
  QuickLinks,
  ReputationCard,
  WalletSection,
  type WalletRole,
} from "@/components/profile";

/**
 * Capture re-auth callback params from `window.location` once on mount.
 * Lazy `useState` initializer runs only on the client (the component is
 * marked `"use client"`), so the SSR guard returns nulls during the server
 * render and the client re-reads on hydration.
 */
function captureReAuthFromUrl(): {
  token: string | null;
  role: WalletRole | null;
} {
  if (typeof window === "undefined") return { token: null, role: null };
  const params = new URLSearchParams(window.location.search);
  const token = params.get("reAuthToken");
  const change = params.get("walletChange");
  const role: WalletRole | null =
    change === "seller" || change === "refund" ? (change as WalletRole) : null;
  return { token: role ? token : null, role };
}

/**
 * S08 — Profil (Kendi) (04 §7.4). Authenticated kullanıcı kendi profilini
 * görüntüler ve cüzdan adreslerini yönetir.
 *
 * Cüzdan değiştirme Steam re-auth flow'u sayfa seviyesinde koordine
 * edilir (T31 + T34 entegrasyonu):
 *
 *   1. WalletSection "Adresi Değiştir" → POST /auth/steam/re-verify
 *      `returnUrl=/profile?walletChange={role}`.
 *   2. Steam redirect → backend callback → `/profile?walletChange={role}&reAuthToken=<token>`.
 *   3. Page mount'unda URL'den token + role parse edilir; ilgili
 *      WalletSection input moduna geçer. Token tek-kullanımlık (5 dk
 *      TTL), wallet update çağrısında consume edilir.
 *   4. Token capture sonrası `router.replace()` ile query param'lar URL'den
 *      silinir — browser history'de token sızıntısı önlenir (referrer
 *      koruması A6'da `Referrer-Policy: same-origin` ile yapıldı; bu
 *      ikincil katman).
 *
 * Known limitations (T-future devir):
 *   K1 — accountAge backend Türkçe verbatim ("3 gün") — T97 i18n devir.
 *   K2 — SignalR realtime profil güncellemesi yok — T96 devir; React Query
 *        invalidate ile manuel refetch.
 *   K3 — Re-auth token URL'den 1 mount'ta okunup state'e taşınır; manuel
 *        URL ziyaretinde (kopyalama) token consume olmadan kalır, yine de
 *        backend GETDEL ile single-use garanti.
 */
export default function ProfilePage() {
  const t = useTranslations("profile");
  const router = useRouter();
  const pathname = usePathname();

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isSuspended = useAuthStore((s) => s.isSuspended);
  const profile = useMyProfile(isAuthenticated);

  const [reAuthState, setReAuthState] = useState(captureReAuthFromUrl);
  const reAuthToken = reAuthState.token;
  const reAuthRole = reAuthState.role;

  // After the lazy initializer captured the token + role, strip them from
  // the URL so the token doesn't linger in browser history. Backend
  // already set `Referrer-Policy: same-origin` on the callback redirect
  // (A6) — this is defense in depth.
  useEffect(() => {
    if (!reAuthToken) return;
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (!params.has("reAuthToken") && !params.has("walletChange")) return;
    params.delete("reAuthToken");
    params.delete("walletChange");
    const query = params.toString();
    router.replace(query ? `${pathname}?${query}` : pathname);
  }, [pathname, reAuthToken, router]);

  function clearReAuth() {
    setReAuthState({ token: null, role: null });
  }

  if (!isAuthenticated) {
    return (
      <ErrorState
        title={t("errors.forbidden.title")}
        message={t("errors.forbidden.message")}
      />
    );
  }

  if (profile.isLoading) {
    return (
      <div className="mx-auto w-full max-w-4xl space-y-4 px-4 py-6">
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }

  if (profile.error instanceof ApiError && profile.error.status === 401) {
    return (
      <ErrorState
        title={t("errors.forbidden.title")}
        message={t("errors.forbidden.message")}
      />
    );
  }

  if (profile.isError || !profile.data) {
    return (
      <ErrorState
        title={t("errors.generic.title")}
        message={t("errors.generic.message")}
        onRetry={() => profile.refetch()}
      />
    );
  }

  const data = profile.data;

  return (
    <div className="mx-auto w-full max-w-4xl space-y-4 px-4 py-6">
      {isSuspended && <SuspendedBanner />}

      <ProfileHeader
        displayName={data.displayName}
        avatarUrl={data.avatarUrl}
        steamId={data.steamId}
        accountAge={data.accountAge}
        variant="own"
      />

      <ReputationCard
        variant="own"
        reputationScore={data.reputationScore}
        completedTransactionCount={data.completedTransactionCount}
        successfulTransactionRate={data.successfulTransactionRate}
        cancelRate={data.cancelRate}
      />

      <WalletSection
        role="seller"
        currentAddress={data.sellerWalletAddress}
        activeReAuthToken={reAuthToken}
        activeRoleFromCallback={reAuthRole}
        onChangeCancelled={() => {
          if (reAuthRole === "seller") clearReAuth();
        }}
        onSavedSuccessfully={clearReAuth}
      />

      <WalletSection
        role="refund"
        currentAddress={data.refundWalletAddress}
        activeReAuthToken={reAuthToken}
        activeRoleFromCallback={reAuthRole}
        onChangeCancelled={() => {
          if (reAuthRole === "refund") clearReAuth();
        }}
        onSavedSuccessfully={clearReAuth}
      />

      <QuickLinks />
    </div>
  );
}
