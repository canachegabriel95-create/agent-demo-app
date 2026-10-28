import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { test, expect } from '../support/fixtures';

const GENERIC_ERROR = 'Invalid email or password.';

test.beforeEach(async ({ page }) => {
  await page.goto('/login');
});

async function signIn(page: Page, email: string, password: string) {
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

test('valid author credentials open the dashboard and store the session token', async ({ page, newUser }) => {
  const author = await newUser({ role: 'Author' });

  await signIn(page, author.email, author.password);

  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { name: 'Author Portal' })).toBeVisible();
  expect(await page.evaluate(() => sessionStorage.getItem('cr.author.session'))).toBeTruthy();
});

test('wrong password shows the generic error', async ({ page, newUser }) => {
  const author = await newUser({ role: 'Author' });

  await signIn(page, author.email, 'Wrong-password-1!');

  await expect(page.getByRole('alert')).toHaveText(GENERIC_ERROR);
});

test('unknown email shows the generic error', async ({ page }) => {
  await signIn(page, `e2e-unknown-${randomUUID()}@example.test`, 'Wrong-password-1!');

  await expect(page.getByRole('alert')).toHaveText(GENERIC_ERROR);
});

test('an administrator account shows the generic error in the author portal', async ({ page, newUser }) => {
  const admin = await newUser({ role: 'Administrator' });

  await signIn(page, admin.email, admin.password);

  await expect(page.getByRole('alert')).toHaveText(GENERIC_ERROR);
});

test('empty email and password show field validation messages', async ({ page }) => {
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page.getByText('Email is required.')).toBeVisible();
  await expect(page.getByText('Password is required.')).toBeVisible();
});

test('a suspended author cannot sign in', async ({ page, newUser }) => {
  const author = await newUser({ role: 'Author', status: 'Suspended' });

  await signIn(page, author.email, author.password);

  await expect(page.getByRole('alert')).toHaveText(GENERIC_ERROR);
  await expect(page).toHaveURL(/\/login/);
});

for (const path of ['/', '/articles', '/profile']) {
  test(`opening ${path} without a session redirects to /login`, async ({ page }) => {
    await page.goto(path);

    await expect(page).toHaveURL(/\/login/);
  });
}

test('the registration link opens the registration page', async ({ page }) => {
  await page.getByTestId('register-link').click();

  await expect(page).toHaveURL('/register');
});
