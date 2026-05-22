"use client";

import { use } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { useAuthStore } from "@/lib/stores/auth-store";
import { useTransactionDetail } from "@/lib/hooks/useTransactionDetail";
import { useMyProfile } from "@/lib/hooks/useMyProfile";
import { ErrorState, ItemCard, Skeleton, TransactionTimeline } from "@/components/common";
import { SuspendedBanner } from "@/components/dashboard";
import {
  CancelInfoBlock,
  DetailHeader,
  DisputeBlock,
  FlagHoldBanner,
  InviteLinkBlock,
  PartiesPanel,
  PaymentEventBanners,
  PaymentInfoBlock,
  SellerPayoutSummary,
  StateActionPanel,
  TransactionInfoPanel,
} from "@/components/transactions/detail";
import { TransactionStatus } from "@/types/enums";
import { isCancelledStatus, isEmergencyHold } from "@/components/transactions/detail/helpers";

interface TransactionDetailPageProps {
  params: Promise<{ id: string }>;
}

/**
 * S07 — İşlem Detay (04 §7.3). Platformun en karmaşık tekil ekranı:
 *
 *   • Sabit layout: header + timeline + (item + info + parties).
 *   • State × role action panel — 12 state × seller/buyer + public surface.
 *   • Conditional panels: paymentInfo (buyer ITEM_ESCROWED), payout
 *     (seller COMPLETED), cancelInfo (CANCELLED_*), flag/hold banners,
 *     dispute, invite link, payment edge case banners.
 *   • Suspended session override: banner + tüm aksiyonlar disabled.
 *
 * Known limitations (T-future devir):
 *   K1 — SignalR real-time güncellemeler (T96). Şimdilik React Query
 *        staleTime=5s + window-focus refetch + onSuccess invalidate.
 *   K2 — Dispute butonu disabled, T92 DisputeForm wiring devir.
 *   K3 — Steam trade offer URL (TRADE_OFFER_SENT_TO_* state'leri). DTO'da
 *        link yok; spec'deki "Steam'e git" CTA T-future devir.
 *   K4 — İade adresi "Değiştir" linki disabled. Backend AcceptRequest tek
 *        adres alanı + cooldown check yapıyor; per-transaction override
 *        field T-future.
 */
export default function TransactionDetailPage({ params }: TransactionDetailPageProps) {
  const { id } = use(params);
  const t = useTranslations("transactionDetail");
  const queryClient = useQueryClient();

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isSuspended = useAuthStore((s) => s.isSuspended);

  const detail = useTransactionDetail(id);
  const profile = useMyProfile(isAuthenticated);

  function handleRefetch() {
    queryClient.invalidateQueries({ queryKey: ["transactions", "detail", id] });
  }

  if (detail.isLoading) {
    return (
      <div className="mx-auto w-full max-w-5xl space-y-4 px-4 py-6">
        <Skeleton className="h-10 w-2/3" />
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-64 w-full" />
        <Skeleton className="h-32 w-full" />
      </div>
    );
  }

  if (detail.error instanceof ApiError) {
    if (detail.error.status === 404) {
      return (
        <ErrorState title={t("errors.notFound.title")} message={t("errors.notFound.message")} />
      );
    }
    if (detail.error.status === 403) {
      return (
        <ErrorState title={t("errors.forbidden.title")} message={t("errors.forbidden.message")} />
      );
    }
  }

  if (detail.isError || !detail.data) {
    return (
      <ErrorState
        title={t("errors.generic.title")}
        message={t("errors.generic.message")}
        onRetry={() => detail.refetch()}
      />
    );
  }

  const data = detail.data;
  const status = data.status;
  const role = data.userRole;
  const cancelled = isCancelledStatus(status);
  const emergencyHold = isEmergencyHold(status);
  const showPaymentInfo =
    role === "buyer" && data.payment && status === TransactionStatus.ITEM_ESCROWED;
  const showSellerPayout =
    role === "seller" && data.sellerPayout && status === TransactionStatus.COMPLETED;
  const showInviteInfo =
    role === "seller" && data.inviteInfo && status === TransactionStatus.CREATED;

  return (
    <div className="mx-auto w-full max-w-5xl space-y-4 px-4 py-6">
      {isSuspended && <SuspendedBanner />}

      <DetailHeader id={data.id} status={status} />

      {emergencyHold && <FlagHoldBanner holdInfo={data.holdInfo} />}
      {!emergencyHold && data.flagInfo && <FlagHoldBanner flagInfo={data.flagInfo} />}

      <TransactionTimeline
        status={
          status === "EMERGENCY_HOLD"
            ? TransactionStatus.ITEM_ESCROWED
            : (status as TransactionStatus)
        }
        cancelled={cancelled}
        flagged={status === TransactionStatus.FLAGGED}
      />

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

      {role && <PartiesPanel seller={data.seller} buyer={data.buyer} />}

      {showInviteInfo && data.inviteInfo && <InviteLinkBlock inviteInfo={data.inviteInfo} />}

      {data.paymentEvents && data.paymentEvents.length > 0 && (
        <PaymentEventBanners
          events={data.paymentEvents}
          stablecoin={data.stablecoin}
          cancelled={cancelled}
        />
      )}

      {showPaymentInfo && data.payment && (
        <PaymentInfoBlock payment={data.payment} timeout={data.timeout} />
      )}

      {showSellerPayout && data.sellerPayout && (
        <SellerPayoutSummary payout={data.sellerPayout} stablecoin={data.stablecoin} />
      )}

      {cancelled && data.cancelInfo && (
        <CancelInfoBlock
          cancelInfo={data.cancelInfo}
          refund={data.refund}
          stablecoin={data.stablecoin}
        />
      )}

      {data.dispute && <DisputeBlock dispute={data.dispute} />}

      <StateActionPanel
        detail={data}
        defaultRefundAddress={profile.data?.refundWalletAddress ?? null}
        isAuthenticated={isAuthenticated}
        isSuspended={isSuspended}
        onRefetch={handleRefetch}
      />
    </div>
  );
}
