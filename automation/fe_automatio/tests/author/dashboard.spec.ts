import { apiAs, approve, createDraft, createSubmittedArticle, reject } from '../support/api';
import { apiLogin } from '../support/auth';
import { test, expect } from '../support/fixtures';

test('the counts per status match the articles created through the API', async ({ page, author, adminApi }) => {
  await createDraft(author.api);
  await createDraft(author.api);
  await createSubmittedArticle(author.api);
  await approve(adminApi, await createSubmittedArticle(author.api));
  await reject(adminApi, await createSubmittedArticle(author.api));

  await page.goto('/');

  await expect(page.getByTestId('stat-draft')).toContainText('2');
  await expect(page.getByTestId('stat-pending-review')).toContainText('1');
  await expect(page.getByTestId('stat-published')).toContainText('1');
  await expect(page.getByTestId('stat-rejected')).toContainText('1');
  await expect(page.getByTestId('attention-message')).toHaveText('1 article needs your attention');
});

test("another author's articles are not counted", async ({ page, author, newUser, request }) => {
  const other = apiAs(request, await apiLogin(request, await newUser({ role: 'Author' }), 'Author'));
  await createDraft(other);
  await createDraft(other);
  await createDraft(author.api);

  await page.goto('/');

  await expect(page.getByTestId('stat-draft')).toContainText('1');
});

const cards = [
  { testId: 'stat-draft', url: '/articles?status=Draft' },
  { testId: 'stat-pending-review', url: '/articles?status=PendingReview' },
  { testId: 'stat-published', url: '/articles?status=Published' },
  { testId: 'stat-rejected', url: '/articles?status=Rejected' },
];

for (const { testId, url } of cards) {
  test(`the ${testId} card opens ${url}`, async ({ page, author }) => {
    void author;
    await page.goto('/');

    await page.getByTestId(testId).click();

    await expect(page).toHaveURL(url);
  });
}

test('a new author sees zero counts and no attention message', async ({ page, author }) => {
  void author;

  await page.goto('/');

  for (const testId of ['stat-draft', 'stat-pending-review', 'stat-published', 'stat-rejected']) {
    await expect(page.getByTestId(testId)).toContainText('0');
  }
  await expect(page.getByTestId('attention-message')).toHaveCount(0);
});

test('shows a loading state until the counts arrive', async ({ page, author }) => {
  void author;
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/me/articles/stats', async (route) => {
    await gate;
    await route.continue();
  });

  await page.goto('/');

  await expect(page.getByTestId('stats-loading')).toBeVisible();
  release();
  await expect(page.getByTestId('stat-draft')).toBeVisible();
});

test('shows an error state when the counts cannot be loaded', async ({ page, author }) => {
  void author;
  await page.route('**/api/me/articles/stats', (route) => route.fulfill({ status: 500, json: { title: 'Server error' } }));

  await page.goto('/');

  await expect(page.getByRole('alert')).toBeVisible();
});
