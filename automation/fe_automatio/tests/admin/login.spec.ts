import { randomUUID } from 'node:crypto';
import { test, expect } from '../support/fixtures';

const GENERIC_ERROR = 'Invalid email or password.';

test.beforeEach(async ({ page }) => {
  await page.goto('/login');
});

async function signIn(page: import('@playwright/test').Page, email: string, password: string) {
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

test('valid admin credentials open the dashboard and store the session token', async ({ page, newUser }) => {
  const admin = await newUser({ role: 'Administrator' });

  await signIn(page, admin.email, admin.password);

  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { name: 'Admin Portal' })).toBeVisible();
  expect(await page.evaluate(() => sessionStorage.getItem('cr.admin.session'))).toBeTruthy();
});

test('wrong password shows the generic error', async ({ page, newUser }) => {
  const admin = await newUser({ role: 'Administrator' });

  await signIn(page, admin.email, 'Wrong-password-1!');

  await expect(page.getByRole('alert')).toHaveText(GENERIC_ERROR);
});

test('unknown email shows the generic error', async ({ page }) => {
  await signIn(page, `e2e-unknown-${randomUUID()}@example.test`, 'Wrong-password-1!');

  await expect(page.getByRole('alert')).toHaveText(GENERIC_ERROR);
});

test('an author account shows the generic error in the admin portal', async ({ page, newUser }) => {
  const author = await newUser({ role: 'Author' });

  await signIn(page, author.email, author.password);

  await expect(page.getByRole('alert')).toHaveText(GENERIC_ERROR);
});

test('empty email and password show field validation messages', async ({ page }) => {
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page.getByText('Email is required.')).toBeVisible();
  await expect(page.getByText('Password is required.')).toBeVisible();
});

test('a suspended admin cannot sign in', async ({ page, newUser }) => {
  const admin = await newUser({ role: 'Administrator', status: 'Suspended' });

  await signIn(page, admin.email, admin.password);

  await expect(page.getByRole('alert')).toHaveText(GENERIC_ERROR);
  await expect(page).toHaveURL(/\/login/);
});

for (const path of ['/', '/users', '/invitations']) {
  test(`opening ${path} without a session redirects to /login`, async ({ page }) => {
    await page.goto(path);

    await expect(page).toHaveURL(/\/login/);
  });
}

test('submit button is disabled and shows progress while signing in', async ({ page, newUser }) => {
  const admin = await newUser({ role: 'Administrator' });
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route('**/api/auth/login', async (route) => {
    await gate;
    await route.continue();
  });

  await signIn(page, admin.email, admin.password);

  const submit = page.getByRole('button', { name: 'Signing in…' });
  await expect(submit).toBeDisabled();
  release();
  await expect(page).toHaveURL('/');
});
