import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  globalSetup: './global-setup.ts',
  forbidOnly: true,
  // The admin dashboard counters are global, so spec files must not run side by side.
  workers: 1,
  reporter: 'list',
  projects: [
    {
      name: 'api',
      use: { baseURL: process.env.API_BASE_URL ?? 'http://localhost:5080' },
    },
  ],
});
