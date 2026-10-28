import type { Page } from '@playwright/test';

// Portal URLs and accounts come from .env (loaded in playwright.config.ts).
export const portals = {
  admin: { url: process.env.ADMIN_PORTAL_URL ?? 'http://localhost:5173', email: 'QA_ADMIN_EMAIL', password: 'QA_ADMIN_PASSWORD' },
  author: { url: process.env.AUTHOR_PORTAL_URL ?? 'http://localhost:5174', email: 'QA_AUTHOR_EMAIL', password: 'QA_AUTHOR_PASSWORD' },
};

export type Portal = keyof typeof portals;

export function credentials(portal: Portal): { email: string; password: string } {
  const email = process.env[portals[portal].email];
  const password = process.env[portals[portal].password];
  if (!email || !password) {
    throw new Error(`${portals[portal].email} / ${portals[portal].password} missing in .env`);
  }
  return { email, password };
}

/** Signs in through the portal's login form and waits until the dashboard is shown. */
export async function login(page: Page, portal: Portal, user = credentials(portal)): Promise<void> {
  await page.goto(`${portals[portal].url}/login`);
  await page.getByLabel('Email').fill(user.email);
  await page.getByLabel('Password').fill(user.password);
  await page.getByTestId('login-submit').click();
  await page.waitForURL(`${portals[portal].url}/`);
}
