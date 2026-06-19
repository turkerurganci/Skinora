"use client";

import { useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { getMe } from "@/lib/api/auth";
import { isAdminRole } from "@/lib/auth/roles";
import { useAuthStore } from "@/lib/stores/auth-store";

/**
 * Hydrates session state on app mount (T105a). The access token survives a
 * page refresh in localStorage (apiClient reads the same key), but the Zustand
 * store resets to its defaults, so we re-seed it here and then fetch
 * `/auth/me`. The `isSuspended` flag drives the restricted session
 * (MainShell → SuspendedHeader, 04 §6.7 / S03d) — without this hydration a
 * suspended user would see the normal session after a refresh.
 *
 * Renders nothing; it only writes to the auth store.
 */
export function AuthInitializer() {
  const accessToken = useAuthStore((s) => s.accessToken);
  const setAccessToken = useAuthStore((s) => s.setAccessToken);
  const setProfile = useAuthStore((s) => s.setProfile);

  useEffect(() => {
    if (accessToken) return;
    if (typeof window === "undefined") return;
    const stored = window.localStorage.getItem("access_token");
    if (stored) setAccessToken(stored);
  }, [accessToken, setAccessToken]);

  const { data } = useQuery({
    queryKey: ["auth", "me"],
    queryFn: getMe,
    enabled: !!accessToken,
    staleTime: 60_000,
  });

  useEffect(() => {
    if (!data) return;
    // WP13 — populate isAdmin from the /auth/me role claim. Previously the store
    // field stayed false for everyone; the admin route guard and any role-aware
    // UI now have a reliable signal (backend stays authoritative).
    setProfile({ isSuspended: data.isSuspended, isAdmin: isAdminRole(data.role) });
  }, [data, setProfile]);

  return null;
}
