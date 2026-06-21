import type { BrowserContext, Page } from '@playwright/test';

/**
 * Inject a minted JWT into localStorage so the frontend's AuthInitializer
 * hydrates the session (setAccessToken → isAuthenticated=true → /auth/me) —
 * bypassing Steam OAuth, which cannot be scripted. Must run BEFORE the first
 * navigation (addInitScript runs on every document creation).
 */
export async function injectLogin(context: BrowserContext, token: string): Promise<void> {
  await context.addInitScript((t) => {
    window.localStorage.setItem('access_token', t);
  }, token);
}

/**
 * Wait until the detail page's status badge shows `target`, reloading on an
 * interval. Reload-based polling is robust regardless of SignalR realtime
 * delivery — each load refetches the detail query. Throws on a CANCELLED_* /
 * FLAGGED terminal or timeout.
 */
export async function waitForUiStatus(
  page: Page,
  target: string,
  opts?: { timeoutMs?: number; intervalMs?: number },
): Promise<void> {
  const deadline = Date.now() + (opts?.timeoutMs ?? 240_000);
  const interval = opts?.intervalMs ?? 5_000;
  let last: string | null = null;
  while (Date.now() < deadline) {
    last = await page
      .getByTestId('tx-status-badge')
      .getAttribute('data-status')
      .catch(() => null);
    if (last === target) return;
    if (last && (last.startsWith('CANCELLED') || last === 'FLAGGED')) {
      throw new Error(`UI badge reached terminal ${last} while awaiting ${target}`);
    }
    await page.waitForTimeout(interval);
    await page.reload({ waitUntil: 'domcontentloaded' });
  }
  throw new Error(`timeout awaiting UI status ${target} (last=${last})`);
}
