import { AdminShell } from "@/components/layout";
import { AdminGuard } from "@/lib/auth/AdminGuard";

// ToastProvider is mounted globally in `Providers` (WP9) so the realtime layer
// can raise toasts everywhere; admin components consume that same context.
// AdminGuard (WP13) bounces non-admins client-side before the shell mounts;
// backend authorization remains the authoritative check on every endpoint.
export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return (
    <AdminGuard>
      <AdminShell>{children}</AdminShell>
    </AdminGuard>
  );
}
