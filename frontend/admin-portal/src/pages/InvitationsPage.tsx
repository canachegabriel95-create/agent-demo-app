import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import {
  INVITATION_STATUSES,
  isInvitationStatus,
  listInvitations,
  type Invitation,
  type Page,
} from '../api/invitations';
import {
  CopyButton,
  EmptyState,
  ErrorState,
  FormField,
  LoadingState,
  Pagination,
} from '../components';
import { CreateInvitationDialog } from './CreateInvitationDialog';
import { describeError, formatDateTime } from './invitationFormat';
import styles from './InvitationsPage.module.css';
import { RevokeInvitationDialog } from './RevokeInvitationDialog';

export const EMAIL_DEBOUNCE_MS = 300;
export const CONFLICT_MESSAGE = 'This invitation changed. Refresh to see the latest status.';
export const NO_MATCHES_MESSAGE = 'No invitations match the filters';

interface Notice {
  kind: 'success' | 'warning';
  text: string;
}

interface Result {
  key: string;
  data?: Page<Invitation>;
  error?: unknown;
}

function parsePage(value: string | null): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1;
}

function PersonLine({ at, by }: { at: string; by: { fullName: string } | null }) {
  return (
    <>
      <span>{formatDateTime(at)}</span>
      {by && <span className={styles.secondaryText}> by {by.fullName}</span>}
    </>
  );
}

function UsedOrRevoked({ invitation }: { invitation: Invitation }) {
  if (invitation.usedAt) {
    return <PersonLine at={invitation.usedAt} by={invitation.usedBy} />;
  }

  if (invitation.revokedAt) {
    return <PersonLine at={invitation.revokedAt} by={invitation.revokedBy} />;
  }

  return <span className={styles.secondaryText}>—</span>;
}

export function InvitationsPage() {
  const [params, setParams] = useSearchParams();
  const statusParam = params.get('status');
  const status = isInvitationStatus(statusParam) ? statusParam : undefined;
  const email = (params.get('email') ?? '').trim();
  const page = parsePage(params.get('page'));

  const [emailInput, setEmailInput] = useState(email);
  const [syncedEmail, setSyncedEmail] = useState(email);
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();
  const [creating, setCreating] = useState(false);
  const [revoking, setRevoking] = useState<Invitation | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);

  // The address bar changed (back button, a dashboard link): show it in the input too.
  if (email !== syncedEmail) {
    setSyncedEmail(email);
    if (email !== emailInput.trim()) {
      setEmailInput(email);
    }
  }

  function updateParams(changes: Record<string, string | null>, replace: boolean) {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        for (const [name, value] of Object.entries(changes)) {
          if (value) {
            next.set(name, value);
          } else {
            next.delete(name);
          }
        }

        return next;
      },
      { replace },
    );
  }

  // Typing in the email filter updates the URL (and so the request) once typing pauses.
  useEffect(() => {
    const trimmed = emailInput.trim();
    if (trimmed === email) {
      return;
    }

    const timer = window.setTimeout(
      () => updateParams({ email: trimmed || null, page: null }, true),
      EMAIL_DEBOUNCE_MS,
    );
    return () => window.clearTimeout(timer);
    // updateParams only wraps setParams, which is stable for the lifetime of the page.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [emailInput, email]);

  const requestKey = `${status ?? ''}|${email}|${page}|${reload}`;
  useEffect(() => {
    const controller = new AbortController();
    listInvitations({ email: email || undefined, status, page }, controller.signal).then(
      (data) => setResult({ key: requestKey, data }),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setResult({ key: requestKey, error });
        }
      },
    );

    return () => controller.abort();
  }, [requestKey, email, status, page]);

  const current = result?.key === requestKey ? result : undefined;
  const data = current?.data ?? result?.data;
  const refreshing = result?.key !== requestKey;

  function refresh() {
    setReload((value) => value + 1);
  }

  function handleCreated() {
    // The new invitation is the newest one: show the first page of the unfiltered-by-page list.
    updateParams({ page: null }, true);
    refresh();
  }

  function handleRevoked(invitation: Invitation) {
    setRevoking(null);
    setNotice({ kind: 'success', text: `The invitation for ${invitation.email} was revoked.` });
    refresh();
  }

  function handleConflict() {
    setRevoking(null);
    setNotice({ kind: 'warning', text: CONFLICT_MESSAGE });
    refresh();
  }

  let body;
  if (current?.error) {
    const { message, correlationId } = describeError(current.error);
    body = (
      <ErrorState
        title="The invitations could not be loaded"
        message={
          current.error instanceof ApiError && current.error.status === 0
            ? (current.error.detail ?? message)
            : message
        }
        correlationId={correlationId}
        onRetry={refresh}
      />
    );
  } else if (!data) {
    body = <LoadingState message="Loading invitations…" />;
  } else if (data.items.length === 0) {
    body = (
      <>
        <EmptyState
          title={NO_MATCHES_MESSAGE}
          description={
            status || email
              ? 'Try a different email or status.'
              : 'Create the first invitation with the button above.'
          }
        />
        {data.totalCount > 0 && (
          <Pagination
            page={page}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            onPageChange={(next) => updateParams({ page: next > 1 ? String(next) : null }, false)}
          />
        )}
      </>
    );
  } else {
    body = (
      <>
        <div
          className={styles.tableWrap}
          role="region"
          aria-label="Invitations table"
          aria-busy={refreshing}
        >
          <table className={styles.table}>
            <caption className={styles.visuallyHidden}>Invitations, newest first</caption>
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Email</th>
                <th scope="col">Status</th>
                <th scope="col">Created</th>
                <th scope="col">Expires</th>
                <th scope="col">Used / Revoked</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((invitation) => (
                <tr key={invitation.id} data-testid={`invitation-row-${invitation.id}`}>
                  <td>
                    <code className={styles.code}>{invitation.code}</code>{' '}
                    <CopyButton
                      text={invitation.code}
                      label={`Copy code for ${invitation.email}`}
                      testId={`invitation-copy-${invitation.id}`}
                    />
                  </td>
                  <td>{invitation.email}</td>
                  <td>
                    <span className={styles.badge} data-status={invitation.status}>
                      {invitation.status}
                    </span>
                  </td>
                  <td>
                    <PersonLine at={invitation.createdAt} by={invitation.createdBy} />
                  </td>
                  <td>{formatDateTime(invitation.expiresAt)}</td>
                  <td>
                    <UsedOrRevoked invitation={invitation} />
                  </td>
                  <td>
                    {invitation.status === 'Available' && (
                      <button
                        type="button"
                        className={styles.rowAction}
                        onClick={() => setRevoking(invitation)}
                        aria-label={`Revoke invitation for ${invitation.email}`}
                        data-testid={`invitation-revoke-${invitation.id}`}
                      >
                        Revoke
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination
          page={data.page}
          pageSize={data.pageSize}
          totalCount={data.totalCount}
          onPageChange={(next) => updateParams({ page: next > 1 ? String(next) : null }, false)}
        />
      </>
    );
  }

  return (
    <section aria-labelledby="invitations-heading">
      <div className={styles.titleRow}>
        <h1 id="invitations-heading">Invitations</h1>
        <button
          type="button"
          className={styles.primary}
          onClick={() => setCreating(true)}
          data-testid="invitation-create-open"
        >
          New invitation
        </button>
      </div>

      <form
        className={styles.filters}
        role="search"
        aria-label="Filter invitations"
        onSubmit={(event) => event.preventDefault()}
      >
        <FormField
          label="Email"
          name="email"
          type="search"
          autoComplete="off"
          value={emailInput}
          onChange={setEmailInput}
        />
        <div className={styles.statusFilter}>
          <label htmlFor="invitation-status-filter" className={styles.filterLabel}>
            Status
          </label>
          <select
            id="invitation-status-filter"
            className={styles.select}
            value={status ?? ''}
            onChange={(event) =>
              updateParams({ status: event.target.value || null, page: null }, false)
            }
          >
            <option value="">All</option>
            {INVITATION_STATUSES.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </div>
        <button
          type="button"
          className={styles.secondary}
          onClick={refresh}
          data-testid="invitations-refresh"
        >
          Refresh
        </button>
      </form>

      {notice && (
        <p
          className={notice.kind === 'warning' ? styles.warning : styles.success}
          role={notice.kind === 'warning' ? 'alert' : 'status'}
          data-testid="invitations-notice"
        >
          {notice.text}
        </p>
      )}

      {body}

      {creating && (
        <CreateInvitationDialog onClose={() => setCreating(false)} onCreated={handleCreated} />
      )}
      {revoking && (
        <RevokeInvitationDialog
          invitation={revoking}
          onClose={() => setRevoking(null)}
          onRevoked={handleRevoked}
          onConflict={handleConflict}
        />
      )}
    </section>
  );
}
