import { defineConfig, devices } from '@playwright/test';

/**
 * The tests expect the whole local stack to be running (PostgreSQL, the API on :5080, the Admin Portal on :5173 and
 * the Author Portal on :5174). Every URL and the administrator account can be overridden; see `tests/support/env.ts`.
 * They create their own author through the API, so they can run against a database with demo data in it.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    trace: 'retain-on-failure',
    ...devices['Desktop Chrome'],
    // Set PLAYWRIGHT_CHROMIUM_EXECUTABLE to use a browser that is already installed instead of `playwright install`.
    launchOptions: {
      executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined,
    },
  },
});
