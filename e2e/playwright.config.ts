import { defineConfig } from '@playwright/test';

/**
 * The tests drive a running local stack (see the repository README): the API on 5080, the Admin Portal on 5173 and
 * the Author Portal on 5174, with an administrator already provisioned (db/scripts/provision-admin.sql).
 * Credentials come from the environment, never from the repository:
 *   E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD   the provisioned administrator
 *   E2E_API_URL, E2E_ADMIN_PORTAL_URL, E2E_AUTHOR_PORTAL_URL   optional, default to the local ports
 */
export default defineConfig({
  testDir: './tests',
  // Tests create their own authors and articles with unique emails, but a single worker keeps the output readable.
  workers: 1,
  fullyParallel: false,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list']],
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});
