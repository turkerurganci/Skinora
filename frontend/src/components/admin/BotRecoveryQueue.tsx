"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import type { UpdateBotRecoveryRequest } from "@/lib/api/admin";
import { useBotRecoveryQueue, useUpdateBotRecovery } from "@/lib/hooks/useAdminSteamAccounts";
import { RecoveryQueuePanel } from "./RecoveryQueuePanel";

export interface BotRecoveryQueueProps {
  botId: string;
  botName: string;
  className?: string;
}

/**
 * Data wrapper for one bot's S18 recovery queue (AD25/AD26, T103b-2). Fetches
 * the queue, owns the triage mutation, and hands the rows + an update callback to
 * {@link RecoveryQueuePanel}. Rendered once per restricted/banned bot.
 */
export function BotRecoveryQueue({ botId, botName, className }: BotRecoveryQueueProps) {
  const t = useTranslations("adminSteamAccounts.recovery");
  const { data, isLoading, isError } = useBotRecoveryQueue(botId, true);
  const mutation = useUpdateBotRecovery(botId);

  function handleUpdate(id: string, body: UpdateBotRecoveryRequest) {
    mutation.mutate({ id, body });
  }

  if (isLoading || isError) {
    return (
      <section
        className={cn("rounded-lg border border-gray-200 bg-white p-4 shadow-sm", className)}
      >
        <h3 className="text-sm font-semibold text-gray-900">{t("titleFor", { bot: botName })}</h3>
        <p className={cn("mt-2 text-xs", isError ? "text-red-600" : "text-gray-500")}>
          {isError ? t("loadError") : t("loading")}
        </p>
      </section>
    );
  }

  return (
    <RecoveryQueuePanel
      className={className}
      botName={botName}
      items={data?.items ?? []}
      onUpdate={handleUpdate}
      pendingId={mutation.isPending ? (mutation.variables?.id ?? null) : null}
    />
  );
}
