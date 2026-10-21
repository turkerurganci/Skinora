"use client";

import { useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { TosModal } from "./TosModal";
import { acceptTos, getMe } from "@/lib/api/auth";
import { ApiError } from "@/lib/api/client";
import { useAuthStore } from "@/lib/stores/auth-store";

const CURRENT_TOS_VERSION = process.env.NEXT_PUBLIC_TOS_VERSION ?? "1.0";

/**
 * Routes where the gate stays dormant: the auth pages own their own flow (the
 * Steam callback shows the first-time modal itself; the age/geo/sanctions
 * screens are where a rejected user lands) and the legal pages must stay
 * readable — the modal's "Terms" link opens `/terms` in a new tab.
 */
const DORMANT_ROUTE = /^\/[^/]+\/(auth|terms|privacy)(\/|$)/;

export type TosGateMode = "first" | "reprompt" | null;

/**
 * Decide which prompt (if any) an authenticated session needs.
 * - `first`: the account has not accepted the ToS + 18+ yet. Since T166 the
 *   backend rejects every gated endpoint with 403 `TOS_NOT_ACCEPTED` in this
 *   state, so the modal is the only way forward. It covers the account that
 *   closed the callback modal: the next sign-in reports `success`, not
 *   `new_user`, and nothing else would show the modal again.
 * - `reprompt`: accepted, but an older version (WP11/T30 — still soft: the
 *   server does not enforce the version).
 */
export function tosGateMode(
  me: { tosAccepted: boolean; tosAcceptedVersion: string | null } | undefined,
  currentVersion: string,
): TosGateMode {
  if (!me) return null;
  if (!me.tosAccepted) return "first";
  if (me.tosAcceptedVersion !== currentVersion) return "reprompt";
  return null;
}

/**
 * Global ToS gate (WP11/T30 re-prompt, T166 first-time acceptance). Mounted
 * inside Providers; dormant for anonymous sessions, for accounts already on
 * the current version, and on {@link DORMANT_ROUTE} pages.
 *
 * Reuses the shared `["auth", "me"]` query (also used by AuthInitializer), so
 * it adds no extra request. On acceptance it invalidates every query: pages
 * behind the modal may have received 403 `TOS_NOT_ACCEPTED` and must refetch.
 */
export function TosRepromptGate() {
  const accessToken = useAuthStore((s) => s.accessToken);
  const t = useTranslations("auth.tos");
  const locale = useLocale();
  const pathname = usePathname() ?? "";
  const router = useRouter();
  const queryClient = useQueryClient();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const { data } = useQuery({
    queryKey: ["auth", "me"],
    queryFn: getMe,
    enabled: !!accessToken,
    staleTime: 60_000,
  });

  const mode = DORMANT_ROUTE.test(pathname) ? null : tosGateMode(data, CURRENT_TOS_VERSION);

  if (mode === null) return null;

  const handleAccept = async () => {
    setSubmitting(true);
    setError(null);
    try {
      await acceptTos(CURRENT_TOS_VERSION);
      await queryClient.invalidateQueries();
      setSubmitting(false);
    } catch (err) {
      // Already on this version (e.g. accepted in another tab) → just refresh.
      if (err instanceof ApiError && err.code === "TOS_ALREADY_ACCEPTED") {
        await queryClient.invalidateQueries();
        setSubmitting(false);
        return;
      }
      setError(t("acceptError"));
      setSubmitting(false);
    }
  };

  return (
    <TosModal
      open
      tosVersion={CURRENT_TOS_VERSION}
      tosHref={`/${locale}/terms`}
      title={mode === "reprompt" ? t("reprompt.title") : undefined}
      description={mode === "reprompt" ? t("reprompt.description") : undefined}
      submitting={submitting}
      errorMessage={error}
      onAccept={() => void handleAccept()}
      onAgeRejected={
        mode === "first" ? () => router.replace(`/${locale}/auth/age-gate`) : undefined
      }
    />
  );
}
