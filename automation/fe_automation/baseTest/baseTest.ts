import { test as base, type Page } from '@playwright/test';
import { closeDb, runQuery } from '../db/db';
import { login } from './login';

type Fixtures = {
  adminPage: Page;
  authorPage: Page;
};

type WorkerFixtures = {
  db: { runQuery: typeof runQuery };
};

export const test = base.extend<Fixtures, WorkerFixtures>({
  // A page already signed in to the Admin Portal.
  adminPage: async ({ page }, use) => {
    await login(page, 'admin');
    await use(page);
  },

  // A page already signed in to the Author Portal.
  authorPage: async ({ page }, use) => {
    await login(page, 'author');
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
