import { randomUUID } from 'node:crypto';
import { apiAs, approve, createDraft, createSubmittedArticle, reject } from '../support/api';
import { apiLogin } from '../support/auth';
import { test, expect } from '../support/fixtures';

const rows = (page: import('@playwright/test').Page) => page.getByTestId(/^review-row-/);

async function authorApi(request: import('@playwright/test').APIRequestContext, author: Parameters<typeof apiLogin>[1]) {
  return apiAs(request, await apiLogin(request, author, 'Author'));
}

test('lists only pending articles, with title and author', async ({ page, request, admin, newUser }) => {
  const author = await newUser({ role: 'Author', fullName: `Queue Author ${randomUUID().slice(0, 6)}` });
  const api = await authorApi(request, author);
  const pending = await createSubmittedArticle(api, `Pending ${randomUUID()}`);
  const draft = await createDraft(api, `Draft ${randomUUID()}`);
  const published = await approve(admin.api, await createSubmittedArticle(api, `Published ${randomUUID()}`));
  const rejected = await reject(admin.api, await createSubmittedArticle(api, `Rejected ${randomUUID()}`));

  await page.goto('/reviews');

  const row = page.getByTestId(`review-row-${pending.id}`);
  await expect(row).toBeVisible();
  await expect(row).toContainText(author.fullName);
  for (const other of [draft, published, rejected]) {
    await expect(page.getByTestId(`review-row-${other.id}`)).toHaveCount(0);
  }
});

test('sorts by submission date, oldest first by default', async ({ page, request, admin, newUser }) => {
  void admin;
  const api = await authorApi(request, await newUser({ role: 'Author' }));
  const first = await createSubmittedArticle(api);
  const second = await createSubmittedArticle(api);
  await page.goto('/reviews');
  await expect(rows(page).first()).toHaveAttribute('data-testid', `review-row-${first.id}`);

  await page.getByTestId('review-sort-submitted').click();

  await expect(rows(page).first()).toHaveAttribute('data-testid', `review-row-${second.id}`);
});

test('paginates 20 articles per page', async ({ page, request, admin, newUser }) => {
  void admin;
  const api = await authorApi(request, await newUser({ role: 'Author' }));
  for (let i = 0; i < 21; i++) await createSubmittedArticle(api);

  await page.goto('/reviews');
  await expect(rows(page)).toHaveCount(20);

  await page.getByRole('button', { name: 'Next page' }).click();
  await expect(rows(page)).toHaveCount(1);
});

test('an approved or rejected article leaves the queue after a refresh', async ({ page, request, admin, newUser }) => {
  const api = await authorApi(request, await newUser({ role: 'Author' }));
  const toApprove = await createSubmittedArticle(api);
  const toReject = await createSubmittedArticle(api);
  await page.goto('/reviews');
  await expect(rows(page)).toHaveCount(2);

  await approve(admin.api, toApprove);
  await reject(admin.api, toReject);
  await page.getByTestId('review-refresh').click();

  await expect(page.getByText('No articles are waiting for review')).toBeVisible();
});

test('a row opens the review detail page', async ({ page, request, admin, newUser }) => {
  void admin;
  const api = await authorApi(request, await newUser({ role: 'Author' }));
  const article = await createSubmittedArticle(api);
  await page.goto('/reviews');

  await page.getByTestId(`review-row-${article.id}`).getByRole('link').first().click();

  await expect(page).toHaveURL(`/reviews/${article.id}`);
});

test('shows the empty state when the queue is empty', async ({ page, admin }) => {
  void admin;

  await page.goto('/reviews');

  await expect(page.getByText('No articles are waiting for review')).toBeVisible();
});

test('shows a loading state until the queue arrives', async ({ page, admin }) => {
  void admin;
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(/\/api\/admin\/reviews\/queue(\?.*)?$/, async (route) => {
    await gate;
    await route.continue();
  });

  await page.goto('/reviews');

  await expect(page.getByText('Loading the review queue…')).toBeVisible();
  release();
  await expect(page.getByText('Loading the review queue…')).toBeHidden();
});

test('shows an error state when the queue cannot be loaded', async ({ page, admin }) => {
  void admin;
  await page.route(/\/api\/admin\/reviews\/queue(\?.*)?$/, (route) => route.fulfill({ status: 500, json: { title: 'Server error' } }));

  await page.goto('/reviews');

  await expect(page.getByRole('alert')).toContainText('The review queue could not be loaded');
});
