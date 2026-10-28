import { test, expect } from '../../baseTest/baseTest';
import { AuthorsPage } from '../../pageObjects/admin/AuthorsPage';
import { newAuthors } from '../../testData/authors';

let authors: ReturnType<typeof newAuthors>;

test.beforeEach(async ({ db }) => {
  authors = newAuthors();
  for (const author of [authors.active, authors.suspended]) {
    await db.runQuery('insertAuthor', [author.fullName, author.email, author.status]);
  }
});

test.afterEach(async ({ db }) => {
  await db.runQuery('deleteUsersByEmail', [[authors.active.email, authors.suspended.email]]);
});

test.describe('Admin Portal - Authors list', () => {
  test('shows the authors table with data', async ({ adminPage }) => {
    const authorsPage = new AuthorsPage(adminPage);
    await authorsPage.open();

    await expect(authorsPage.row(authors.active.email)).toBeVisible();
  });

  test('search by name shows only the matching author', async ({ adminPage }) => {
    const authorsPage = new AuthorsPage(adminPage);
    await authorsPage.open();

    await authorsPage.search(authors.active.fullName);

    await expect(authorsPage.emailCells).toHaveText([authors.active.email]);
  });

  test('search by email shows only the matching author', async ({ adminPage }) => {
    const authorsPage = new AuthorsPage(adminPage);
    await authorsPage.open();

    await authorsPage.search(authors.suspended.email);

    await expect(authorsPage.emailCells).toHaveText([authors.suspended.email]);
  });

  test('status filter shows only authors with that status', async ({ adminPage }) => {
    const authorsPage = new AuthorsPage(adminPage);
    await authorsPage.open();
    await authorsPage.search(authors.tag);

    await authorsPage.filterByStatus('Suspended');

    await expect(authorsPage.emailCells).toHaveText([authors.suspended.email]);
  });
});
