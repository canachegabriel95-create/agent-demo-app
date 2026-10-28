import { test, expect } from '../support/fixtures';
import { ADMIN_SESSION_KEY, apiLogin, apiLogout, apiMeStatus, startSignedIn } from '../support/auth';
import { suspendUser, type TestUser } from '../support/db';

const links = [
  { name: 'Review queue', path: '/reviews' },
  { name: 'Authors', path: '/users' },
  { name: 'Invitations', path: '/invitations' },
  { name: 'Audit history', path: '/audit' },
  { name: 'Dashboard', path: '/' },
];

let admin: TestUser;

test.beforeEach(async ({ page, request, newUser }) => {
  admin = await newUser({ role: 'Administrator' });
  const token = await apiLogin(request, admin, 'Admin');
  await startSignedIn(page, ADMIN_SESSION_KEY, token);
});

test('every navigation link opens its page and is marked as current', async ({ page }) => {
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: 'Main' });

  for (const { name, path } of links) {
    await nav.getByRole('link', { name }).click();

    await expect(page).toHaveURL(path);
    await expect(nav.getByRole('link', { name })).toHaveAttribute('aria-current', 'page');
  }
});

test('logging out clears the session, returns to /login and revokes the token', async ({ page, request }) => {
  await page.goto('/');
  const token = await page.evaluate((key) => sessionStorage.getItem(key), ADMIN_SESSION_KEY);
  expect(token).toBeTruthy();

  await page.getByRole('button', { name: 'Log out' }).click();

  await expect(page).toHaveURL(/\/login/);
  expect(await page.evaluate((key) => sessionStorage.getItem(key), ADMIN_SESSION_KEY)).toBeNull();
  expect(await apiMeStatus(request, token!)).toBe(401);
});

// These two start on the not-found page, which makes no API calls, so the nav click is the first request after the change.
async function openQuietPage(page: import('@playwright/test').Page) {
  await page.goto('/quiet-page');
  await expect(page.getByTestId('current-user')).toBeVisible();
}

test('a 401 from an API call clears the session and redirects to /login', async ({ page, request }) => {
  await openQuietPage(page);
  const token = await page.evaluate((key) => sessionStorage.getItem(key), ADMIN_SESSION_KEY);
  await apiLogout(request, token!); // revoked behind the portal's back

  await page.getByRole('link', { name: 'Authors' }).click();

  await expect(page).toHaveURL(/\/login/);
  expect(await page.evaluate((key) => sessionStorage.getItem(key), ADMIN_SESSION_KEY)).toBeNull();
});

test('suspending the signed-in admin ends the session on the next request', async ({ page }) => {
  await openQuietPage(page);
  await suspendUser(admin.id);

  await page.getByRole('link', { name: 'Authors' }).click();

  await expect(page).toHaveURL(/\/login/);
});

test('an unknown route shows the not-found view', async ({ page }) => {
  await page.goto('/this-page-does-not-exist');

  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
});
