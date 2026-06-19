"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useLocale } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { getMe } from "@/lib/api/auth";
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
 */
export function AdminGuard({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const locale = useLocale();
  const [token] = useState<string | null>(() =>
    typeof window === "undefined" ? null : window.localStorage.getItem(ACCESS_TOKEN_STORAGE_KEY),
  );

  const { data, isPending, isError } = useQuery({
    queryKey: ["auth", "me"],
    queryFn: getMe,
    enabled: !!token,
    staleTime: 60_000,
  });

  const loggedOut = !token || isError;
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

  // Logged out / still resolving /auth/me / not an admin → render nothing while
  // the redirect (or the round-trip) settles, so admin chrome never flashes.
  if (!allowed) return null;

  return <>{children}</>;
}
