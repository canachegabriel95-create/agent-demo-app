import type { Page } from '@playwright/test';

// Portal URLs and accounts come from .env (loaded in playwright.config.ts).
const apiBaseUrl = process.env.API_BASE_URL ?? 'http://localhost:5080';

export const portals = {
  admin: {
    url: process.env.ADMIN_PORTAL_URL ?? 'http://localhost:5173',
    apiPortal: 'Admin',
    sessionKey: 'cr.admin.session',
    email: 'QA_ADMIN_EMAIL',
    password: 'QA_ADMIN_PASSWORD',
  },
  author: {
    url: process.env.AUTHOR_PORTAL_URL ?? 'http://localhost:5174',
    apiPortal: 'Author',
    sessionKey: 'cr.author.session',
    email: 'QA_AUTHOR_EMAIL',
    password: 'QA_AUTHOR_PASSWORD',
  },
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

/** Signs in through the portal's login form and waits until the dashboard is shown. Use it to test the login itself. */
export async function login(page: Page, portal: Portal, user = credentials(portal)): Promise<void> {
  await page.goto(`${portals[portal].url}/login`);
  await page.getByLabel('Email').fill(user.email);
  await page.getByLabel('Password').fill(user.password);
  await page.getByTestId('login-submit').click();
  await page.waitForURL(`${portals[portal].url}/`);
}

/** Signs in through the API and returns the session token (used once per run by globalSetup.ts). */
export async function apiLogin(portal: Portal, user = credentials(portal)): Promise<string> {
  const response = await fetch(`${apiBaseUrl}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...user, portal: portals[portal].apiPortal }),
  });
  if (!response.ok) {
    throw new Error(`${portals[portal].apiPortal} login failed with ${response.status}`);
  }
  const body = (await response.json()) as { token: string };
  return body.token;
}

/** Stores the token where the portal keeps its session, so every page the test opens is already signed in. */
export async function useSession(page: Page, portal: Portal, token: string): Promise<void> {
  await page.addInitScript(
    ({ key, value }) => window.sessionStorage.setItem(key, value),
    { key: portals[portal].sessionKey, value: token },
  );
}
