import type { Metadata } from "next";
import Link from "next/link";
import { LanguageSelector } from "@/components/common";

/**
 * WP2a — keep every `/auth/*` screen out of search results.
 *
 * The trigger was `UITour-StateScreensReachableAnonymously`: `/auth/suspended`,
 * `/auth/geo-block` and `/auth/sanctions` render fully for an anonymous visitor
 * (measured in the 2026-08-23 UI tour, all three return 200). Anonymous access
 * is a deliberate product decision and stays — these are informational screens
 * and the people who need them are, by definition, not logged in. What was not
 * decided is that a crawler could index them: "Skinora — your account has been
 * suspended" is not a page anyone should reach from a search engine.
 *
 * Applied at the layout instead of on the three pages because the pages are
 * client components (they cannot export `metadata`) and because nothing under
 * `/auth` benefits from indexing — login, the OpenID callback, the age gate and
 * the mobile-authenticator screen are all transient, session-scoped surfaces.
 * Narrowing this back to the three state screens would mean three near-identical
 * layout files and would leave the login page indexable for no gain.
 */
export const metadata: Metadata = {
  robots: { index: false, follow: false },
};

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col bg-gray-50">
      <header className="flex items-center justify-between border-b border-gray-200 bg-white px-4 py-3">
        <Link href="/" className="text-lg font-semibold text-gray-900" aria-label="Skinora">
          Skinora
        </Link>
        <LanguageSelector />
      </header>
      <main className="flex flex-1 items-center justify-center px-4 py-10">{children}</main>
    </div>
  );
}
