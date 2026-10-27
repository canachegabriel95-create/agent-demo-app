import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { listAuditActions, listAuditEvents, type AuditEvent } from '../api/audit';
import type { Page } from '../api/invitations';
import {
  CopyButton,
  EmptyState,
  ErrorState,
  FormField,
  LoadingState,
  Pagination,
} from '../components';
import { describeError, formatDateTime } from './invitationFormat';
import styles from './AuditPage.module.css';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE = /^\d{4}-\d{2}-\d{2}$/;

function parsePage(value: string | null): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1;
}

/** The start of a calendar day in the administrator's time zone, as an instant the API understands. */
export function startOfDay(date: string): string {
  return new Date(`${date}T00:00:00`).toISOString();
}

/** The "to" date is inclusive for the administrator; the API's `to` is exclusive, so it is the start of the next day. */
export function startOfNextDay(date: string): string {
  const next = new Date(`${date}T00:00:00`);
  next.setDate(next.getDate() + 1);
  return next.toISOString();
}

/** The details as short lines of text: `key: value`. Nested values are shown as JSON text. */
export function detailLines(details: Record<string, unknown> | null): string[] {
  if (!details) {
    return [];
  }

  return Object.entries(details).map(([key, value]) => {
    let text: string;
    if (value === null || value === undefined) {
      text = '—';
    } else if (
      Array.isArray(value) &&
      value.every((item) => typeof item !== 'object' || item === null)
    ) {
      text = value.join(', ');
    } else if (typeof value === 'object') {
      text = JSON.stringify(value);
    } else {
      text = String(value);
    }

    return `${key}: ${text}`;
  });
}

function entityLink(event: AuditEvent): string | undefined {
  if (!event.entityId) {
    return undefined;
  }

  if (event.entityType === 'User') {
    return `/users/${event.entityId}`;
  }

  return event.entityType === 'Article' ? `/reviews/${event.entityId}` : undefined;
}

interface Result {
  key: string;
  data?: Page<AuditEvent>;
  error?: unknown;
}

export function AuditPage() {
  const [params, setParams] = useSearchParams();
  const selectedActions = params.getAll('action');
  const actorParam = params.get('actorId') ?? '';
  const actorId = GUID.test(actorParam) ? actorParam : '';
  const fromParam = params.get('from') ?? '';
  const toParam = params.get('to') ?? '';
  const from = DATE.test(fromParam) ? fromParam : '';
  const to = DATE.test(toParam) ? toParam : '';
  const page = parsePage(params.get('page'));
  const rangeInvalid = Boolean(from && to && from > to);

  const [actions, setActions] = useState<string[]>([]);
  const [actorInput, setActorInput] = useState(actorId);
  const [syncedActor, setSyncedActor] = useState(actorId);
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();

  // The address bar changed (back button, a dashboard link): show it in the input too.
  if (actorId !== syncedActor) {
    setSyncedActor(actorId);
    setActorInput(actorId);
  }

  const actorTyped = actorInput.trim();
  const actorInvalid = actorTyped !== '' && !GUID.test(actorTyped);

  function updateParams(changes: Record<string, string | string[] | null>, replace = false) {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        for (const [name, value] of Object.entries(changes)) {
          next.delete(name);
          for (const item of Array.isArray(value) ? value : value ? [value] : []) {
            next.append(name, item);
          }
        }

        next.delete('page');
        return next;
      },
      { replace },
    );
  }

  useEffect(() => {
    const controller = new AbortController();
    listAuditActions(controller.signal).then(setActions, () => {
      // Without the list the action filter is simply empty; the table still works.
    });
    return () => controller.abort();
  }, []);

  // A valid actor id (or an emptied field) goes to the address; a half-typed one waits.
  useEffect(() => {
    if (actorTyped === actorId || actorInvalid) {
      return;
    }

    updateParams({ actorId: actorTyped || null }, true);
    // updateParams only wraps setParams, which is stable for the lifetime of the page.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [actorTyped, actorId, actorInvalid]);

  const actionsKey = selectedActions.join(',');
  const requestKey = `${actionsKey}|${actorId}|${from}|${to}|${page}|${reload}`;
  useEffect(() => {
    if (rangeInvalid) {
      return;
    }

    const controller = new AbortController();
    listAuditEvents(
      {
        actions: actionsKey ? actionsKey.split(',') : undefined,
        actorId: actorId || undefined,
        from: from ? startOfDay(from) : undefined,
        to: to ? startOfNextDay(to) : undefined,
        page,
      },
      controller.signal,
    ).then(
      (data) => setResult({ key: requestKey, data }),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setResult({ key: requestKey, error });
        }
      },
    );

    return () => controller.abort();
  }, [requestKey, actionsKey, actorId, from, to, page, rangeInvalid]);

  const current = result?.key === requestKey ? result : undefined;
  const data = current?.data ?? result?.data;
  const filtered = Boolean(selectedActions.length || actorId || from || to);

  function toggleAction(action: string, checked: boolean) {
    updateParams({
      action: checked ? [...selectedActions, action] : selectedActions.filter((a) => a !== action),
    });
  }

  function clearFilters() {
    // The actor input follows the address, so it empties when the address does.
    setParams({}, { replace: false });
  }

  let body;
  if (rangeInvalid) {
    body = null;
  } else if (current?.error) {
    const { message, correlationId } = describeError(current.error);
    body = (
      <ErrorState
        title="The audit history could not be loaded"
        message={message}
        correlationId={correlationId}
        onRetry={() => setReload((value) => value + 1)}
      />
    );
  } else if (!data) {
    body = <LoadingState message="Loading the audit history…" />;
  } else if (data.items.length === 0) {
    body = (
      <EmptyState
        title={filtered ? 'No audit events match the filters' : 'No audit events yet'}
        description={
          filtered
            ? 'Try other actions or a wider date range.'
            : 'Events appear here as people use the platform.'
        }
        action={
          filtered ? (
            <button
              type="button"
              className={styles.secondary}
              onClick={clearFilters}
              data-testid="audit-clear-filters"
            >
              Clear filters
            </button>
          ) : undefined
        }
      />
    );
  } else {
    body = (
      <>
        <div className={styles.tableWrap}>
          <table className={styles.table} aria-label="Audit events" aria-busy={!current}>
            <thead>
              <tr>
                <th scope="col">Time</th>
                <th scope="col">Action</th>
                <th scope="col">Actor</th>
                <th scope="col">Entity</th>
                <th scope="col">Details</th>
                <th scope="col">Correlation id</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((event) => {
                const link = entityLink(event);
                const lines = detailLines(event.details);
                return (
                  <tr key={event.id} data-testid="audit-row">
                    <td>
                      <time dateTime={event.occurredAt} title={`${event.occurredAt} (UTC)`}>
                        {formatDateTime(event.occurredAt)}
                      </time>
                    </td>
                    <td>{event.action}</td>
                    <td>
                      {event.actor ? (
                        <>
                          <span data-testid="audit-actor">{event.actor.fullName}</span>{' '}
                          <button
                            type="button"
                            className={styles.link}
                            aria-label={`Only events by ${event.actor.fullName}`}
                            onClick={() => updateParams({ actorId: event.actor!.id })}
                          >
                            filter
                          </button>
                        </>
                      ) : (
                        <span data-testid="audit-actor">System</span>
                      )}
                    </td>
                    <td>
                      {event.entityType}{' '}
                      {event.entityId &&
                        (link ? (
                          <Link to={link} title={event.entityId}>
                            {event.entityId.slice(-8)}
                          </Link>
                        ) : (
                          <span title={event.entityId}>{event.entityId.slice(-8)}</span>
                        ))}
                    </td>
                    <td>
                      {lines.length > 0 && (
                        <ul className={styles.details} data-testid="audit-details">
                          {lines.map((line) => (
                            <li key={line}>{line}</li>
                          ))}
                        </ul>
                      )}
                    </td>
                    <td>
                      {event.correlationId && (
                        <>
                          <code className={styles.correlation}>
                            {event.correlationId.slice(0, 8)}
                          </code>
                          <CopyButton
                            text={event.correlationId}
                            label={`Copy correlation id ${event.correlationId.slice(0, 8)}`}
                          />
                        </>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <Pagination
          page={page}
          pageSize={data.pageSize}
          totalCount={data.totalCount}
          onPageChange={(next) => {
            setParams((previous) => {
              const nextParams = new URLSearchParams(previous);
              if (next > 1) {
                nextParams.set('page', String(next));
              } else {
                nextParams.delete('page');
              }

              return nextParams;
            });
          }}
        />
      </>
    );
  }

  return (
    <section aria-labelledby="audit-heading">
      <h1 id="audit-heading">Audit history</h1>
      <p className={styles.lead}>Who did what, and when. Events cannot be changed or deleted.</p>

      <form
        className={styles.filters}
        role="search"
        aria-label="Filter audit events"
        onSubmit={(e) => e.preventDefault()}
      >
        <fieldset className={styles.actions}>
          <legend>Actions</legend>
          {actions.map((action) => (
            <label key={action} className={styles.check}>
              <input
                type="checkbox"
                checked={selectedActions.includes(action)}
                onChange={(event) => toggleAction(action, event.target.checked)}
              />
              {action}
            </label>
          ))}
        </fieldset>
        <FormField
          label="Actor id"
          name="actorId"
          autoComplete="off"
          hint="Use “filter” next to a name, or paste an id"
          error={actorInvalid ? 'Enter a valid id.' : undefined}
          value={actorInput}
          onChange={setActorInput}
        />
        <div className={styles.filter}>
          <label htmlFor="audit-from" className={styles.filterLabel}>
            From
          </label>
          <input
            id="audit-from"
            type="date"
            className={styles.date}
            value={from}
            onChange={(event) => updateParams({ from: event.target.value || null })}
          />
        </div>
        <div className={styles.filter}>
          <label htmlFor="audit-to" className={styles.filterLabel}>
            To
          </label>
          <input
            id="audit-to"
            type="date"
            className={styles.date}
            value={to}
            onChange={(event) => updateParams({ to: event.target.value || null })}
          />
        </div>
        <button
          type="button"
          className={styles.secondary}
          onClick={clearFilters}
          data-testid="audit-clear"
        >
          Clear filters
        </button>
      </form>

      {rangeInvalid && (
        <p role="alert" className={styles.alert} data-testid="audit-range-error">
          The end date must not be before the start date.
        </p>
      )}

      {body}
    </section>
  );
}
