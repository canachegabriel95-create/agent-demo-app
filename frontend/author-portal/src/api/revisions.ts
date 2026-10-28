import { apiFetch } from './client';
import type { ArticleContent } from './articles';

/** A revision is open while it is a Draft, PendingReview or Rejected; Approved is final. */
export type RevisionStatus = 'Draft' | 'PendingReview' | 'Rejected' | 'Approved';

/** The open revision of a published article, as shown next to it. */
export interface RevisionSummary {
  id: string;
  status: RevisionStatus;
  updatedAt: string;
  submittedAt: string | null;
}

/** An administrator's decision on one submission of a revision. Who decided is never sent to authors. */
export interface RevisionReviewEntry {
  submissionVersion: number;
  decision: 'Approved' | 'Rejected';
  reason: string | null;
  decidedAt: string;
}

/** What the API returns for a revision of one of the signed-in author's published articles. */
export interface Revision {
  id: string;
  articleId: string;
  title: string;
  summary: string;
  body: string;
  category: { id: string; name: string } | null;
  status: RevisionStatus;
  submissionVersion: number;
  createdAt: string;
  updatedAt: string;
  submittedAt: string | null;
  latestReview: {
    decision: 'Approved' | 'Rejected';
    reason: string | null;
    decidedAt: string;
  } | null;
  /** Newest first. */
  reviews: RevisionReviewEntry[];
  /** What the lifecycle allows right now: `edit`, `delete`, `submit`, `withdraw`. */
  allowedActions: string[];
  /** Concurrency token: send it back when changing, submitting, withdrawing or discarding. */
  version: number;
}

export const REVISION_STATUS_LABELS: Record<RevisionStatus, string> = {
  Draft: 'Revision draft',
  PendingReview: 'Revision pending review',
  Rejected: 'Revision rejected',
  Approved: 'Revision approved',
};

function toBody(content: ArticleContent) {
  return {
    title: content.title,
    summary: content.summary,
    body: content.body,
    categoryId: content.categoryId || null,
  };
}

/**
 * Starts a revision of a published article: a draft copy of its content. The published version is not changed. A 409
 * means the article is not published, or already has a revision in progress.
 */
export function createRevision(articleId: string): Promise<Revision> {
  return apiFetch<Revision>(`/api/articles/${articleId}/revisions`, { method: 'POST', body: {} });
}

export function getRevision(id: string, signal?: AbortSignal): Promise<Revision> {
  return apiFetch<Revision>(`/api/revisions/${id}`, { signal });
}

/** A stale `version` is a 409 (its body names `currentVersion`); so is a revision that can no longer be edited. */
export function updateRevision(
  id: string,
  content: ArticleContent,
  version: number,
): Promise<Revision> {
  return apiFetch<Revision>(`/api/revisions/${id}`, {
    method: 'PUT',
    body: { ...toBody(content), version },
  });
}

/** Only a Draft or Rejected revision can be discarded; the published article is not affected. */
export function deleteRevision(id: string, version: number): Promise<void> {
  return apiFetch<void>(`/api/revisions/${id}?version=${version}`, { method: 'DELETE' });
}

/** Same submission rules as an article: a 400 carries `errors.title|summary|body|categoryId`. */
export function submitRevision(id: string, version: number): Promise<Revision> {
  return apiFetch<Revision>(`/api/revisions/${id}/submit`, { method: 'POST', body: { version } });
}

/** Takes a PendingReview revision back; it becomes a Draft again. A 409 means an administrator decided first. */
export function withdrawRevision(id: string, version: number): Promise<Revision> {
  return apiFetch<Revision>(`/api/revisions/${id}/withdraw`, { method: 'POST', body: { version } });
}
