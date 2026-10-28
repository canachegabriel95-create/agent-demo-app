import { randomUUID } from 'node:crypto';
import { test as base, expect, request, type APIRequestContext } from '@playwright/test';

export { expect };

export interface Author {
  email: string;
  password: string;
  api: APIRequestContext;
}

async function authenticated(baseURL: string, token: string): Promise<APIRequestContext> {
  return request.newContext({ baseURL, extraHTTPHeaders: { Authorization: `Bearer ${token}` } });
}

async function login(api: APIRequestContext, email: string, password: string, portal: 'Admin' | 'Author') {
  const response = await api.post('/api/auth/login', { data: { email, password, portal } });
  expect(response.status(), `login as ${portal}`).toBe(200);
  return (await response.json()).token as string;
}

export function requiredEnv(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`Set ${name} (see .env.example).`);
  return value;
}

export const test = base.extend<{ adminApi: APIRequestContext; newAuthor: () => Promise<Author> }>({
  adminApi: async ({ request: anonymous, baseURL }, use) => {
    const token = await login(anonymous, requiredEnv('API_ADMIN_EMAIL'), requiredEnv('API_ADMIN_PASSWORD'), 'Admin');
    const api = await authenticated(baseURL!, token);
    await use(api);
    await api.dispose();
  },

  // Registers a fresh author through the API (invitation -> register -> login).
  newAuthor: async ({ request: anonymous, adminApi, baseURL }, use) => {
    const contexts: APIRequestContext[] = [];
    await use(async () => {
      const email = `author-${randomUUID()}@example.test`;
      const password = `Aa1!${randomUUID()}`;
      const invitation = await adminApi.post('/api/admin/invitations', { data: { email } });
      expect(invitation.status(), 'create invitation').toBe(201);
      const { code } = await invitation.json();
      const registration = await anonymous.post('/api/registration', {
        data: { invitationCode: code, fullName: 'Test Author', email, password, confirmPassword: password },
      });
      expect(registration.status(), 'register author').toBe(201);
      const token = await login(anonymous, email, password, 'Author');
      const api = await authenticated(baseURL!, token);
      contexts.push(api);
      return { email, password, api };
    });
    await Promise.all(contexts.map((c) => c.dispose()));
  },
});
