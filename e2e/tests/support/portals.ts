import { expect, type Page } from '@playwright/test';
import { ADMIN_PORTAL_URL, AUTHOR_PORTAL_URL, adminCredentials } from './stack';

async function signIn(page: Page, baseUrl: string, email: string, password: string) {
  await page.goto(`${baseUrl}/login`);
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel(/^Password/).fill(password);
  await page.getByTestId('login-submit').click();
  await expect(page).not.toHaveURL(/\/login/);
}

export function signInAsAdmin(page: Page) {
  const { email, password } = adminCredentials();
  return signIn(page, ADMIN_PORTAL_URL, email, password);
}

export function signInAsAuthor(page: Page, email: string, password: string) {
  return signIn(page, AUTHOR_PORTAL_URL, email, password);
}
