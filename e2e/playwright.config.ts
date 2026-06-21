import { defineConfig } from '@playwright/test';
import { e2eConfig } from './src/config';

/**
 * The happy-path flow crosses three Hangfire-job-driven transitions (escrow
 * dispatch, delivery dispatch, payout pipeline), each on a ~minute cadence, so
 * the per-test timeout is generous. Single worker — the smoke owns shared DB
 * state and drives a global sequence.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 10 * 60 * 1000,
  expect: { timeout: 15_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: e2eConfig.baseUrl,
    extraHTTPHeaders: { 'Content-Type': 'application/json' },
    trace: 'retain-on-failure',
  },
});
