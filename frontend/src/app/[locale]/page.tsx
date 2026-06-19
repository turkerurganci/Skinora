"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useLocale } from "next-intl";
import { Footer } from "@/components/layout/Footer";
import { HeroSection, HowItWorks, MaintenanceGate, TrustSignals } from "@/components/landing";
import { useAuthStore } from "@/lib/stores/auth-store";

export default function LandingPage() {
  // WP13 — once AuthInitializer has hydrated a stored session, send an
  // authenticated visitor straight to their dashboard instead of the
  // marketing landing page. Unauthenticated visitors keep `isAuthenticated`
  // false, so this is a no-op for them.
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const router = useRouter();
  const locale = useLocale();

  useEffect(() => {
    if (isAuthenticated) {
      router.replace(`/${locale}/dashboard`);
    }
  }, [isAuthenticated, router, locale]);

  if (isAuthenticated) return null;

  return (
    <div className="flex min-h-screen flex-col bg-white">
      <MaintenanceGate>
        {({ ctaDisabled }) => (
          <main className="flex-1">
            <HeroSection ctaDisabled={ctaDisabled} />
            <HowItWorks />
            <TrustSignals />
          </main>
        )}
      </MaintenanceGate>
      <Footer />
    </div>
  );
}
