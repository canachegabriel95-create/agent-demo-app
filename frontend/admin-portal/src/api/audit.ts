import { apiFetch } from './client';
import type { Page } from './invitations';

/** One audit event as the administrator sees it. Nothing here can be changed or deleted. */
export interface AuditEvent {
  id: string;
  occurredAt: string;
  action: string;
  /** `null` for events written by the system (e.g. the administrator provisioning script). */
  actor: { id: string; fullName: string; role: 'Administrator' | 'Author' } | null;
  entityType: string;
  entityId: string | null;
  /** The non-sensitive facts recorded with the action. */
  details: Record<string, unknown> | null;
  correlationId: string | null;
}

export interface AuditFilters {
  actions?: string[];
  actorId?: string;
  /** ISO 8601 with an offset; inclusive. */
  from?: string;
  /** ISO 8601 with an offset; exclusive. */
  to?: string;
  page?: number;
}

export function listAuditEvents(
  filters: AuditFilters,
  signal?: AbortSignal,
): Promise<Page<AuditEvent>> {
  const query = new URLSearchParams();
  for (const action of filters.actions ?? []) {
    query.append('action', action);
  }
  if (filters.actorId) {
    query.set('actorId', filters.actorId);
  }
  if (filters.from) {
    query.set('from', filters.from);
  }
  if (filters.to) {
    query.set('to', filters.to);
  }
  if (filters.page && filters.page > 1) {
    query.set('page', String(filters.page));
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : '';
  return apiFetch<Page<AuditEvent>>(`/api/admin/audit-events${suffix}`, { signal });
}

/** Every action name an event can have, for the filter. */
export function listAuditActions(signal?: AbortSignal): Promise<string[]> {
  return apiFetch<string[]>('/api/admin/audit-events/actions', { signal });
}
