import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import {
  USER_STATUSES,
  isSortDirection,
  isUserStatus,
  listUsers,
  type SortDirection,
  type UserListItem,
} from '../api/users';
import type { Page } from '../api/invitations';
import { EmptyState, ErrorState, FormField, LoadingState, Pagination } from '../components';
import { describeError, formatDate } from './invitationFormat';
import styles from './UsersPage.module.css';

export const SEARCH_DEBOUNCE_MS = 300;
export const NO_MATCHES_MESSAGE = 'No authors match the filters';

interface Result {
  key: string;
  data?: Page<UserListItem>;
  error?: unknown;
}

function parsePage(value: string | null): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1;
}

export function UsersPage() {
  const [params, setParams] = useSearchParams();
  const statusParam = params.get('status');
  const status = isUserStatus(statusParam) ? statusParam : undefined;
  const search = (params.get('search') ?? '').trim();
  const directionParam = params.get('direction');
  // The API default, and the one an absent parameter means, is newest first.
  const direction: SortDirection = isSortDirection(directionParam) ? directionParam : 'desc';
  const page = parsePage(params.get('page'));

  const [searchInput, setSearchInput] = useState(search);
  const [syncedSearch, setSyncedSearch] = useState(search);
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();

  // The address bar changed (back button, a dashboard link): show it in the input too.
  if (search !== syncedSearch) {
    setSyncedSearch(search);
    if (search !== searchInput.trim()) {
      setSearchInput(search);
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

  // Typing in the search box updates the URL (and so the request) once typing pauses.
  useEffect(() => {
    const trimmed = searchInput.trim();
    if (trimmed === search) {
      return;
    }

    const timer = window.setTimeout(
      () => updateParams({ search: trimmed || null, page: null }, true),
      SEARCH_DEBOUNCE_MS,
    );
    return () => window.clearTimeout(timer);
    // updateParams only wraps setParams, which is stable for the lifetime of the page.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchInput, search]);

  const requestKey = `${search}|${status ?? ''}|${direction}|${page}|${reload}`;
  useEffect(() => {
    const controller = new AbortController();
    listUsers(
      {
        search: search || undefined,
        status,
        direction: direction === 'desc' ? undefined : direction,
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
  }, [requestKey, search, status, direction, page]);

  const current = result?.key === requestKey ? result : undefined;
  const data = current?.data ?? result?.data;
  const refreshing = result?.key !== requestKey;

  function refresh() {
    setReload((value) => value + 1);
  }

  function changePage(next: number) {
    updateParams({ page: next > 1 ? String(next) : null }, false);
  }

  let body;
  if (current?.error) {
    const { message, correlationId } = describeError(current.error);
    body = (
      <ErrorState
        title="The authors could not be loaded"
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
    body = <LoadingState message="Loading authors…" />;
  } else if (data.items.length === 0) {
    body = (
      <>
        <EmptyState
          title={NO_MATCHES_MESSAGE}
          description={
            status || search
              ? 'Try a different name, email or status.'
              : 'Authors appear here after they register with an invitation.'
          }
        />
        {data.totalCount > 0 && (
          <Pagination
            page={page}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            onPageChange={changePage}
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
          aria-label="Authors table"
          aria-busy={refreshing}
        >
          <table className={styles.table}>
            <caption className={styles.visuallyHidden}>
              Authors, {direction === 'desc' ? 'newest' : 'oldest'} registration first
            </caption>
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Email</th>
                <th scope="col">Status</th>
                <th scope="col" aria-sort={direction === 'asc' ? 'ascending' : 'descending'}>
                  <button
                    type="button"
                    className={styles.sortButton}
                    onClick={() =>
                      updateParams(
                        { direction: direction === 'desc' ? 'asc' : null, page: null },
                        false,
                      )
                    }
                    data-testid="users-sort-registered"
                  >
                    Registered {direction === 'asc' ? '▲' : '▼'}
                    <span className={styles.visuallyHidden}>
                      {direction === 'asc'
                        ? ', sorted oldest first. Activate to show newest first'
                        : ', sorted newest first. Activate to show oldest first'}
                    </span>
                  </button>
                </th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((user) => (
                <tr key={user.id} data-testid={`user-row-${user.id}`}>
                  <td>
                    <Link to={`/users/${user.id}`}>{user.fullName}</Link>
                  </td>
                  <td>{user.email}</td>
                  <td>
                    <span className={styles.badge} data-status={user.status}>
                      {user.status}
                    </span>
                  </td>
                  <td>{formatDate(user.registeredAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination
          page={data.page}
          pageSize={data.pageSize}
          totalCount={data.totalCount}
          onPageChange={changePage}
        />
      </>
    );
  }

  return (
    <section aria-labelledby="users-heading">
      <div className={styles.titleRow}>
        <h1 id="users-heading">Authors</h1>
      </div>

      <form
        className={styles.filters}
        role="search"
        aria-label="Filter authors"
        onSubmit={(event) => event.preventDefault()}
      >
        <FormField
          label="Search"
          name="search"
          type="search"
          autoComplete="off"
          hint="Part of a name or an email"
          value={searchInput}
          onChange={setSearchInput}
        />
        <div className={styles.statusFilter}>
          <label htmlFor="user-status-filter" className={styles.filterLabel}>
            Status
          </label>
          <select
            id="user-status-filter"
            className={styles.select}
            value={status ?? ''}
            onChange={(event) =>
              updateParams({ status: event.target.value || null, page: null }, false)
            }
          >
            <option value="">All</option>
            {USER_STATUSES.map((value) => (
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
          data-testid="users-refresh"
        >
          Refresh
        </button>
      </form>

      {body}
    </section>
  );
}
