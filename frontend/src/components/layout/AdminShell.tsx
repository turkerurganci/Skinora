"use client";

import { useEffect, useState } from "react";
import { AdminHeader } from "./AdminHeader";
import { AdminSidebar } from "./AdminSidebar";

export interface AdminShellProps {
  children: React.ReactNode;
}

export function AdminShell({ children }: AdminShellProps) {
  const [isDrawerOpen, setDrawerOpen] = useState(false);

  useEffect(() => {
    if (!isDrawerOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setDrawerOpen(false);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [isDrawerOpen]);

  return (
    <div className="flex min-h-screen flex-col bg-gray-50">
      <AdminHeader onMenuClick={() => setDrawerOpen(true)} />
      <div className="flex flex-1">
        <AdminSidebar isDrawerOpen={isDrawerOpen} onCloseDrawer={() => setDrawerOpen(false)} />
        <main className="min-w-0 flex-1 p-4">{children}</main>
      </div>
    </div>
  );
}
