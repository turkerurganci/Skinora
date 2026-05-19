"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { TimeoutFreezeReason } from "@/types/enums";
import { cn } from "@/lib/utils/cn";

export interface CountdownTimerProps {
  deadline: Date | string;
  warningThresholdSeconds: number;
  frozen?: boolean;
  frozenReason?: TimeoutFreezeReason;
  format?: "verbose" | "clock";
  className?: string;
}

interface RemainingTime {
  totalSeconds: number;
  days: number;
  hours: number;
  minutes: number;
  seconds: number;
  expired: boolean;
}

function compute(deadline: Date): RemainingTime {
  const diffMs = deadline.getTime() - Date.now();
  const totalSeconds = Math.max(0, Math.floor(diffMs / 1000));
  return {
    totalSeconds,
    days: Math.floor(totalSeconds / 86400),
    hours: Math.floor((totalSeconds % 86400) / 3600),
    minutes: Math.floor((totalSeconds % 3600) / 60),
    seconds: totalSeconds % 60,
    expired: totalSeconds === 0,
  };
}

function classify(
  remainingSeconds: number,
  warningThresholdSeconds: number,
): "green" | "yellow" | "red" {
  if (remainingSeconds <= warningThresholdSeconds) return "red";
  if (remainingSeconds <= warningThresholdSeconds * 2) return "yellow";
  return "green";
}

const ZONE_STYLE: Record<"green" | "yellow" | "red", string> = {
  green: "text-green-700",
  yellow: "text-yellow-700",
  red: "text-red-700 animate-pulse",
};

export function CountdownTimer({
  deadline,
  warningThresholdSeconds,
  frozen,
  frozenReason,
  format = "verbose",
  className,
}: CountdownTimerProps) {
  const t = useTranslations("countdown");
  const deadlineDate = useMemo(
    () => (typeof deadline === "string" ? new Date(deadline) : deadline),
    [deadline],
  );
  const [remaining, setRemaining] = useState<RemainingTime>(() => compute(deadlineDate));

  useEffect(() => {
    if (frozen) return;
    const tick = () => setRemaining(compute(deadlineDate));
    tick();
    const interval = window.setInterval(tick, 1000);
    return () => window.clearInterval(interval);
  }, [deadlineDate, frozen]);

  if (frozen) {
    return (
      <span
        className={cn(
          "inline-flex items-center gap-1 rounded-md bg-gray-100 px-2 py-1 text-xs font-medium text-gray-700",
          className,
        )}
        role="status"
      >
        <svg className="h-3.5 w-3.5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
          <path
            fillRule="evenodd"
            d="M5 4a1 1 0 011 1v10a1 1 0 11-2 0V5a1 1 0 011-1zm10 0a1 1 0 011 1v10a1 1 0 11-2 0V5a1 1 0 011-1z"
            clipRule="evenodd"
          />
        </svg>
        {t("frozen")}
        {frozenReason && (
          <span className="text-gray-500">({t(`freezeReason.${frozenReason}`)})</span>
        )}
      </span>
    );
  }

  const zone = classify(remaining.totalSeconds, warningThresholdSeconds);
  const text = format === "clock" ? formatClock(remaining) : formatVerbose(remaining, t);

  return (
    <span
      className={cn(
        "inline-flex flex-col items-start gap-0.5 font-mono text-sm tabular-nums",
        ZONE_STYLE[zone],
        className,
      )}
      role="timer"
      aria-live={zone === "red" ? "assertive" : "off"}
    >
      <span>{remaining.expired ? t("expired") : text}</span>
      {zone === "red" && !remaining.expired && (
        <span className="text-xs font-normal">{t("warning")}</span>
      )}
    </span>
  );
}

function formatClock(r: RemainingTime): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  if (r.days > 0) {
    return `${r.days}d ${pad(r.hours)}:${pad(r.minutes)}:${pad(r.seconds)}`;
  }
  return `${pad(r.hours)}:${pad(r.minutes)}:${pad(r.seconds)}`;
}

function formatVerbose(
  r: RemainingTime,
  t: (key: string, values?: Record<string, number>) => string,
): string {
  if (r.days > 0) {
    return t("verboseDays", { days: r.days, hours: r.hours });
  }
  if (r.hours > 0) {
    return t("verboseHours", { hours: r.hours, minutes: r.minutes });
  }
  return t("verboseMinutes", { minutes: r.minutes, seconds: r.seconds });
}
