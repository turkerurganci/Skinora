import { AdminShell } from "@/components/layout";

// ToastProvider is mounted globally in `Providers` (WP9) so the realtime layer
// can raise toasts everywhere; admin components consume that same context.
export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return <AdminShell>{children}</AdminShell>;
}
