import { AdminShell } from "@/components/layout";
import { ToastProvider } from "@/components/common";

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return (
    <ToastProvider>
      <AdminShell>{children}</AdminShell>
    </ToastProvider>
  );
}
