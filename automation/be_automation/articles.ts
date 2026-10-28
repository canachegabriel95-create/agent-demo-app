import { randomUUID } from 'node:crypto';
import { expect, type APIRequestContext } from '@playwright/test';

export interface Article {
  id: string;
  title: string;
  status: string;
  version: number;
  submissionVersion: number;
  allowedActions: string[];
}

export async function firstCategoryId(api: APIRequestContext) {
  const categories = await (await api.get('/api/categories')).json();
  return categories[0].id as string;
}

// Content that passes the submission rules (title 5-150, summary 20-500, body 100-20000, category).
export async function submittableContent(api: APIRequestContext) {
  return {
    title: `Article ${randomUUID()}`,
    summary: 'A summary that is long enough to be submitted.',
    body: 'A body that is long enough to be submitted for review by an administrator. '.repeat(2),
    categoryId: await firstCategoryId(api),
  };
}

export async function createDraft(api: APIRequestContext, content: Record<string, unknown> = {}): Promise<Article> {
  const response = await api.post('/api/articles', { data: { title: `Draft ${randomUUID()}`, ...content } });
  expect(response.status(), 'create draft').toBe(201);
  return response.json();
}

export async function submitArticle(api: APIRequestContext, article: Article): Promise<Article> {
  const response = await api.post(`/api/articles/${article.id}/submit`, { data: { version: article.version } });
  expect(response.status(), 'submit article').toBe(200);
  return response.json();
}

export async function submittedArticle(api: APIRequestContext): Promise<Article> {
  return submitArticle(api, await createDraft(api, await submittableContent(api)));
}
