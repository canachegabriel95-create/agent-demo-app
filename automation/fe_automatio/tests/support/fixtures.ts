import { test as base } from '@playwright/test';
import { createUser, deleteUsers, type NewUser, type TestUser } from './db';

type Fixtures = {
  /** Creates a user in the DB; every user created here is deleted when the test ends. */
  newUser: (user: NewUser) => Promise<TestUser>;
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
});

export { expect } from '@playwright/test';
