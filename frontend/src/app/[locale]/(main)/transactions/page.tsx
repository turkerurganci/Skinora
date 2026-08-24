import { redirect } from "@/i18n/navigation";

/**
 * F6 / `UITour-TransactionsListPageIsStub` — this route shipped in `c01e790`
 * ("T13: Next.js Frontend iskeleti") as a bare `<div>Transactions</div>` and
 * was never touched again; no navigation links here, so only a hand-typed URL
 * or a back-navigation from `/transactions/{id}` reaches it.
 *
 * It redirects instead of rendering a list, because the transaction list
 * already lives on the dashboard (`TransactionList` + `TransactionTabs` +
 * the URL-synced tab/page state from WP13). Rendering a second copy here
 * would fork that state into two pages that must be kept in sync — the
 * duplicate-source-of-truth defect family this project has been bitten by
 * before. Deleting the file instead was rejected: the parent path of two live
 * child routes would 404 into the untranslated global `not-found.tsx`.
 */
export default async function TransactionsPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  redirect({ href: "/dashboard", locale });
}
