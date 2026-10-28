import { randomUUID } from 'node:crypto';
import { apiAs, approve, createInvitation, createSubmittedArticle, ok } from '../support/api';
import { apiLogin } from '../support/auth';
import { test, expect } from '../support/fixtures';

interface Dashboard {
  activeAuthors: number;
  suspendedAuthors: number;
  availableInvitations: number;
  pendingReviews: number;
  publishedArticles: number;
}

test('counters match the data created through the API', async ({ page, request, admin, newUser }) => {
  const before = await ok<Dashboard>(await admin.api.get('/api/admin/dashboard'));
  const author = await newUser({ role: 'Author' });
  await newUser({ role: 'Author', status: 'Suspended' });
  await createInvitation(admin.api, `e2e-${randomUUID()}@example.test`);
  const authorApi = apiAs(request, await apiLogin(request, author, 'Author'));
  await createSubmittedArticle(authorApi);
  await approve(admin.api, await createSubmittedArticle(authorApi));

  await page.goto('/');

  const expected = {
    'dash-active-authors': before.activeAuthors + 1,
    'dash-suspended-authors': before.suspendedAuthors + 1,
    'dash-available-invitations': before.availableInvitations + 1,
    'dash-pending-reviews': before.pendingReviews + 1,
    'dash-published-articles': before.publishedArticles + 1,
  };
  for (const [testId, count] of Object.entries(expected)) {
    await expect(page.getByTestId(testId).getByText(String(count), { exact: true })).toBeVisible();
  }
});

test('shows a loading state until the numbers arrive', async ({ page, admin }) => {
  void admin;
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/admin/dashboard', async (route) => {
    await gate;
    await route.continue();
  });

  await page.goto('/');

  await expect(page.getByRole('list', { name: 'Loading the dashboard' })).toBeVisible();
  release();
  await expect(page.getByTestId('dash-active-authors')).toBeVisible();
});

test('shows an error state when the dashboard cannot be loaded', async ({ page, admin }) => {
  void admin;
  await page.route('**/api/admin/dashboard', (route) => route.fulfill({ status: 500, json: { title: 'Server error' } }));

  await page.goto('/');

  await expect(page.getByRole('alert')).toContainText('The dashboard could not be loaded');
});

const cards = [
  { testId: 'dash-active-authors', url: '/users?status=Active' },
  { testId: 'dash-suspended-authors', url: '/users?status=Suspended' },
  { testId: 'dash-available-invitations', url: '/invitations?status=Available' },
  { testId: 'dash-pending-reviews', url: '/reviews' },
  { testId: 'dash-published-articles', url: '/audit?action=ArticleApproved' },
];

for (const { testId, url } of cards) {
  test(`the ${testId} card opens ${url}`, async ({ page, admin }) => {
    void admin;
    await page.goto('/');

    await page.getByTestId(testId).click();

    await expect(page).toHaveURL(url);
  });
}
