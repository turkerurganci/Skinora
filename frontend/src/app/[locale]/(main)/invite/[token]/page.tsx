"use client";

import { use, useEffect } from "react";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { useAuthStore } from "@/lib/stores/auth-store";
import { useTransactionByInvite } from "@/lib/hooks/useTransactionByInvite";
import { useMyProfile } from "@/lib/hooks/useMyProfile";
import { ErrorState, ItemCard, Skeleton } from "@/components/common";
import {
  DetailHeader,
  PartiesPanel,
  StateActionPanel,
  TransactionInfoPanel,
} from "@/components/transactions/detail";
import { TransactionStatus } from "@/types/enums";

interface InviteConsumePageProps {
  params: Promise<{ token: string }>;
}

/**
 * S07 public-invite consume surface (04 §7.3 public variant, 07 §7.5a) —
 * F-INVITE-01. Resolves the OPEN_LINK opaque token (the seller-shared
 * `/invite/:token` link, enumeration-safe) to the CREATED acceptance surface:
 *
 *   • Unauthenticated → trimmed public info + "Giriş Yap ve Kabul Et" CTA that
 *     returns to this same `/invite/:token` after Steam auth.
 *   • Authenticated prospective buyer → full acceptance surface (AcceptForm);
 *     accept stays id-based, the backend enforces the 02 §6.2 first-comer guard.
 *   • Seller opening their own link → seller "waiting" view.
 *
 * Once a party reaches a non-CREATED state (accepted / progressed) we hand off
 * to the canonical `/transactions/:id` detail page; a spent invite for a
 * non-party shows an "unavailable" notice. The route lives under `(main)`
 * (MainShell is unauth-safe — header only, no auth gate).
 */
export default function InviteConsumePage({ params }: InviteConsumePageProps) {
  const { token } = use(params);
  const t = useTranslations("invitePage");
  const locale = useLocale();
  const router = useRouter();

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isSuspended = useAuthStore((s) => s.isSuspended);

  const invite = useTransactionByInvite(token);
  const profile = useMyProfile(isAuthenticated);

  const data = invite.data;
  const isParty = data?.userRole != null;
  const isCreated = data?.status === TransactionStatus.CREATED;

  // A party (seller, or the buyer who already accepted) on a non-CREATED
  // invite belongs on the canonical detail page with the full surface.
  useEffect(() => {
    if (data && isParty && !isCreated) {
      router.replace(`/${locale}/transactions/${data.id}`);
    }
  }, [data, isParty, isCreated, locale, router]);

  if (invite.isLoading) {
    return (
      <div className="mx-auto w-full max-w-5xl space-y-4 px-4 py-6">
        <Skeleton className="h-10 w-2/3" />
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-64 w-full" />
        <Skeleton className="h-32 w-full" />
      </div>
    );
  }

  if (invite.error instanceof ApiError && invite.error.status === 404) {
    return <ErrorState title={t("errors.notFound.title")} message={t("errors.notFound.message")} />;
  }

  if (invite.isError || !data) {
    return (
      <ErrorState
        title={t("errors.generic.title")}
        message={t("errors.generic.message")}
        onRetry={() => invite.refetch()}
      />
    );
  }

  // Spent / progressed invite reached by a non-party visitor — 03 §3.2 step 3
  // "başka bir kullanıcı tarafından kabul edildi".
  if (!isParty && !isCreated) {
    return <ErrorState title={t("unavailable.title")} message={t("unavailable.message")} />;
  }

  // Party on a non-CREATED invite — the redirect effect is in flight.
  if (isParty && !isCreated) {
    return (
      <div className="mx-auto w-full max-w-5xl px-4 py-6">
        <Skeleton className="h-32 w-full" />
      </div>
    );
  }

  return (
    <div className="mx-auto w-full max-w-5xl space-y-4 px-4 py-6">
      <div
        className="rounded-lg border border-blue-200 bg-blue-50 p-4"
        role="note"
        aria-label={t("title")}
      >
        <p className="text-base font-semibold text-blue-900">{t("title")}</p>
        <p className="mt-1 text-sm text-blue-800">{t("subtitle")}</p>
      </div>

      <DetailHeader id={data.id} status={data.status} />

      <div className="grid grid-cols-1 gap-4 md:grid-cols-[2fr_1fr]">
        <ItemCard
          item={{
            steamItemId: data.item.assetId ?? "",
            name: data.item.name,
            type: data.item.type,
            wear: data.item.wear,
            imageUrl: data.item.imageUrl,
            tradeable: true,
          }}
          variant="detailed"
        />
        <TransactionInfoPanel detail={data} />
      </div>

      <PartiesPanel seller={data.seller} buyer={data.buyer} />

      <StateActionPanel
        detail={data}
        defaultRefundAddress={profile.data?.refundWalletAddress ?? null}
        isAuthenticated={isAuthenticated}
        isSuspended={isSuspended}
        onRefetch={() => router.replace(`/${locale}/transactions/${data.id}`)}
        loginReturnTo={`/invite/${token}`}
      />
    </div>
  );
}
