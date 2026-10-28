import type { Page } from '@playwright/test';
import { apiAs } from '../support/api';
import { AUTHOR_SESSION_KEY, apiLogout, apiMeStatus } from '../support/auth';
import { test, expect } from '../support/fixtures';

const links = [
  { name: 'My Articles', path: '/articles' },
  { name: 'Feed', path: '/feed' },
  { name: 'Profile', path: '/profile' },
  { name: 'Dashboard', path: '/' },
];

const token = (page: Page) => page.evaluate((key) => sessionStorage.getItem(key), AUTHOR_SESSION_KEY);

/** The not-found page makes no API calls, so the next nav click is the first request after a change. */
async function openQuietPage(page: Page) {
  await page.goto('/quiet-page');
  await expect(page.getByTestId('current-user')).toBeVisible();
}

test('every navigation link opens its page', async ({ page, author }) => {
  void author;
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: 'Main' });

  for (const { name, path } of links) {
    await nav.getByRole('link', { name }).click();

    await expect(page).toHaveURL(path);
  }
});

test('logging out clears the session, returns to /login and revokes the token', async ({ page, request, author }) => {
  void author;
  await page.goto('/');
  const before = await token(page);
  expect(before).toBeTruthy();

  await page.getByRole('button', { name: 'Log out' }).click();

  await expect(page).toHaveURL(/\/login/);
  expect(await token(page)).toBeNull();
  expect(await apiMeStatus(request, before!)).toBe(401);
});

test('a 401 from an API call clears the session and redirects to /login', async ({ page, request, author }) => {
  void author;
  await openQuietPage(page);
  await apiLogout(request, (await token(page))!);

  await page.getByRole('link', { name: 'My Articles' }).click();

  await expect(page).toHaveURL(/\/login/);
  expect(await token(page)).toBeNull();
});

test('admin pages are not part of the author portal', async ({ page, author }) => {
  void author;

  await page.goto('/users');

  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
});

test('admin data is refused to an author', async ({ request, author }) => {
  const response = await apiAs(request, author.token).get('/api/admin/users');

  expect(response.status()).toBe(403);
});

test('an unknown route shows the not-found view', async ({ page, author }) => {
  void author;

  await page.goto('/this-page-does-not-exist');

  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
});
