import { test as base, type Page } from '@playwright/test';
import { closeDb, runQuery } from '../db/db';
import { useSession } from './login';

type Fixtures = {
  adminPage: Page;
  authorPage: Page;
};

type WorkerFixtures = {
  db: { runQuery: typeof runQuery };
};

export const test = base.extend<Fixtures, WorkerFixtures>({
  // A page already signed in to the Admin Portal (token from globalSetup.ts).
  adminPage: async ({ page }, use) => {
    await useSession(page, 'admin', process.env.ADMIN_SESSION_TOKEN!);
    await use(page);
  },

  // A page already signed in to the Author Portal (token from globalSetup.ts).
  authorPage: async ({ page }, use) => {
    await useSession(page, 'author', process.env.AUTHOR_SESSION_TOKEN!);
    await use(page);
  },

  // One connection pool per worker, closed when the worker ends.
  db: [
    async ({}, use) => {
      await use({ runQuery });
      await closeDb();
    },
    { scope: 'worker' },
  ],
});

export { expect } from '@playwright/test';
