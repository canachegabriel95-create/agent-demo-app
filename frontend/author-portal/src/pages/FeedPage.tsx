import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { listFeed, type FeedPage as FeedData } from '../api/feed';
import { EmptyState, ErrorState, FormField, LoadingState, Pagination } from '../components';
import { useCategories } from '../hooks/useCategories';
import styles from './FeedPage.module.css';

export const SEARCH_DEBOUNCE_MS = 300;
export const NO_ARTICLES_TITLE = 'No published articles yet';
export const NO_MATCHES_TITLE = 'No articles match your search';

const dateFormat = new Intl.DateTimeFormat('en', { dateStyle: 'medium' });

function parsePage(value: string | null): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1;
}

interface Result {
  key: string;
  data?: FeedData;
  error?: unknown;
}

/** The published articles of all authors. Everything shown is plain text. */
export function FeedPage() {
  const [params, setParams] = useSearchParams();
  const categories = useCategories();
  const search = (params.get('search') ?? '').trim();
  const categoryId = params.get('categoryId') ?? '';
  const page = parsePage(params.get('page'));

  const [searchInput, setSearchInput] = useState(search);
  const [syncedSearch, setSyncedSearch] = useState(search);
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();

  // The address bar changed (back button): show it in the input too.
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

  const requestKey = `${search}|${categoryId}|${page}|${reload}`;
  useEffect(() => {
    const controller = new AbortController();
    listFeed(
      { search: search || undefined, categoryId: categoryId || undefined, page },
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
  }, [requestKey, search, categoryId, page]);

  const current = result?.key === requestKey ? result : undefined;
  const data = current?.data ?? result?.data;
  const filtered = Boolean(search || categoryId);

  function clearFilters() {
    setSearchInput('');
    setParams({}, { replace: false });
  }

  function changePage(next: number) {
    updateParams({ page: next > 1 ? String(next) : null }, false);
  }

  let body;
  if (current?.error) {
    const error = current.error;
    body = (
      <ErrorState
        title="The feed could not be loaded"
        message={
          error instanceof ApiError
            ? (error.detail ?? error.title)
            : 'Something went wrong. Please try again.'
        }
        correlationId={error instanceof ApiError ? error.correlationId : undefined}
        onRetry={() => setReload((value) => value + 1)}
      />
    );
  } else if (!data) {
    body = <LoadingState message="Loading the feed…" />;
  } else if (data.items.length === 0) {
    body = filtered ? (
      <EmptyState
        title={NO_MATCHES_TITLE}
        description="Try another search or category."
        action={
          <button
            type="button"
            className={styles.secondary}
            onClick={clearFilters}
            data-testid="feed-clear-filters"
          >
            Clear filters
          </button>
        }
      />
    ) : data.totalCount > 0 ? (
      <>
        <EmptyState title="There are no articles on this page" />
        <Pagination
          page={page}
          pageSize={data.pageSize}
          totalCount={data.totalCount}
          onPageChange={changePage}
        />
      </>
    ) : (
      <EmptyState
        title={NO_ARTICLES_TITLE}
        description="Articles appear here once an administrator approves them."
      />
    );
  } else {
    body = (
      <>
        <ul className={styles.list} aria-busy={!current}>
          {data.items.map((article) => (
            <li key={article.id} className={styles.card} data-testid={`feed-card-${article.id}`}>
              <h2 className={styles.title}>
                <Link to={`/feed/${article.id}`}>{article.title}</Link>
              </h2>
              <p className={styles.meta}>
                {article.category?.name ?? 'No category'} · {article.authorDisplayName} ·{' '}
                {dateFormat.format(new Date(article.publishedAt))}
              </p>
              <p className={styles.summary}>{article.summary}</p>
            </li>
          ))}
        </ul>
        <Pagination
          page={page}
          pageSize={data.pageSize}
          totalCount={data.totalCount}
          onPageChange={changePage}
        />
      </>
    );
  }

  return (
    <section aria-labelledby="feed-heading">
      <h1 id="feed-heading">Feed</h1>
      <p className={styles.lead}>Articles published by all authors.</p>

      <form
        className={styles.filters}
        role="search"
        aria-label="Filter the feed"
        onSubmit={(event) => event.preventDefault()}
      >
        <FormField
          label="Search"
          name="search"
          type="search"
          autoComplete="off"
          hint="Part of the title"
          testId="feed-search"
          value={searchInput}
          onChange={setSearchInput}
        />
        <div className={styles.filter}>
          <label htmlFor="feed-category" className={styles.filterLabel}>
            Category
          </label>
          <select
            id="feed-category"
            data-testid="feed-category"
            className={styles.select}
            value={categoryId}
            onChange={(event) =>
              updateParams({ categoryId: event.target.value || null, page: null }, false)
            }
          >
            <option value="">All</option>
            {categories.categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </div>
      </form>

      {body}
    </section>
  );
}
