import { apiFetch } from './client';
import type { Page } from './invitations';

export type UserStatus = 'Active' | 'Suspended';

export const USER_STATUSES: readonly UserStatus[] = ['Active', 'Suspended'];

export function isUserStatus(value: string | null): value is UserStatus {
  return USER_STATUSES.some((status) => status === value);
}

export type SortDirection = 'asc' | 'desc';

export function isSortDirection(value: string | null): value is SortDirection {
  return value === 'asc' || value === 'desc';
}

export interface UserListItem {
  id: string;
  fullName: string;
  email: string;
  status: UserStatus;
  registeredAt: string;
}

export interface ArticleCounts {
  draft: number;
  pendingReview: number;
  published: number;
  rejected: number;
}

export interface UserDetails {
  id: string;
  fullName: string;
  email: string;
  bio: string | null;
  role: 'Author';
  status: UserStatus;
  registeredAt: string;
  suspendedAt: string | null;
  suspensionReason: string | null;
  articleCounts: ArticleCounts;
  /** Concurrency token: send it back when editing, suspending or reactivating (F06.5). */
  version: number;
}

export interface UserFilters {
  search?: string;
  status?: UserStatus;
  /** Direction of the "Registered" sort; the API default is newest first (`desc`). */
  direction?: SortDirection;
  page?: number;
}

export function listUsers(filters: UserFilters, signal?: AbortSignal): Promise<Page<UserListItem>> {
  const query = new URLSearchParams();
  if (filters.search) {
    query.set('search', filters.search);
  }
  if (filters.status) {
    query.set('status', filters.status);
  }
  if (filters.direction) {
    query.set('sort', 'registeredAt');
    query.set('direction', filters.direction);
  }
  if (filters.page && filters.page > 1) {
    query.set('page', String(filters.page));
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : '';
  return apiFetch<Page<UserListItem>>(`/api/admin/users${suffix}`, { signal });
}

/** An unknown id (or an administrator's id) is a 404. */
export function getUser(id: string, signal?: AbortSignal): Promise<UserDetails> {
  return apiFetch<UserDetails>(`/api/admin/users/${encodeURIComponent(id)}`, { signal });
}
