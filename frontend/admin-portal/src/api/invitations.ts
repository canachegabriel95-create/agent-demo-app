import { apiFetch } from './client';

export type InvitationStatus = 'Available' | 'Used' | 'Expired' | 'Revoked';

export const INVITATION_STATUSES: readonly InvitationStatus[] = [
  'Available',
  'Used',
  'Expired',
  'Revoked',
];

export function isInvitationStatus(value: string | null): value is InvitationStatus {
  return INVITATION_STATUSES.some((status) => status === value);
}

export interface PersonRef {
  id: string;
  fullName: string;
}

export interface Invitation {
  id: string;
  code: string;
  email: string;
  status: InvitationStatus;
  createdAt: string;
  createdBy: PersonRef;
  expiresAt: string;
  revokedAt: string | null;
  revokedBy: PersonRef | null;
  usedAt: string | null;
  usedBy: PersonRef | null;
  /** Concurrency token: send it back when revoking. */
  version: number;
}

export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface InvitationFilters {
  email?: string;
  status?: InvitationStatus;
  page?: number;
}

export function listInvitations(
  filters: InvitationFilters,
  signal?: AbortSignal,
): Promise<Page<Invitation>> {
  const query = new URLSearchParams();
  if (filters.email) {
    query.set('email', filters.email);
  }
  if (filters.status) {
    query.set('status', filters.status);
  }
  if (filters.page && filters.page > 1) {
    query.set('page', String(filters.page));
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : '';
  return apiFetch<Page<Invitation>>(`/api/admin/invitations${suffix}`, { signal });
}

/** Creates an invitation. A 400 carries `errors.email` / `errors.validForDays`; an existing user's email is a 409. */
export function createInvitation(email: string, validForDays: number): Promise<Invitation> {
  return apiFetch<Invitation>('/api/admin/invitations', {
    method: 'POST',
    body: { email, validForDays },
  });
}

/** Revokes an Available invitation. A stale `version` or a status that changed meanwhile is a 409. */
export function revokeInvitation(id: string, version: number): Promise<Invitation> {
  return apiFetch<Invitation>(`/api/admin/invitations/${encodeURIComponent(id)}/revoke`, {
    method: 'POST',
    body: { version },
  });
}
