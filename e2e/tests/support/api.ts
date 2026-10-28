import { expect, type APIRequestContext } from '@playwright/test';
import { ADMIN_EMAIL, ADMIN_PASSWORD, API_URL } from './env';

const AUTHOR_PASSWORD = 'Wr1ter!Passw0rd#E2E';
export const SUMMARY = 'A summary that is long enough to be submitted for review.';
export const BODY = 'The body of the article. '.repeat(8);

export interface Author {
  email: string;
  password: string;
  token: string;
}

/** Login and registration are rate limited per client; wait out a 429 instead of failing the test. */
async function post(request: APIRequestContext, path: string, token: string | undefined, data: unknown) {
  for (let attempt = 0; ; attempt++) {
    const response = await request.post(`${API_URL}${path}`, {
      data,
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    });
    if (response.status() === 429 && attempt < 2) {
      const seconds = Number(response.headers()['retry-after'] ?? 30);
      await new Promise((resolve) => setTimeout(resolve, (Number.isFinite(seconds) ? seconds : 30) * 1000));
      continue;
    }

    expect(response.ok(), `${path}: ${response.status()} ${await response.text()}`).toBeTruthy();
    return response.json();
  }
}

export async function loginAdmin(request: APIRequestContext): Promise<string> {
  const login = await post(request, '/api/auth/login', undefined, {
    email: ADMIN_EMAIL,
    password: ADMIN_PASSWORD,
    portal: 'Admin',
  });
  return login.token as string;
}

/** Invites a fresh author, registers them and signs them in, all through the API. */
export async function createAuthor(request: APIRequestContext, adminToken: string): Promise<Author> {
  const email = `e2e.${Date.now()}.${Math.floor(Math.random() * 1e6)}@demo.local`;
  const invitation = await post(request, '/api/admin/invitations', adminToken, { email });
  await post(request, '/api/registration', undefined, {
    invitationCode: invitation.code,
    fullName: 'E2E Author',
    email,
    password: AUTHOR_PASSWORD,
    confirmPassword: AUTHOR_PASSWORD,
  });
  const login = await post(request, '/api/auth/login', undefined, {
    email,
    password: AUTHOR_PASSWORD,
    portal: 'Author',
  });
  return { email, password: AUTHOR_PASSWORD, token: login.token as string };
}

/** Writes, submits and approves an article, so that it is Published. Returns its id. */
export async function publishArticle(
  request: APIRequestContext,
  author: Author,
  adminToken: string,
  title: string,
): Promise<string> {
  const categories = await (
    await request.get(`${API_URL}/api/categories`, { headers: { Authorization: `Bearer ${author.token}` } })
  ).json();
  const created = await post(request, '/api/articles', author.token, {
    title,
    summary: SUMMARY,
    body: BODY,
    categoryId: categories[0].id,
  });
  const submitted = await post(request, `/api/articles/${created.id}/submit`, author.token, {
    version: created.version,
  });
  await post(request, `/api/admin/articles/${created.id}/approve`, adminToken, {
    version: submitted.version,
    submissionVersion: submitted.submissionVersion,
  });
  return created.id as string;
}

/** What readers see now, as an author reads it from the feed. */
export async function feedTitle(request: APIRequestContext, token: string, id: string): Promise<string> {
  const response = await request.get(`${API_URL}/api/feed/${id}`, { headers: { Authorization: `Bearer ${token}` } });
  expect(response.ok()).toBeTruthy();
  return (await response.json()).title as string;
}
