import { defineConfig, devices } from '@playwright/test';
import { e2eConfig } from './src/config';

/**
 * The happy-path flow crosses three Hangfire-job-driven transitions (escrow
 * dispatch, delivery dispatch, payout pipeline), each on a ~minute cadence, so
 * the per-test timeout is generous. Single worker — the specs own shared DB
 * state and drive a global sequence. The API smoke uses plain fetch (no
 * browser); the UI smoke uses the chromium project.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 12 * 60 * 1000,
  expect: { timeout: 15_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: e2eConfig.baseUrl,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
