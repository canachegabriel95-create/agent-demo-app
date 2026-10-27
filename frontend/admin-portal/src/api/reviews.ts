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
