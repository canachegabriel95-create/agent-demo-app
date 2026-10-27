import { apiFetch } from './client';

/** The numbers on the administrator dashboard. Users are authors only. */
export interface AdminDashboard {
  activeAuthors: number;
  suspendedAuthors: number;
  /** Not used, not revoked and not expired. */
  availableInvitations: number;
  pendingReviews: number;
  publishedArticles: number;
}

export function getDashboard(signal?: AbortSignal): Promise<AdminDashboard> {
  return apiFetch<AdminDashboard>('/api/admin/dashboard', { signal });
}
