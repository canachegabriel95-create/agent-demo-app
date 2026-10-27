import { apiFetch } from './client';

export type ArticleStatus = 'Draft' | 'PendingReview' | 'Published' | 'Rejected';

export interface ArticleReview {
  decision: 'Approved' | 'Rejected';
  reason: string | null;
  decidedAt: string;
}

/** What the API returns for one of the signed-in author's articles. */
export interface Article {
  id: string;
  title: string;
  summary: string;
  body: string;
  category: { id: string; name: string } | null;
  status: ArticleStatus;
  submissionVersion: number;
  createdAt: string;
  updatedAt: string;
  submittedAt: string | null;
  publishedAt: string | null;
  latestReview: ArticleReview | null;
  /** What the lifecycle allows right now, e.g. `edit`, `delete`, `submit`, `withdraw`. */
  allowedActions: string[];
  /** Concurrency token: send it back when saving. */
  version: number;
}

/** The editable content of an article. An empty `categoryId` means no category. */
export interface ArticleContent {
  title: string;
  summary: string;
  body: string;
  categoryId: string;
}

function toBody(content: ArticleContent) {
  return {
    title: content.title,
    summary: content.summary,
    body: content.body,
    categoryId: content.categoryId || null,
  };
}

export function getArticle(id: string, signal?: AbortSignal): Promise<Article> {
  return apiFetch<Article>(`/api/articles/${id}`, { signal });
}

/** Only the title is required for a draft. A 400 carries `errors.title|summary|body|categoryId`. */
export function createArticle(content: ArticleContent): Promise<Article> {
  return apiFetch<Article>('/api/articles', { method: 'POST', body: toBody(content) });
}

/** A stale `version` is a 409 (its body names `currentVersion`); so is an article that can no longer be edited. */
export function updateArticle(
  id: string,
  content: ArticleContent,
  version: number,
): Promise<Article> {
  return apiFetch<Article>(`/api/articles/${id}`, {
    method: 'PUT',
    body: { ...toBody(content), version },
  });
}

/** Only a Draft can be deleted. A stale `version`, or an article that is no longer a draft, is a 409. */
export function deleteArticle(id: string, version: number): Promise<void> {
  return apiFetch<void>(`/api/articles/${id}?version=${version}`, { method: 'DELETE' });
}

/** An administrator's decision on one submission. Who decided is never sent to authors. */
export interface ArticleReviewEntry {
  submissionVersion: number;
  decision: 'Approved' | 'Rejected';
  reason: string | null;
  decidedAt: string;
}

/** Newest submission first; empty until the first decision. */
export function listReviews(id: string, signal?: AbortSignal): Promise<ArticleReviewEntry[]> {
  return apiFetch<ArticleReviewEntry[]>(`/api/articles/${id}/reviews`, { signal });
}

/**
 * Sends a Draft, or resubmits a Rejected article, for review. A 400 carries `errors.title|summary|body|categoryId`
 * (the submission rules are stricter than the draft rules); a 409 means the article changed status or version.
 */
export function submitArticle(id: string, version: number): Promise<Article> {
  return apiFetch<Article>(`/api/articles/${id}/submit`, { method: 'POST', body: { version } });
}

/** Takes a PendingReview article back; it becomes a Draft again. A 409 means an administrator decided first. */
export function withdrawArticle(id: string, version: number): Promise<Article> {
  return apiFetch<Article>(`/api/articles/${id}/withdraw`, { method: 'POST', body: { version } });
}
