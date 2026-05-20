"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/utils/cn";

export type InfoScreenTone = "info" | "warning" | "danger" | "success";

export interface InfoScreenProps {
  tone?: InfoScreenTone;
  icon?: ReactNode;
  title: string;
  description?: ReactNode;
  children?: ReactNode;
  actions?: ReactNode;
  className?: string;
}

const TONE_STYLES: Record<InfoScreenTone, { ring: string; iconWrap: string }> = {
  info: { ring: "ring-blue-100", iconWrap: "bg-blue-50 text-blue-600" },
  warning: { ring: "ring-amber-100", iconWrap: "bg-amber-50 text-amber-600" },
  danger: { ring: "ring-red-100", iconWrap: "bg-red-50 text-red-600" },
  success: { ring: "ring-green-100", iconWrap: "bg-green-50 text-green-600" },
};

export function InfoScreen({
  tone = "info",
  icon,
  title,
  description,
  children,
  actions,
  className,
}: InfoScreenProps) {
  const palette = TONE_STYLES[tone];

  return (
    <section
      role="region"
      aria-labelledby="auth-info-title"
      className={cn(
        "mx-auto w-full max-w-md rounded-xl bg-white p-6 shadow-sm ring-1",
        palette.ring,
        className,
      )}
    >
      {icon && (
        <div
          aria-hidden="true"
          className={cn(
            "mb-4 inline-flex h-12 w-12 items-center justify-center rounded-full text-2xl",
            palette.iconWrap,
          )}
        >
          {icon}
        </div>
      )}
      <h1 id="auth-info-title" className="text-xl font-semibold text-gray-900">
        {title}
      </h1>
      {description && (
        <div className="mt-2 text-sm text-gray-600">
          {typeof description === "string" ? <p>{description}</p> : description}
        </div>
      )}
      {children && <div className="mt-4 text-sm text-gray-700">{children}</div>}
      {actions && (
        <div className="mt-6 flex flex-col gap-2 sm:flex-row sm:items-center">{actions}</div>
      )}
    </section>
  );
}
