import { apiAs, approve, createDraft, createSubmittedArticle } from '../support/api';
import { apiLogin } from '../support/auth';
import { test, expect } from '../support/fixtures';

const rows = (page: import('@playwright/test').Page) => page.getByTestId('article-row');

test('lists only my articles, each with its status', async ({ page, author, adminApi, newUser, request }) => {
  const other = apiAs(request, await apiLogin(request, await newUser({ role: 'Author' }), 'Author'));
  await createDraft(author.api, 'My draft article');
  await createSubmittedArticle(author.api, 'My pending article');
  await approve(adminApi, await createSubmittedArticle(author.api, 'My published article'));
  await createDraft(other, 'Somebody else article');

  await page.goto('/articles');

  await expect(rows(page).filter({ hasText: 'My draft article' }).getByTestId('article-row-status')).toHaveText('Draft');
  await expect(rows(page).filter({ hasText: 'My pending article' }).getByTestId('article-row-status')).toContainText('Pending');
  await expect(rows(page).filter({ hasText: 'My published article' }).getByTestId('article-row-status')).toHaveText('Published');
  await expect(rows(page).filter({ hasText: 'Somebody else article' })).toHaveCount(0);
});

test('filters by status', async ({ page, author }) => {
  await createDraft(author.api, 'Filter draft');
  await createSubmittedArticle(author.api, 'Filter pending');
  await page.goto('/articles');

  await page.getByLabel('Status').selectOption('PendingReview');

  await expect(rows(page).filter({ hasText: 'Filter pending' })).toHaveCount(1);
  await expect(rows(page).filter({ hasText: 'Filter draft' })).toHaveCount(0);
});

test('paginates 20 articles per page', async ({ page, author }) => {
  for (let i = 0; i < 21; i++) await createDraft(author.api);

  await page.goto('/articles');
  await expect(rows(page)).toHaveCount(20);

  await page.getByRole('button', { name: 'Next page' }).click();
  await expect(rows(page)).toHaveCount(1);
});

test('a row opens the article and "New article" opens the editor', async ({ page, author }) => {
  const draft = await createDraft(author.api, 'Open me');
  await page.goto('/articles');

  await rows(page).filter({ hasText: 'Open me' }).getByRole('link', { name: 'Open me', exact: true }).click();
  await expect(page).toHaveURL(`/articles/${draft.id}`);

  await page.goto('/articles');
  await page.getByTestId('articles-new').click();
  await expect(page).toHaveURL('/articles/new');
});

test("another author's article is not found when opened by its address", async ({ page, author, newUser, request }) => {
  void author;
  const other = apiAs(request, await apiLogin(request, await newUser({ role: 'Author' }), 'Author'));
  const theirs = await createDraft(other, 'Not yours');

  await page.goto(`/articles/${theirs.id}`);

  await expect(page.getByRole('heading', { name: 'Article not found' })).toBeVisible();
});

test('shows the empty state when I have no articles', async ({ page, author }) => {
  void author;

  await page.goto('/articles');

  await expect(page.getByTestId('articles-empty-new')).toBeVisible();
});

test('shows a loading state until the articles arrive', async ({ page, author }) => {
  void author;
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(/\/api\/me\/articles(\?.*)?$/, async (route) => {
    await gate;
    await route.continue();
  });

  await page.goto('/articles');

  await expect(page.getByText('Loading your articles…')).toBeVisible();
  release();
  await expect(page.getByText('Loading your articles…')).toBeHidden();
});

test('shows an error state when the articles cannot be loaded', async ({ page, author }) => {
  void author;
  await page.route(/\/api\/me\/articles(\?.*)?$/, (route) => route.fulfill({ status: 500, json: { title: 'Server error' } }));

  await page.goto('/articles');

  await expect(page.getByRole('alert')).toContainText('Your articles could not be loaded');
});
