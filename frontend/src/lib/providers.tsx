"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { RealtimeProvider } from "@/lib/signalr/RealtimeProvider";
import { AuthInitializer } from "@/lib/auth/AuthInitializer";
import { TosRepromptGate } from "@/components/auth";
import { ToastProvider } from "@/components/common";

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30 * 1000,
            retry: 1,
            refetchOnWindowFocus: false,
          },
        },
      }),
  );

  return (
    <QueryClientProvider client={queryClient}>
      <AuthInitializer />
      {/* WP11 (T30) — re-prompts ToS re-acceptance on a version bump. Global so
          it covers every authenticated entry point; dormant otherwise. */}
      <TosRepromptGate />
      {/* ToastProvider wraps RealtimeProvider so the realtime layer can surface
          C09 toasts (WP9 — NewNotification). Hoisted from the admin layout to
          the global tree so a single toast stack serves every authenticated page. */}
      <ToastProvider>
        <RealtimeProvider>{children}</RealtimeProvider>
      </ToastProvider>
    </QueryClientProvider>
  );
}
