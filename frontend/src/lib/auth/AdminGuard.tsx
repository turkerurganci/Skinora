"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { getMe } from "@/lib/api/auth";
import { ApiError } from "@/lib/api/client";
import { ErrorState } from "@/components/common";
import { ACCESS_TOKEN_STORAGE_KEY } from "@/lib/stores/auth-store";
import { isAdminRole } from "./roles";

/**
 * Client-side route guard for the /admin surface (WP13 — minimal FE guard,
 * owner decision). Backend authorization stays authoritative: every admin
 * endpoint enforces its permission server-side and returns 403. This guard
 * only spares a non-admin the broken-shell experience by bouncing them out
 * before any admin page mounts; it is not a security boundary.
 *
 * The token is read straight from localStorage (not the Zustand store) so the
 * decision is not subject to the store's post-mount hydration lag, which would
 * otherwise bounce a real admin on the first render. It reuses the shared
 * ["auth","me"] query (AuthInitializer already primes it) — no extra request —
 * and waits for that query to resolve before deciding.
 *
 * F3c — "cevap alamadım" ile "yetkin yok" AYRI ŞEYLER.
 * Önceki hâl `loggedOut = !token || isError` diyordu, yani `/auth/me`'nin
 * HERHANGİ bir sebeple başarısız olması (429, 5xx, ağ kopması) kullanıcıyı
 * sessizce dışarı atıyordu. UI turunda bunun canlı sonucu ölçüldü: rate limit
 * kovası dolunca süper admin, hiçbir hata görmeden kullanıcı paneline
 * atılıyordu — ekran "admin değilsin" diyordu, oysa sistem "şu an bilmiyorum"
 * demeliydi (`UITour-AuthBucketIncludesSessionReads` 🔴).
 *
 * Yeni ayrım:
 *   - 401 → oturum gerçekten yok → ana sayfaya (eski davranış korunur)
 *   - başka hata → GEÇİCİ: yeniden denenebilir hata gösterilir, kullanıcı
 *     dışarı atılmaz. Yanlış yönlendirmektense bilmediğimizi söylemek yeğdir.
 *   - çözüldü ve admin değil → kullanıcı paneline (eski davranış korunur)
 */
export function AdminGuard({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const locale = useLocale();
  const t = useTranslations("adminGuard");
  const [token] = useState<string | null>(() =>
    typeof window === "undefined" ? null : window.localStorage.getItem(ACCESS_TOKEN_STORAGE_KEY),
  );

  const { data, isPending, isError, error, refetch, isFetching } = useQuery({
    queryKey: ["auth", "me"],
    queryFn: getMe,
    enabled: !!token,
    staleTime: 60_000,
  });

  // 401 tek başına "oturum yok" anlamına gelir. Diğer her hata geçicidir.
  const sessionGone = error instanceof ApiError && error.status === 401;
  const transient = isError && !sessionGone;

  const loggedOut = !token || sessionGone;
  const resolved = !!token && !isPending && !isError;
  const allowed = resolved && !!data && isAdminRole(data.role);

  useEffect(() => {
    if (loggedOut) {
      router.replace(`/${locale}`);
      return;
    }
    if (resolved && !allowed) {
      router.replace(`/${locale}/dashboard`);
    }
  }, [loggedOut, resolved, allowed, router, locale]);

  // Geçici hata → kullanıcıyı yönlendirme, ne olduğunu söyle ve tekrar deneme
  // imkânı ver. 429 için mesaj ayrıca isimlendirilir çünkü davranışı farklıdır:
  // beklemek yeterlidir, kimlik veya yetkiyle ilgisi yoktur.
  if (transient) {
    const rateLimited = error instanceof ApiError && error.status === 429;
    return (
      <ErrorState
        className="m-6"
        title={rateLimited ? t("rateLimitedTitle") : t("unavailableTitle")}
        message={rateLimited ? t("rateLimitedMessage") : t("unavailableMessage")}
        onRetry={isFetching ? undefined : () => void refetch()}
      />
    );
  }

  // Logged out / still resolving /auth/me / not an admin → render nothing while
  // the redirect (or the round-trip) settles, so admin chrome never flashes.
  if (!allowed) return null;

  return <>{children}</>;
}
