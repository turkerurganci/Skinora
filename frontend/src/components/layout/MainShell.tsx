"use client";

import type { ReactNode } from "react";
import { useAuthStore } from "@/lib/stores/auth-store";
import { useUnreadCount } from "@/lib/hooks/useUnreadCount";
import { Header } from "./Header";
import { SuspendedHeader } from "./SuspendedHeader";

export function MainShell({ children }: { children: ReactNode }) {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const isSuspended = useAuthStore((s) => s.isSuspended);

  // Suspended sessions hide the notifications nav entry entirely
  // (04 §10.2 Suspended Header), so we only poll when the regular Header
  // would be rendered. The hook is auth-gated as well — keeps the badge
  // silent for unauthenticated visitors.
  const unread = useUnreadCount(isAuthenticated && !isSuspended);

  return (
    <div className="flex min-h-screen flex-col bg-gray-50">
      {isSuspended ? (
        <SuspendedHeader />
      ) : (
        <Header unreadNotifications={unread.data?.unreadCount ?? 0} />
      )}
      <main className="flex-1">{children}</main>
    </div>
  );
}
