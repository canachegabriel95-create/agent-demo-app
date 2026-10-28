import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  globalSetup: './global-setup.ts',
  forbidOnly: true,
  reporter: 'list',
  projects: [
    {
      name: 'api',
      use: { baseURL: process.env.API_BASE_URL ?? 'http://localhost:5080' },
    },
  ],
});
