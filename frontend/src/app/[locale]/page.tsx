"use client";

import { Footer } from "@/components/layout/Footer";
import { HeroSection, HowItWorks, MaintenanceGate, TrustSignals } from "@/components/landing";

export default function LandingPage() {
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
