import type { APIRequestContext, APIResponse } from '@playwright/test';

export const apiUrl = process.env.API_URL ?? 'http://localhost:5080';

/** The API as one signed-in user. */
export function apiAs(request: APIRequestContext, token: string) {
  const headers = { Authorization: `Bearer ${token}` };
  const url = (path: string) => `${apiUrl}${path}`;
  return {
    get: (path: string) => request.get(url(path), { headers }),
    post: (path: string, data?: unknown) => request.post(url(path), { headers, data }),
    put: (path: string, data?: unknown) => request.put(url(path), { headers, data }),
    delete: (path: string) => request.delete(url(path), { headers }),
  };
}

export type Api = ReturnType<typeof apiAs>;

/** Reads the JSON of a response that must have succeeded. */
export async function ok<T>(response: APIResponse): Promise<T> {
  if (!response.ok()) {
    throw new Error(`${response.url()} answered ${response.status()}: ${await response.text()}`);
  }
  return (await response.json()) as T;
}

export interface Article {
  id: string;
  status: string;
  version: number;
  submissionVersion: number;
}

export async function firstCategoryId(api: Api): Promise<string> {
  const categories = await ok<{ id: string }[]>(await api.get('/api/categories'));
  return categories[0].id;
}

/** A Draft that already satisfies the submit rules. */
export async function createDraft(
  api: Api,
  title = `E2E article ${crypto.randomUUID()}`,
  body = 'A body that is long enough to be submitted. '.repeat(4),
): Promise<Article> {
  return ok<Article>(
    await api.post('/api/articles', {
      title,
      summary: 'A summary that is long enough to be submitted.',
      body,
      categoryId: await firstCategoryId(api),
    }),
  );
}

export async function createSubmittedArticle(api: Api, title?: string, body?: string): Promise<Article> {
  const draft = await createDraft(api, title, body);
  return ok<Article>(await api.post(`/api/articles/${draft.id}/submit`, { version: draft.version }));
}

export async function approve(adminApi: Api, article: Article): Promise<Article> {
  return ok<Article>(
    await adminApi.post(`/api/admin/articles/${article.id}/approve`, {
      version: article.version,
      submissionVersion: article.submissionVersion,
    }),
  );
}

export async function createInvitation(adminApi: Api, email: string, validForDays = 7) {
  return ok<{ id: string; code: string; version: number; email: string }>(
    await adminApi.post('/api/admin/invitations', { email, validForDays }),
  );
}

export async function reject(adminApi: Api, article: Article, reason = 'Needs more detail before publishing.') {
  return ok<Article>(
    await adminApi.post(`/api/admin/articles/${article.id}/reject`, {
      version: article.version,
      submissionVersion: article.submissionVersion,
      reason,
    }),
  );
}

export async function withdraw(api: Api, article: Article): Promise<Article> {
  return ok<Article>(await api.post(`/api/articles/${article.id}/withdraw`, { version: article.version }));
}

/** Edits a Rejected article (new title) and submits it again. */
export async function resubmit(api: Api, article: Article, title = `Revised ${crypto.randomUUID()}`): Promise<Article> {
  const edited = await ok<Article>(
    await api.put(`/api/articles/${article.id}`, {
      title,
      summary: 'A revised summary that is long enough to be submitted.',
      body: 'A revised body that is long enough to be submitted. '.repeat(4),
      categoryId: await firstCategoryId(api),
      version: article.version,
    }),
  );
  return ok<Article>(await api.post(`/api/articles/${edited.id}/submit`, { version: edited.version }));
}
