"use client";

import { useEffect, useState } from "react";
import { cn } from "@/lib/utils/cn";

export interface InlineCountdownProps {
  /** Absolute expiry timestamp (epoch ms). */
  deadline: number;
  /** Rendered once the countdown reaches zero. */
  expiredLabel?: string;
  className?: string;
}

function remainingSeconds(deadline: number): number {
  return Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
}

/**
 * Lightweight mm:ss / Ns countdown that ticks once a second (WP13). Used for
 * the email verification code validity and resend-cooldown displays — distinct
 * from the transaction-oriented {@link "./CountdownTimer"} (timeout zones,
 * freeze states, day/hour spans).
 */
export function InlineCountdown({ deadline, expiredLabel, className }: InlineCountdownProps) {
  const [remaining, setRemaining] = useState(() => remainingSeconds(deadline));

  useEffect(() => {
    const tick = () => setRemaining(remainingSeconds(deadline));
    tick();
    const id = window.setInterval(tick, 1000);
    return () => window.clearInterval(id);
  }, [deadline]);

  if (remaining === 0 && expiredLabel) {
    return <span className={className}>{expiredLabel}</span>;
  }

  const minutes = Math.floor(remaining / 60);
  const seconds = remaining % 60;
  const text = minutes > 0 ? `${minutes}:${String(seconds).padStart(2, "0")}` : `${seconds}s`;

  return <span className={cn("tabular-nums", className)}>{text}</span>;
}
