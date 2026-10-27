import { apiFetch } from './client';
import type { ArticleContent, RevisionStatus } from './articles';

/** The decision on the latest reviewed submission of a revision. */
export interface RevisionReview {
  decision: 'Approved' | 'Rejected';
  reason: string | null;
  decidedAt: string;
}

/** A revision of one of the signed-in author's published articles. Editing it never changes what readers see. */
export interface Revision {
  id: string;
  articleId: string;
  number: number;
  title: string;
  summary: string;
  body: string;
  category: { id: string; name: string } | null;
  status: RevisionStatus;
  submissionVersion: number;
  createdAt: string;
  updatedAt: string;
  submittedAt: string | null;
  latestReview: RevisionReview | null;
  /** What the lifecycle allows right now: `edit`, `submit`, `withdraw`. */
  allowedActions: string[];
  /** Concurrency token: send it back when saving, submitting or withdrawing. */
  version: number;
}

/** A decision on one submission of one revision. Who decided is never sent to authors. */
export interface RevisionReviewEntry {
  revisionNumber: number;
  submissionVersion: number;
  decision: 'Approved' | 'Rejected';
  reason: string | null;
  decidedAt: string;
}

/** Starts a revision as a copy of the published article. A 409 means it is not published or one is already open. */
export function createRevision(articleId: string): Promise<Revision> {
  return apiFetch<Revision>(`/api/articles/${articleId}/revision`, { method: 'POST' });
}

/** A 404 means the article has no revision in progress. */
export function getRevision(articleId: string, signal?: AbortSignal): Promise<Revision> {
  return apiFetch<Revision>(`/api/articles/${articleId}/revision`, { signal });
}

/** A stale `version` is a 409; so is a revision that can no longer be edited. A 400 carries `errors.title|...`. */
export function updateRevision(
  articleId: string,
  content: ArticleContent,
  version: number,
): Promise<Revision> {
  return apiFetch<Revision>(`/api/articles/${articleId}/revision`, {
    method: 'PUT',
    body: {
      title: content.title,
      summary: content.summary,
      body: content.body,
      categoryId: content.categoryId || null,
      version,
    },
  });
}

/** The submission rules are stricter than the draft rules: a 400 carries `errors.title|summary|body|categoryId`. */
export function submitRevision(articleId: string, version: number): Promise<Revision> {
  return apiFetch<Revision>(`/api/articles/${articleId}/revision/submit`, {
    method: 'POST',
    body: { version },
  });
}

/** Takes a PendingReview revision back; it becomes a Draft again. A 409 means an administrator decided first. */
export function withdrawRevision(articleId: string, version: number): Promise<Revision> {
  return apiFetch<Revision>(`/api/articles/${articleId}/revision/withdraw`, {
    method: 'POST',
    body: { version },
  });
}

/** Newest first, over all revisions of the article. */
export function listRevisionReviews(
  articleId: string,
  signal?: AbortSignal,
): Promise<RevisionReviewEntry[]> {
  return apiFetch<RevisionReviewEntry[]>(`/api/articles/${articleId}/revision/reviews`, { signal });
}
