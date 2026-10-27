import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { deleteArticle, submitArticle, withdrawArticle, type ArticleStatus } from '../api/articles';
import { ApiError } from '../api/client';
import {
  ARTICLE_STATUSES,
  isArticleSort,
  isArticleStatus,
  listMyArticles,
  type ArticlePage,
  type MyArticleItem,
  type MyArticleSort,
} from '../api/myArticles';
import {
  ConfirmDialog,
  EmptyState,
  ErrorState,
  FormField,
  LoadingState,
  Pagination,
} from '../components';
import { useCategories } from '../hooks/useCategories';
import { STATUS_LABELS } from './ArticlePage';
import styles from './MyArticlesPage.module.css';

export const SEARCH_DEBOUNCE_MS = 300;
export const NO_ARTICLES_TITLE = 'You have no articles yet';
export const NO_MATCHES_TITLE = 'No articles match your filters';
export const CHANGED_MESSAGE =
  'The article changed in the meantime. The list shows its current state.';

const SORT_LABELS: Record<MyArticleSort, string> = {
  updatedAt: 'Last updated',
  submittedAt: 'Submitted date',
  title: 'Title',
};

const dateTime = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' });

/** The date that matters for the status: when it was last touched, sent in, or published. */
export function relevantDate(article: MyArticleItem): string {
  const format = (value: string) => dateTime.format(new Date(value));
  if (article.status === 'PendingReview') {
    return `Submitted ${format(article.submittedAt ?? article.updatedAt)}`;
  }
  if (article.status === 'Published') {
    return `Published ${format(article.publishedAt ?? article.updatedAt)}`;
  }

  return `Updated ${format(article.updatedAt)}`;
}

function parsePage(value: string | null): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1;
}

interface Result {
  key: string;
  data?: ArticlePage;
  error?: unknown;
}

type Action = 'delete' | 'submit' | 'withdraw';

function failureText(error: unknown): { message: string; correlationId?: string } {
  if (error instanceof ApiError) {
    return { message: error.detail ?? error.title, correlationId: error.correlationId };
  }

  return { message: 'Something went wrong. Please try again.' };
}

export function MyArticlesPage() {
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const categories = useCategories();
  const statusParam = params.get('status');
  const status = isArticleStatus(statusParam) ? statusParam : undefined;
  const search = (params.get('search') ?? '').trim();
  const categoryId = params.get('categoryId') ?? '';
  const sortParam = params.get('sort');
  const sort: MyArticleSort = isArticleSort(sortParam) ? sortParam : 'updatedAt';
  const page = parsePage(params.get('page'));

  // Another page (deleting a draft in the viewer) sends the author here with a message to show once.
  const [flash, setFlash] = useState(
    () => (location.state as { message?: string } | null)?.message ?? '',
  );
  const [notice, setNotice] = useState('');
  const [searchInput, setSearchInput] = useState(search);
  const [syncedSearch, setSyncedSearch] = useState(search);
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();
  const [dialog, setDialog] = useState<{ action: Action; article: MyArticleItem }>();
  const [feedbackOpen, setFeedbackOpen] = useState<ReadonlySet<string>>(new Set());

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

  useEffect(() => {
    if (location.state) {
      // The message is read once; the entry is replaced so a reload does not show it again.
      navigate(
        { pathname: location.pathname, search: location.search },
        { replace: true, state: null },
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

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

  const requestKey = `${search}|${status ?? ''}|${categoryId}|${sort}|${page}|${reload}`;
  useEffect(() => {
    const controller = new AbortController();
    listMyArticles(
      {
        search: search || undefined,
        status,
        categoryId: categoryId || undefined,
        sort: sort === 'updatedAt' ? undefined : sort,
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
  }, [requestKey, search, status, categoryId, sort, page]);

  const current = result?.key === requestKey ? result : undefined;
  const data = current?.data ?? result?.data;
  const filtered = Boolean(search || status || categoryId);

  function clearFilters() {
    setSearchInput('');
    setParams({}, { replace: false });
  }

  function toggleFeedback(id: string) {
    setFeedbackOpen((previous) => {
      const next = new Set(previous);
      if (!next.delete(id)) {
        next.add(id);
      }

      return next;
    });
  }

  async function confirm(): Promise<string | undefined> {
    if (!dialog) {
      return undefined;
    }

    const { action, article } = dialog;
    try {
      if (action === 'delete') {
        await deleteArticle(article.id, article.version);
        setFlash(`Draft “${article.title}” deleted.`);
      } else if (action === 'submit') {
        await submitArticle(article.id, article.version);
        setFlash(`“${article.title}” submitted for review.`);
      } else {
        await withdrawArticle(article.id, article.version);
        setFlash(`“${article.title}” withdrawn. It is a draft again.`);
      }
      setNotice('');
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setFlash('');
        setNotice(CHANGED_MESSAGE);
      } else if (error instanceof ApiError && error.status === 400 && action === 'submit') {
        // The submission rules are stricter than the draft rules: name what is missing and keep the dialog open.
        const messages = Object.entries(error.errors)
          .filter(([field]) => field !== 'version')
          .flatMap(([, texts]) => texts);
        return `This article cannot be submitted yet: ${(messages.length > 0 ? messages : [failureText(error).message]).join(' ')} Open the article to fix it.`;
      } else {
        const { message, correlationId } = failureText(error);
        return correlationId ? `${message} Reference: ${correlationId}` : message;
      }
    }

    setDialog(undefined);
    setReload((value) => value + 1);
    return undefined;
  }

  let body;
  if (current?.error) {
    const { message, correlationId } = failureText(current.error);
    body = (
      <ErrorState
        title="Your articles could not be loaded"
        message={message}
        correlationId={correlationId}
        onRetry={() => setReload((value) => value + 1)}
      />
    );
  } else if (!data) {
    body = <LoadingState message="Loading your articles…" />;
  } else if (data.items.length === 0) {
    body = filtered ? (
      <EmptyState
        title={NO_MATCHES_TITLE}
        description="Try another search or remove a filter."
        action={
          <button
            type="button"
            className={styles.secondary}
            onClick={clearFilters}
            data-testid="articles-clear-filters"
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
          onPageChange={(next) => updateParams({ page: next > 1 ? String(next) : null }, false)}
        />
      </>
    ) : (
      <EmptyState
        title={NO_ARTICLES_TITLE}
        description="Write your first article and submit it for review."
        action={
          <Link to="/articles/new" className={styles.primary} data-testid="articles-empty-new">
            New article
          </Link>
        }
      />
    );
  } else {
    body = (
      <>
        <ul className={styles.list} aria-busy={!current}>
          {data.items.map((article) => {
            const can = (action: string) => article.allowedActions.includes(action);
            const open = feedbackOpen.has(article.id);
            return (
              <li key={article.id} className={styles.row} data-testid="article-row">
                <div className={styles.main}>
                  <Link to={`/articles/${article.id}`} className={styles.title}>
                    {article.title}
                  </Link>
                  <span
                    className={styles.badge}
                    data-status={article.status}
                    data-testid="article-row-status"
                  >
                    {STATUS_LABELS[article.status]}
                  </span>
                </div>
                <p className={styles.meta}>
                  <span data-testid="article-row-category">
                    {article.category?.name ?? 'No category'}
                  </span>
                  {' · '}
                  <span data-testid="article-row-date">{relevantDate(article)}</span>
                </p>
                <div className={styles.actions}>
                  {can('edit') && (
                    <Link
                      to={`/articles/${article.id}/edit`}
                      className={styles.link}
                      aria-label={`Edit ${article.title}`}
                    >
                      Edit
                    </Link>
                  )}
                  {can('submit') && (
                    <button
                      type="button"
                      className={styles.secondary}
                      onClick={() => setDialog({ action: 'submit', article })}
                      data-testid="article-row-submit"
                      aria-label={`${article.status === 'Rejected' ? 'Resubmit' : 'Submit'} ${article.title}`}
                    >
                      {article.status === 'Rejected' ? 'Resubmit' : 'Submit'}
                    </button>
                  )}
                  {can('withdraw') && (
                    <button
                      type="button"
                      className={styles.secondary}
                      onClick={() => setDialog({ action: 'withdraw', article })}
                      data-testid="article-row-withdraw"
                      aria-label={`Withdraw ${article.title}`}
                    >
                      Withdraw
                    </button>
                  )}
                  {can('delete') && (
                    <button
                      type="button"
                      className={styles.danger}
                      onClick={() => setDialog({ action: 'delete', article })}
                      data-testid="article-row-delete"
                      aria-label={`Delete ${article.title}`}
                    >
                      Delete
                    </button>
                  )}
                  {article.status === 'Rejected' && (
                    <button
                      type="button"
                      className={styles.link}
                      aria-expanded={open}
                      onClick={() => toggleFeedback(article.id)}
                      data-testid="article-row-feedback-toggle"
                      aria-label={`See feedback for ${article.title}`}
                    >
                      See feedback
                    </button>
                  )}
                </div>
                {article.status === 'Rejected' && open && (
                  <p className={styles.feedback} data-testid="article-row-feedback">
                    {article.latestRejectionReason ?? 'No reason was given.'}
                  </p>
                )}
              </li>
            );
          })}
        </ul>
        <Pagination
          page={page}
          pageSize={data.pageSize}
          totalCount={data.totalCount}
          onPageChange={(next) => updateParams({ page: next > 1 ? String(next) : null }, false)}
        />
      </>
    );
  }

  return (
    <section aria-labelledby="articles-heading">
      <div className={styles.titleRow}>
        <h1 id="articles-heading">My Articles</h1>
        <Link to="/articles/new" className={styles.primary} data-testid="articles-new">
          New article
        </Link>
      </div>

      <div role="status" data-testid="flash-message">
        {flash && <p className={styles.success}>{flash}</p>}
      </div>
      {notice && (
        <p role="alert" className={styles.alert} data-testid="articles-notice">
          {notice}
        </p>
      )}

      <form
        className={styles.filters}
        role="search"
        aria-label="Filter articles"
        onSubmit={(event) => event.preventDefault()}
      >
        <FormField
          label="Search"
          name="search"
          type="search"
          autoComplete="off"
          hint="Part of the title"
          value={searchInput}
          onChange={setSearchInput}
        />
        <div className={styles.filter}>
          <label htmlFor="articles-status-filter" className={styles.filterLabel}>
            Status
          </label>
          <select
            id="articles-status-filter"
            className={styles.select}
            value={status ?? ''}
            onChange={(event) =>
              updateParams({ status: event.target.value || null, page: null }, false)
            }
          >
            <option value="">All</option>
            {ARTICLE_STATUSES.map((value: ArticleStatus) => (
              <option key={value} value={value}>
                {STATUS_LABELS[value]}
              </option>
            ))}
          </select>
        </div>
        <div className={styles.filter}>
          <label htmlFor="articles-category-filter" className={styles.filterLabel}>
            Category
          </label>
          <select
            id="articles-category-filter"
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
        <div className={styles.filter}>
          <label htmlFor="articles-sort" className={styles.filterLabel}>
            Sort by
          </label>
          <select
            id="articles-sort"
            className={styles.select}
            value={sort}
            onChange={(event) =>
              updateParams(
                {
                  sort: event.target.value === 'updatedAt' ? null : event.target.value,
                  page: null,
                },
                false,
              )
            }
          >
            {(Object.keys(SORT_LABELS) as MyArticleSort[]).map((value) => (
              <option key={value} value={value}>
                {SORT_LABELS[value]}
              </option>
            ))}
          </select>
        </div>
      </form>

      {body}

      {dialog?.action === 'delete' && (
        <ConfirmDialog
          title="Delete draft"
          confirmLabel="Delete"
          busyLabel="Deleting…"
          confirmTestId="article-row-delete-confirm"
          variant="danger"
          onClose={() => setDialog(undefined)}
          onConfirm={confirm}
        >
          <p>Delete draft “{dialog.article.title}”? This cannot be undone.</p>
        </ConfirmDialog>
      )}
      {dialog?.action === 'submit' && (
        <ConfirmDialog
          title={dialog.article.status === 'Rejected' ? 'Resubmit for review' : 'Submit for review'}
          confirmLabel={dialog.article.status === 'Rejected' ? 'Resubmit' : 'Submit'}
          busyLabel="Submitting…"
          confirmTestId="article-row-submit-confirm"
          onClose={() => setDialog(undefined)}
          onConfirm={confirm}
        >
          <p>
            Submit “{dialog.article.title}” for review? It becomes read-only until an administrator
            decides or you withdraw it.
          </p>
        </ConfirmDialog>
      )}
      {dialog?.action === 'withdraw' && (
        <ConfirmDialog
          title="Withdraw submission"
          confirmLabel="Withdraw"
          busyLabel="Withdrawing…"
          confirmTestId="article-row-withdraw-confirm"
          onClose={() => setDialog(undefined)}
          onConfirm={confirm}
        >
          <p>
            Withdraw “{dialog.article.title}” from review? It becomes an editable draft again and
            you can submit it later.
          </p>
        </ConfirmDialog>
      )}
    </section>
  );
}
