"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { RealtimeProvider } from "@/lib/signalr/RealtimeProvider";
import { AuthInitializer } from "@/lib/auth/AuthInitializer";

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
      <RealtimeProvider>{children}</RealtimeProvider>
    </QueryClientProvider>
  );
}
