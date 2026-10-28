import { defineConfig } from '@playwright/test';

const adminUrl = process.env.ADMIN_URL ?? 'http://localhost:5173';
const authorUrl = process.env.AUTHOR_URL ?? 'http://localhost:5174';

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    { name: 'admin', testMatch: 'admin/**/*.spec.ts', use: { baseURL: adminUrl } },
    { name: 'author', testMatch: 'author/**/*.spec.ts', use: { baseURL: authorUrl } },
  ],
  // Starts the portals when they are not already running (the API is not needed for the smoke specs).
  webServer: [
    {
      command: 'npm --prefix ../../frontend/admin-portal run dev',
      url: adminUrl,
      reuseExistingServer: true,
    },
    {
      command: 'npm --prefix ../../frontend/author-portal run dev',
      url: authorUrl,
      reuseExistingServer: true,
    },
  ],
});
