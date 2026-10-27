import { useEffect, useState } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import type { Page } from '../api/invitations';
import { listReviewQueue, type ReviewQueueItem } from '../api/reviews';
import { isSortDirection, type SortDirection } from '../api/users';
import { EmptyState, ErrorState, LoadingState, Pagination } from '../components';
import { useCategories } from '../hooks/useCategories';
import { AuthorFilter } from './AuthorFilter';
import { describeError, formatDateTime } from './invitationFormat';
import styles from './ReviewQueuePage.module.css';

export const EMPTY_QUEUE_MESSAGE = 'No articles are waiting for review';

interface Result {
  key: string;
  data?: Page<ReviewQueueItem>;
  error?: unknown;
}

function parsePage(value: string | null): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1;
}

export function ReviewQueuePage() {
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  const categories = useCategories();
  const authorId = params.get('authorId') ?? '';
  const categoryId = params.get('categoryId') ?? '';
  const directionParam = params.get('direction');
  // The API default, and the one an absent parameter means, is oldest first.
  const direction: SortDirection = isSortDirection(directionParam) ? directionParam : 'asc';
  const page = parsePage(params.get('page'));

  const [authorName, setAuthorName] = useState<string>();
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();

  function updateParams(changes: Record<string, string | null>, replace = false) {
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

  const requestKey = `${authorId}|${categoryId}|${direction}|${page}|${reload}`;
  useEffect(() => {
    const controller = new AbortController();
    listReviewQueue(
      {
        authorId: authorId || undefined,
        categoryId: categoryId || undefined,
        direction: direction === 'asc' ? undefined : direction,
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
  }, [requestKey, authorId, categoryId, direction, page]);

  const current = result?.key === requestKey ? result : undefined;
  const data = current?.data ?? result?.data;
  const refreshing = result?.key !== requestKey;

  // An author chosen earlier is named; one that arrives by address is named by the first article that has it.
  const fromList = data?.items.find((item) => item.author.id === authorId)?.author.fullName;
  const shownAuthorName = authorName ?? fromList;

  function changePage(next: number) {
    updateParams({ page: next > 1 ? String(next) : null });
  }

  const filtered = Boolean(authorId || categoryId);

  let body;
  if (current?.error) {
    const { message, correlationId } = describeError(current.error);
    body = (
      <ErrorState
        title="The review queue could not be loaded"
        message={
          current.error instanceof ApiError && current.error.status === 0
            ? (current.error.detail ?? message)
            : message
        }
        correlationId={correlationId}
        onRetry={() => setReload((value) => value + 1)}
      />
    );
  } else if (!data) {
    body = <LoadingState message="Loading the review queue…" />;
  } else if (data.items.length === 0) {
    body = (
      <>
        <EmptyState
          title={EMPTY_QUEUE_MESSAGE}
          description={
            filtered
              ? 'No pending article matches the filters. Try another author or category.'
              : 'Articles appear here when authors submit them for review.'
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
          aria-label="Review queue table"
          aria-busy={refreshing}
        >
          <table className={styles.table}>
            <caption className={styles.visuallyHidden}>
              Articles waiting for review, {direction === 'asc' ? 'oldest' : 'newest'} submission
              first
            </caption>
            <thead>
              <tr>
                <th scope="col">Title</th>
                <th scope="col">Author</th>
                <th scope="col">Category</th>
                <th scope="col" aria-sort={direction === 'asc' ? 'ascending' : 'descending'}>
                  <button
                    type="button"
                    className={styles.sortButton}
                    onClick={() =>
                      updateParams({ direction: direction === 'asc' ? 'desc' : null, page: null })
                    }
                    data-testid="review-sort-submitted"
                  >
                    Submitted {direction === 'asc' ? '▲' : '▼'}
                    <span className={styles.visuallyHidden}>
                      {direction === 'asc'
                        ? ', sorted oldest first. Activate to show newest first'
                        : ', sorted newest first. Activate to show oldest first'}
                    </span>
                  </button>
                </th>
                <th scope="col">Version</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((item) => (
                <tr key={item.articleId} data-testid={`review-row-${item.articleId}`}>
                  <td>
                    {/* The page the administrator came from, so that a decision can lead back to the same view. */}
                    <Link
                      to={`/reviews/${item.articleId}`}
                      state={{ queueSearch: location.search }}
                    >
                      {item.title}
                    </Link>
                  </td>
                  <td>
                    {item.author.fullName}
                    {item.author.status === 'Suspended' && (
                      <>
                        {' '}
                        <span className={styles.badge} data-status="Suspended">
                          Suspended
                        </span>
                      </>
                    )}
                  </td>
                  <td>{item.category.name}</td>
                  <td>{formatDateTime(item.submittedAt)}</td>
                  <td>{item.submissionVersion}</td>
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
    <section aria-labelledby="reviews-heading">
      <div className={styles.titleRow}>
        <h1 id="reviews-heading">Review queue</h1>
      </div>

      <form
        className={styles.filters}
        role="search"
        aria-label="Filter the review queue"
        onSubmit={(event) => event.preventDefault()}
      >
        <AuthorFilter
          authorId={authorId}
          authorName={shownAuthorName}
          onSelect={(author) => {
            setAuthorName(author.fullName);
            updateParams({ authorId: author.id, page: null });
          }}
          onClear={() => {
            setAuthorName(undefined);
            updateParams({ authorId: null, page: null });
          }}
        />
        <div className={styles.filter}>
          <label htmlFor="review-category-filter" className={styles.filterLabel}>
            Category
          </label>
          <select
            id="review-category-filter"
            className={styles.select}
            value={categoryId}
            onChange={(event) =>
              updateParams({ categoryId: event.target.value || null, page: null })
            }
            disabled={categories.status === 'loading'}
          >
            <option value="">All</option>
            {categories.categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </div>
        <button
          type="button"
          className={styles.secondary}
          onClick={() => setReload((value) => value + 1)}
          data-testid="review-refresh"
        >
          Refresh
        </button>
      </form>

      {body}
    </section>
  );
}
