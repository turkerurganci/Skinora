import { defineConfig, devices } from '@playwright/test';
import { e2eConfig } from './src/config';

/**
 * Single worker — the specs own shared DB state and drive a global sequence. The
 * API suites use plain fetch (no browser); the UI smoke uses the chromium project.
 *
 * T138 raised the per-test timeout from 12 to 20 minutes, and the reason is a
 * business rule rather than slow infrastructure. 02 §4.5.1 puts a settlement
 * window between ITEM_DELIVERED and COMPLETED, and `payout_settlement_days`
 * floors at 7 days — unshortenable by any setting (SystemSettingsValidator). A
 * test that wants to see COMPLETED therefore brings the eligibility clock forward
 * (DEPLOY_RUNBOOK §G.4 control 10a) and then waits out the REAL jobs behind it:
 * settlement-verification on a five-minute cron, then the payout queue, dispatch
 * and confirmation jobs on per-minute ones. Worst case that is ~8 minutes on top
 * of the flow itself, which does not fit inside 12.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 20 * 60 * 1000,
  expect: { timeout: 15_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: e2eConfig.baseUrl,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
