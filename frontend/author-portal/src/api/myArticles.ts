import { apiFetch } from './client';
import type { ArticleStatus } from './articles';
import type { RevisionSummary } from './revisions';

/** How many of the signed-in author's articles are in each status; all four are always present. */
export interface ArticleStatusCounts {
  draft: number;
  pendingReview: number;
  published: number;
  rejected: number;
}

export function getArticleStats(signal?: AbortSignal): Promise<ArticleStatusCounts> {
  return apiFetch<ArticleStatusCounts>('/api/me/articles/stats', { signal });
}

export type MyArticleSort = 'updatedAt' | 'submittedAt' | 'title';

/** One row of the author's article list; the text of the article is not part of it. */
export interface MyArticleItem {
  id: string;
  title: string;
  category: { id: string; name: string } | null;
  status: ArticleStatus;
  updatedAt: string;
  submittedAt: string | null;
  publishedAt: string | null;
  /** Set only while the article is Rejected: the reason of its latest rejection. */
  latestRejectionReason: string | null;
  /** What the lifecycle allows right now: `edit`, `delete`, `submit`, `withdraw`, `revise`. */
  allowedActions: string[];
  /** The revision in progress of a published article; `null` when there is none. */
  revision: RevisionSummary | null;
  version: number;
}

export interface MyArticlesFilters {
  search?: string;
  status?: ArticleStatus;
  categoryId?: string;
  /** The API sorts by `updatedAt` (newest first) when this is left out. */
  sort?: MyArticleSort;
  page?: number;
}

export interface ArticlePage {
  items: MyArticleItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export const ARTICLE_STATUSES: ArticleStatus[] = [
  'Draft',
  'PendingReview',
  'Published',
  'Rejected',
];
export const ARTICLE_SORTS: MyArticleSort[] = ['updatedAt', 'submittedAt', 'title'];

export function isArticleStatus(value: string | null): value is ArticleStatus {
  return ARTICLE_STATUSES.includes(value as ArticleStatus);
}

export function isArticleSort(value: string | null): value is MyArticleSort {
  return ARTICLE_SORTS.includes(value as MyArticleSort);
}

export function listMyArticles(
  filters: MyArticlesFilters,
  signal?: AbortSignal,
): Promise<ArticlePage> {
  const query = new URLSearchParams();
  if (filters.search) {
    query.set('search', filters.search);
  }
  if (filters.status) {
    query.set('status', filters.status);
  }
  if (filters.categoryId) {
    query.set('categoryId', filters.categoryId);
  }
  if (filters.sort) {
    query.set('sort', filters.sort);
  }
  if (filters.page && filters.page > 1) {
    query.set('page', String(filters.page));
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : '';
  return apiFetch<ArticlePage>(`/api/me/articles${suffix}`, { signal });
}
