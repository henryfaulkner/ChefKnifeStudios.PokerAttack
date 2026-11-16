import { defineConfig, devices } from '@playwright/test';
const { resolveReleaseTargets } = require('./release/safety.cjs');
const targets = resolveReleaseTargets();

// Separate from playwright.config.ts: no destructive setup and no captures that
// could contain verifier credentials, cookies or private hands.
export default defineConfig({
  testDir: './release',
  testMatch: '**/*.spec.ts',
  timeout: 780000,
  expect: { timeout: 30000 },
  workers: 1,
  fullyParallel: false,
  retries: 0,
  forbidOnly: true,
  reporter: [['list']],
  use: {
    ...devices['Desktop Chrome'],
    baseURL: targets.client,
    ignoreHTTPSErrors: false,
    trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 15000, navigationTimeout: 45000,
    serviceWorkers: 'block',
  },
});
