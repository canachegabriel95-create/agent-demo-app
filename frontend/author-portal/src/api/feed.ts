import { apiFetch } from './client';

/** A published article in the feed. Authors are named, never identified. */
export interface FeedItem {
  id: string;
  title: string;
  summary: string;
  category: { id: string; name: string } | null;
  authorDisplayName: string;
  publishedAt: string;
}

/** A published article in full, as the administrator approved it. */
export interface FeedArticle extends FeedItem {
  body: string;
}

export interface FeedPage {
  items: FeedItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface FeedFilters {
  search?: string;
  categoryId?: string;
  page?: number;
}

export function listFeed(filters: FeedFilters, signal?: AbortSignal): Promise<FeedPage> {
  const query = new URLSearchParams();
  if (filters.search) {
    query.set('search', filters.search);
  }
  if (filters.categoryId) {
    query.set('categoryId', filters.categoryId);
  }
  if (filters.page && filters.page > 1) {
    query.set('page', String(filters.page));
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : '';
  return apiFetch<FeedPage>(`/api/feed${suffix}`, { signal });
}

/** Only published articles exist here: anything else, or an unknown id, is a 404. */
export function getFeedArticle(id: string, signal?: AbortSignal): Promise<FeedArticle> {
  return apiFetch<FeedArticle>(`/api/feed/${id}`, { signal });
}
