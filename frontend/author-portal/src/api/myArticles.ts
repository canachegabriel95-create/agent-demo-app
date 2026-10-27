import { apiFetch } from './client';

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
