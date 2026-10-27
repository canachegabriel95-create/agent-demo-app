import { API_URL, adminCredentials } from './stack';

/** Strong enough for the password policy (12-128 characters with upper, lower, digit and symbol). */
export const AUTHOR_PASSWORD = 'E2e!Passw0rd#2026';

export const SUMMARY = 'A summary that is long enough to be submitted for review.';
export const BODY = 'A body that is long enough to be submitted. '.repeat(5);

async function call<T>(method: string, path: string, token: string | undefined, body?: unknown): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  if (!response.ok) {
    throw new Error(`${method} ${path} -> ${response.status} ${text}`);
  }

  return (text ? JSON.parse(text) : undefined) as T;
}

export async function login(email: string, password: string, portal: 'Admin' | 'Author'): Promise<string> {
  const result = await call<{ token: string }>('POST', '/api/auth/login', undefined, { email, password, portal });
  return result.token;
}

export function adminToken(): Promise<string> {
  const { email, password } = adminCredentials();
  return login(email, password, 'Admin');
}

export interface Author {
  email: string;
  fullName: string;
  token: string;
}

/** Invites and registers a new author through the API, the way the portals do. */
export async function registerAuthor(admin: string, label: string): Promise<Author> {
  const unique = `${Date.now()}${Math.floor(Math.random() * 1000)}`;
  const email = `e2e.${label}.${unique}@example.test`;
  const fullName = `E2E ${label} ${unique}`;
  const invitation = await call<{ code: string }>('POST', '/api/admin/invitations', admin, { email, validForDays: 1 });
  await call('POST', '/api/registration', undefined, {
    invitationCode: invitation.code,
    fullName,
    email,
    password: AUTHOR_PASSWORD,
    confirmPassword: AUTHOR_PASSWORD,
  });
  return { email, fullName, token: await login(email, AUTHOR_PASSWORD, 'Author') };
}

interface ArticleResponse {
  id: string;
  version: number;
}

async function firstCategory(token: string): Promise<string> {
  const categories = await call<{ id: string }[]>('GET', '/api/categories', token);
  return categories[0]!.id;
}

/** Creates an article for the author, submits it and has the administrator approve it. Returns its id. */
export async function publishedArticle(admin: string, author: Author, title: string): Promise<string> {
  const categoryId = await firstCategory(author.token);
  const created = await call<ArticleResponse>('POST', '/api/articles', author.token, {
    title,
    summary: SUMMARY,
    body: BODY,
    categoryId,
  });
  const pending = await call<ArticleResponse>('POST', `/api/articles/${created.id}/submit`, author.token, {
    version: created.version,
  });
  await call('POST', `/api/admin/articles/${created.id}/approve`, admin, {
    version: pending.version,
    submissionVersion: 1,
  });
  return created.id;
}
