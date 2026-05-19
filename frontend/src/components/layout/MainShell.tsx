"use client";

import type { ReactNode } from "react";
import { useAuthStore } from "@/lib/stores/auth-store";
import { Header } from "./Header";
import { SuspendedHeader } from "./SuspendedHeader";

export function MainShell({ children }: { children: ReactNode }) {
  const isSuspended = useAuthStore((s) => s.isSuspended);

  return (
    <div className="flex min-h-screen flex-col bg-gray-50">
      {isSuspended ? <SuspendedHeader /> : <Header />}
      <main className="flex-1">{children}</main>
    </div>
  );
}
