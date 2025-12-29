import { defineConfig, devices } from '@playwright/test';

/**
 * See https://playwright.dev/docs/test-configuration.
 */
export default defineConfig({
  testDir: './e2e',

  /* Timeout for each test */
  timeout: 540000, // 9 minutes per test (multiplayer games take time)

  /* Run tests in files in parallel */
  fullyParallel: false, // We want sequential execution for multiplayer coordination

  /* Fail the build on CI if you accidentally left test.only in the source code. */
  forbidOnly: !!process.env.CI,

  /* Retry on CI only */
  retries: process.env.CI ? 2 : 0,

  /* Single worker for multiplayer tests to ensure proper coordination */
  workers: 1,

  /* Reporter to use. See https://playwright.dev/docs/test-reporters */
  reporter: [
    ['html'],
    ['junit', { outputFile: 'test-results/junit.xml' }],
    ['list']
  ],

  /* Shared settings for all the projects below. See https://playwright.dev/docs/api/class-testoptions. */
  use: {
    /* Base URL to use in actions like `await page.goto('/')`. */
    baseURL: process.env.API_URL || 'https://localhost:7150',

    /* Collect trace when retrying the failed test. See https://playwright.dev/docs/trace-viewer */
    trace: 'on-first-retry',

    /* Screenshot on failure */
    screenshot: 'only-on-failure',

    /* Video on failure */
    video: 'retain-on-failure',

    /* Ignore HTTPS errors (for local development with self-signed certs) */
    ignoreHTTPSErrors: true,
  },

  /* Configure projects for different test types */
  projects: [
    {
      name: 'e2e-multiplayer',
      testMatch: /.*\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  /* Run your local dev server before starting the tests */
  // webServer: {
  //   command: 'dotnet run --project Server/ChefKnifeStudios.PokerAttack.Server.WebAPI',
  //   url: 'https://localhost:7150/health',
  //   timeout: 120 * 1000,
  //   reuseExistingServer: !process.env.CI,
  //   ignoreHTTPSErrors: true,
  // },
});
