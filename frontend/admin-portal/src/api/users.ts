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

export const FULL_NAME_MIN = 2;
export const FULL_NAME_MAX = 100;
export const BIO_MAX = 1000;
export const REASON_MIN = 10;
export const REASON_MAX = 500;

/**
 * Changes the display name and biography. A 400 carries `errors.fullName` / `errors.bio`; a stale `version` is a 409.
 * Email, role and password cannot be changed here.
 */
export function updateUserProfile(
  id: string,
  profile: { fullName: string; bio: string },
  version: number,
): Promise<UserDetails> {
  return apiFetch<UserDetails>(`/api/admin/users/${encodeURIComponent(id)}/profile`, {
    method: 'PUT',
    body: { fullName: profile.fullName, bio: profile.bio, version },
  });
}

/** Suspends an Active author and signs them out everywhere. A 400 carries `errors.reason`; a stale `version` or an already suspended author is a 409. */
export function suspendUser(id: string, reason: string, version: number): Promise<UserDetails> {
  return apiFetch<UserDetails>(`/api/admin/users/${encodeURIComponent(id)}/suspend`, {
    method: 'POST',
    body: { reason, version },
  });
}

/** Makes a suspended author Active again; their old sessions stay revoked. A stale `version` or an author who is not suspended is a 409. */
export function reactivateUser(id: string, version: number): Promise<UserDetails> {
  return apiFetch<UserDetails>(`/api/admin/users/${encodeURIComponent(id)}/reactivate`, {
    method: 'POST',
    body: { version },
  });
}
