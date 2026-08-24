import { notFound } from "next/navigation";

/**
 * WP2b — the `/dev/*` component gallery is a development tool and must not be
 * reachable on a production deployment (owner decision 2026-08-24,
 * `dev-route-visibility`).
 *
 * The backlog row described this surface as "public-but-unindexed", but the
 * measurement said otherwise: the route had no guard of any kind and, before
 * WP2a, the project shipped no `robots` metadata at all — so the gallery was
 * both publicly reachable and indexable. It renders the real component
 * inventory, including the account-suspended / geo-block / sanctions state
 * screens, which is not something an outside visitor should be browsing.
 *
 * Gated in a server layout rather than inside the page because the page is a
 * client component: `notFound()` here answers 404 before any of that client
 * bundle renders. Development is unaffected — `next dev` and CI both run with
 * NODE_ENV != "production", so the gallery keeps working exactly as before.
 */
export default function DevLayout({ children }: { children: React.ReactNode }) {
  if (process.env.NODE_ENV === "production") notFound();
  // `children` is returned as-is rather than wrapped in a fragment: the gate is
  // the entire job of this layout, and the plain return keeps the module free of
  // JSX so the gate can be asserted directly under each NODE_ENV.
  return children;
}
