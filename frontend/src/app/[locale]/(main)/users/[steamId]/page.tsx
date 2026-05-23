"use client";

import { use } from "react";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { usePublicUserProfile } from "@/lib/hooks/usePublicUserProfile";
import { ErrorState, Skeleton } from "@/components/common";
import { ProfileHeader, ReputationCard } from "@/components/profile";

interface PublicProfilePageProps {
  params: Promise<{ steamId: string }>;
}

/**
 * S09 — Profil (Başkası — Public) (04 §7.5). Herkese açık (giriş zorunlu
 * değil). Backend `GetPublic` AllowAnonymous + `public` rate-limit bucket
 * ile servis eder.
 *
 * Gösterilmeyenler (04 §7.5):
 *   - Cüzdan adresi
 *   - İptal oranı detayı
 *   - Steam ID (tam)
 *   - Ayarlar veya düzenleme butonları
 *
 * Bu kısıtlar backend DTO seviyesinde uygulanır (`PublicUserProfileDto`
 * — T33). UI ek bir filtreleme yapmaz, sadece S09 variant'ını seçer.
 *
 * 404 USER_NOT_FOUND → ErrorState; diğer hatalar generic.
 */
export default function PublicProfilePage({ params }: PublicProfilePageProps) {
  const { steamId } = use(params);
  const t = useTranslations("publicProfile");
  const profile = usePublicUserProfile(steamId);

  if (profile.isLoading) {
    return (
      <div className="mx-auto w-full max-w-4xl space-y-4 px-4 py-6">
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-24 w-full" />
      </div>
    );
  }

  if (profile.error instanceof ApiError && profile.error.status === 404) {
    return (
      <ErrorState
        title={t("errors.notFound.title")}
        message={t("errors.notFound.message")}
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
      <ProfileHeader
        displayName={data.displayName}
        avatarUrl={data.avatarUrl}
        steamId={null}
        accountAge={data.accountAge}
        variant="public"
      />

      <ReputationCard
        variant="public"
        reputationScore={data.reputationScore}
        completedTransactionCount={data.completedTransactionCount}
        successfulTransactionRate={data.successfulTransactionRate}
      />
    </div>
  );
}
