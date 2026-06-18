"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { TosModal } from "./TosModal";
import { acceptTos, getMe } from "@/lib/api/auth";
import { ApiError } from "@/lib/api/client";
import { useAuthStore } from "@/lib/stores/auth-store";

const CURRENT_TOS_VERSION = process.env.NEXT_PUBLIC_TOS_VERSION ?? "1.0";

/**
 * WP11 (T30) — re-prompts an authenticated user to re-accept the Terms of
 * Service when the version they accepted no longer matches the current version
 * (`NEXT_PUBLIC_TOS_VERSION`). Mounted globally inside Providers; dormant for
 * anonymous sessions and for users already on the current version. First-time
 * acceptance is handled by the Steam callback (new_user flow), so this fires
 * only for an EXISTING acceptance whose version is stale.
 *
 * Reuses the shared `["auth", "me"]` query (also used by AuthInitializer), so
 * it adds no extra request. On acceptance it re-fetches `/auth/me`; the
 * matching version then dismisses the modal.
 */
export function TosRepromptGate() {
  const accessToken = useAuthStore((s) => s.accessToken);
  const t = useTranslations("auth.tos");
  const locale = useLocale();
  const queryClient = useQueryClient();
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const { data } = useQuery({
    queryKey: ["auth", "me"],
    queryFn: getMe,
    enabled: !!accessToken,
    staleTime: 60_000,
  });

  const needsReprompt =
    !!data && data.tosAccepted && data.tosAcceptedVersion !== CURRENT_TOS_VERSION;

  if (!needsReprompt) return null;

  const handleAccept = async () => {
    setSubmitting(true);
    setError(null);
    try {
      await acceptTos(CURRENT_TOS_VERSION);
      await queryClient.invalidateQueries({ queryKey: ["auth", "me"] });
    } catch (err) {
      // Already on this version (e.g. accepted in another tab) → just refresh.
      if (err instanceof ApiError && err.code === "TOS_ALREADY_ACCEPTED") {
        await queryClient.invalidateQueries({ queryKey: ["auth", "me"] });
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
      title={t("reprompt.title")}
      description={t("reprompt.description")}
      submitting={submitting}
      errorMessage={error}
      onAccept={() => void handleAccept()}
    />
  );
}
