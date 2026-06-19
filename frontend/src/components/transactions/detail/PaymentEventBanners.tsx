import { useTranslations } from "next-intl";
import type { TransactionDetailPaymentEvent } from "@/lib/api/transactions";
import { cn } from "@/lib/utils/cn";
import { TxHashLink } from "./TxHashLink";

export interface PaymentEventBannersProps {
  events: TransactionDetailPaymentEvent[];
  stablecoin: string;
  cancelled?: boolean;
}

/**
 * 04 §7.3 — Ödeme edge case banner'ları (4 type).
 * - INCORRECT_AMOUNT: red warning (eksik tutar iade)
 * - EXCESS_AMOUNT: blue info (fazla tutar iade, işlem devam)
 * - WRONG_TOKEN: red warning (yanlış token iade)
 * - LATE_PAYMENT: blue info (cancelled state'te gösterilir — spec)
 *
 * LATE_PAYMENT, `cancelled` true değilse gösterilmez (06 + 07 spec: bu
 * event sadece CANCELLED state'inde aktif anlamlıdır; aksi durumda hep
 * cancelled olur ve burada tekrar render edilir).
 */
export function PaymentEventBanners({ events, stablecoin, cancelled }: PaymentEventBannersProps) {
  const t = useTranslations("transactionDetail.paymentEvents");
  if (!events || events.length === 0) return null;

  return (
    <div className="space-y-2">
      {events.map((event, idx) => {
        const variant = bannerVariant(event.type);
        const showLatePayment = event.type !== "LATE_PAYMENT" || cancelled;
        if (!showLatePayment) return null;
        return (
          <div
            key={`${event.type}-${event.occurredAt}-${idx}`}
            className={cn(
              "rounded-md border p-3 text-sm",
              variant === "warning"
                ? "border-red-300 bg-red-50 text-red-900"
                : "border-blue-300 bg-blue-50 text-blue-900",
            )}
            role={variant === "warning" ? "alert" : "status"}
          >
            <p className="font-medium">{t(`${event.type}.title`)}</p>
            <p>{messageFor(event, stablecoin, t)}</p>
            {event.refundTxHash && (
              <p className="mt-1 flex items-center gap-1 font-mono text-xs">
                <span>{t("refundTxHashLabel")}:</span>
                <TxHashLink txHash={event.refundTxHash} />
              </p>
            )}
          </div>
        );
      })}
    </div>
  );
}

type Variant = "warning" | "info";

function bannerVariant(type: string): Variant {
  return type === "INCORRECT_AMOUNT" || type === "WRONG_TOKEN" ? "warning" : "info";
}

function messageFor(
  event: TransactionDetailPaymentEvent,
  stablecoin: string,
  t: (key: string, values?: Record<string, string | number>) => string,
): string {
  switch (event.type) {
    case "INCORRECT_AMOUNT":
      return t("INCORRECT_AMOUNT.body", {
        received: event.receivedAmount ?? "",
        expected: event.expectedAmount ?? "",
        token: stablecoin,
      });
    case "EXCESS_AMOUNT":
      return t("EXCESS_AMOUNT.body", {
        received: event.receivedAmount ?? "",
        expected: event.expectedAmount ?? "",
        token: stablecoin,
      });
    case "WRONG_TOKEN":
      return t("WRONG_TOKEN.body", { token: stablecoin });
    case "LATE_PAYMENT":
      return t("LATE_PAYMENT.body");
    default:
      return t("unknown.body");
  }
}
