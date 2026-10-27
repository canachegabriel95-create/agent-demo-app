import { apiFetch } from './client';
import type { Page } from './invitations';
import type { SortDirection, UserStatus } from './users';

export interface ReviewQueueItem {
  articleId: string;
  title: string;
  category: { id: string; name: string };
  author: { id: string; fullName: string; status: UserStatus };
  submittedAt: string;
  submissionVersion: number;
}

export interface ReviewQueueFilters {
  authorId?: string;
  categoryId?: string;
  /** Direction of the "Submitted" sort; the API default is oldest first (`asc`). */
  direction?: SortDirection;
  page?: number;
}

/** Pending articles only. Articles of suspended authors are included: the item says so. */
export function listReviewQueue(
  filters: ReviewQueueFilters,
  signal?: AbortSignal,
): Promise<Page<ReviewQueueItem>> {
  const query = new URLSearchParams();
  if (filters.authorId) {
    query.set('authorId', filters.authorId);
  }
  if (filters.categoryId) {
    query.set('categoryId', filters.categoryId);
  }
  if (filters.direction) {
    query.set('sort', 'submittedAt');
    query.set('direction', filters.direction);
  }
  if (filters.page && filters.page > 1) {
    query.set('page', String(filters.page));
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : '';
  return apiFetch<Page<ReviewQueueItem>>(`/api/admin/reviews/queue${suffix}`, { signal });
}

export interface AuthorLookupItem {
  id: string;
  fullName: string;
}

/** Up to 20 authors whose name or email contains `search`, by name. */
export function lookupAuthors(search: string, signal?: AbortSignal): Promise<AuthorLookupItem[]> {
  const query = search ? `?search=${encodeURIComponent(search)}` : '';
  return apiFetch<AuthorLookupItem[]>(`/api/admin/authors/lookup${query}`, { signal });
}

export type ReviewArticleStatus = 'Draft' | 'PendingReview' | 'Published' | 'Rejected';

export interface ReviewHistoryEntry {
  submissionVersion: number;
  decision: 'Approved' | 'Rejected';
  reason: string | null;
  decidedAt: string;
  reviewer: { id: string; fullName: string };
}

/** An article as the administrator reviews it: the content is the submitted snapshot, not the author's draft. */
export interface ReviewArticle {
  id: string;
  title: string;
  summary: string;
  body: string;
  category: { id: string; name: string } | null;
  status: ReviewArticleStatus;
  submissionVersion: number;
  submittedAt: string;
  publishedAt: string | null;
  author: { id: string; fullName: string; email: string; status: UserStatus };
  reviewHistory: ReviewHistoryEntry[];
  canReview: boolean;
  reviewBlockedReason: string | null;
  /** Concurrency token: send it back with the decision. */
  version: number;
}

/** 404 for an unknown article and for one that was never submitted (a private draft). */
export function getReviewArticle(id: string, signal?: AbortSignal): Promise<ReviewArticle> {
  return apiFetch<ReviewArticle>(`/api/admin/articles/${encodeURIComponent(id)}`, { signal });
}

/**
 * Publishes the article. A 409 means it changed since it was opened (the ProblemDetails carries `currentStatus`), or
 * the author is suspended.
 */
export function approveArticle(
  id: string,
  version: number,
  submissionVersion: number,
): Promise<ReviewArticle> {
  return apiFetch<ReviewArticle>(`/api/admin/articles/${encodeURIComponent(id)}/approve`, {
    method: 'POST',
    body: { version, submissionVersion },
  });
}

export const REVIEW_REASON_MIN = 10;
export const REVIEW_REASON_MAX = 1000;

/** Sends the article back with a reason (10-1000 characters; a 400 carries `errors.reason`). */
export function rejectArticle(
  id: string,
  version: number,
  submissionVersion: number,
  reason: string,
): Promise<ReviewArticle> {
  return apiFetch<ReviewArticle>(`/api/admin/articles/${encodeURIComponent(id)}/reject`, {
    method: 'POST',
    body: { version, submissionVersion, reason },
  });
}

/** What the article is now, read from the 409 the API answered with; `undefined` for any other error. */
export function currentStatusOf(error: unknown): string | undefined {
  const body = (error as { body?: unknown } | null)?.body;
  if (typeof body === 'object' && body !== null && 'currentStatus' in body) {
    const value = (body as { currentStatus: unknown }).currentStatus;
    return typeof value === 'string' ? value : undefined;
  }

  return undefined;
}
