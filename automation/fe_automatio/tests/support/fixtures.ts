import { test as base } from '@playwright/test';
import { apiAs, type Api } from './api';
import { ADMIN_SESSION_KEY, AUTHOR_SESSION_KEY, apiLogin, startSignedIn } from './auth';
import { createUser, deleteUsers, type NewUser, type TestUser } from './db';

export interface Session {
  user: TestUser;
  token: string;
  api: Api;
}

type Fixtures = {
  /** Creates a user in the DB; every user created here is deleted when the test ends. */
  newUser: (user: NewUser) => Promise<TestUser>;
  /** A new administrator, signed in: the page starts with the session in place. */
  admin: Session;
  /** A new author, signed in: the page starts with the session in place. */
  author: Session;
};

export const test = base.extend<Fixtures>({
  newUser: async ({}, use) => {
    const created: string[] = [];
    await use(async (user) => {
      const made = await createUser(user);
      created.push(made.id);
      return made;
    });
    await deleteUsers(created);
  },
  admin: async ({ page, request, newUser }, use) => {
    const user = await newUser({ role: 'Administrator' });
    const token = await apiLogin(request, user, 'Admin');
    await startSignedIn(page, ADMIN_SESSION_KEY, token);
    await use({ user, token, api: apiAs(request, token) });
  },
  author: async ({ page, request, newUser }, use) => {
    const user = await newUser({ role: 'Author' });
    const token = await apiLogin(request, user, 'Author');
    await startSignedIn(page, AUTHOR_SESSION_KEY, token);
    await use({ user, token, api: apiAs(request, token) });
  },
});

export { expect } from '@playwright/test';
