import type { APIRequestContext, Page } from '@playwright/test';
import { apiUrl } from './api';
import type { TestUser } from './db';

export const ADMIN_SESSION_KEY = 'cr.admin.session';
export const AUTHOR_SESSION_KEY = 'cr.author.session';

/** Signs the user in through the API and returns the session token. */
export async function apiLogin(request: APIRequestContext, user: TestUser, portal: 'Admin' | 'Author'): Promise<string> {
  const response = await request.post(`${apiUrl}/api/auth/login`, {
    data: { email: user.email, password: user.password, portal },
  });
  return (await response.json()).token as string;
}

/** Makes every page load start with this token in sessionStorage, i.e. already signed in. */
export async function startSignedIn(page: Page, key: string, token: string): Promise<void> {
  await page.addInitScript(([k, t]) => sessionStorage.setItem(k, t), [key, token]);
}

export async function apiMeStatus(request: APIRequestContext, token: string): Promise<number> {
  const response = await request.get(`${apiUrl}/api/auth/me`, { headers: { Authorization: `Bearer ${token}` } });
  return response.status();
}

export async function apiLogout(request: APIRequestContext, token: string): Promise<void> {
  await request.post(`${apiUrl}/api/auth/logout`, { headers: { Authorization: `Bearer ${token}` } });
}
