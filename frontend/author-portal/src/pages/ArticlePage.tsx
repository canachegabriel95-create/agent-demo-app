import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  deleteArticle,
  getArticle,
  listReviews,
  submitArticle,
  withdrawArticle,
  type Article,
  type ArticleReviewEntry,
  type ArticleStatus,
} from '../api/articles';
import { ApiError } from '../api/client';
import { createRevision } from '../api/revisions';
import { REVISION_STATUS_LABELS } from './RevisionPage';
import { ConfirmDialog, EmptyState, ErrorState, LoadingState } from '../components';
import styles from './ArticlePage.module.css';

export const STATUS_LABELS: Record<ArticleStatus, string> = {
  Draft: 'Draft',
  PendingReview: 'Pending review',
  Published: 'Published',
  Rejected: 'Rejected',
};

export const DELETE_CONFLICT_MESSAGE =
  'The article was changed or can no longer be deleted. The latest version has been loaded.';
export const STATUS_CONFLICT_MESSAGE = 'This article changed status. Refresh to continue.';
export const PENDING_NOTICE =
  'Pending review – read-only until an administrator decides or you withdraw it.';
export const PUBLISHED_NOTICE =
  'Published – readers see this version. To change it, edit the published article: the changes are reviewed again and this version stays visible meanwhile.';
export const REVISE_CONFLICT_MESSAGE =
  'This article cannot be revised right now (it already has a revision or is not published). The latest version has been loaded.';

const dateTime = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' });

interface Failure {
  message: string;
  correlationId?: string;
}

function describe(error: unknown): Failure {
  if (error instanceof ApiError) {
    return { message: error.detail ?? error.title, correlationId: error.correlationId };
  }

  return { message: 'Something went wrong. Please try again.' };
}

function when(value: string | null) {
  return value ? dateTime.format(new Date(value)) : undefined;
}

/** One of the author's own articles. Everything the author typed is shown as text, never as HTML. */
export function ArticlePage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [reload, setReload] = useState(0);
  const [article, setArticle] = useState<Article>();
  const [notFound, setNotFound] = useState(false);
  const [loadError, setLoadError] = useState<Failure>();
  const [dialog, setDialog] = useState<'delete' | 'submit' | 'withdraw'>();
  const [revising, setRevising] = useState(false);
  const [notice, setNotice] = useState('');
  const [status, setStatus] = useState('');
  const [submitErrors, setSubmitErrors] = useState<string[]>([]);
  const [reviews, setReviews] = useState<ArticleReviewEntry[]>();
  const [reviewsFailed, setReviewsFailed] = useState(false);
  const [reviewsReload, setReviewsReload] = useState(0);

  useEffect(() => {
    if (!id) {
      return;
    }

    const controller = new AbortController();
    getArticle(id, controller.signal).then(
      (loaded) => {
        setArticle(loaded);
        setNotFound(false);
        setLoadError(undefined);
      },
      (error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        // Somebody else's article looks exactly like one that does not exist.
        if (error instanceof ApiError && error.status === 404) {
          setNotFound(true);
        } else {
          setLoadError(describe(error));
        }
      },
    );

    return () => controller.abort();
  }, [id, reload]);

  const submissionVersion = article?.submissionVersion;
  const articleStatus = article?.status;
  useEffect(() => {
    if (!id || submissionVersion === undefined) {
      return;
    }

    const controller = new AbortController();
    listReviews(id, controller.signal).then(
      (loaded) => {
        setReviews(loaded);
        setReviewsFailed(false);
      },
      () => {
        if (!controller.signal.aborted) {
          setReviewsFailed(true);
        }
      },
    );

    return () => controller.abort();
    // The history changes when a decision lands, which also changes the status or the submission count.
  }, [id, submissionVersion, articleStatus, reviewsReload]);

  function refresh() {
    setNotice('');
    setStatus('');
    setSubmitErrors([]);
    setReload((value) => value + 1);
  }

  /** Runs a lifecycle action. Returns a message when the dialog should stay open with it. */
  async function transition(
    action: (id: string, version: number) => Promise<Article>,
    message: string,
  ): Promise<string | undefined> {
    if (!article) {
      return undefined;
    }

    try {
      const updated = await action(article.id, article.version);
      setArticle(updated);
      setNotice('');
      setSubmitErrors([]);
      setStatus(message);
      setDialog(undefined);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 400) {
        const messages = Object.entries(failure.errors)
          .filter(([field]) => field !== 'version')
          .flatMap(([, texts]) => texts);
        setSubmitErrors(messages.length > 0 ? messages : [describe(failure).message]);
        setStatus('');
        setDialog(undefined);
      } else if (failure instanceof ApiError && failure.status === 409) {
        setNotice(STATUS_CONFLICT_MESSAGE);
        setStatus('');
        setDialog(undefined);
      } else {
        const { message: text, correlationId } = describe(failure);
        return correlationId ? `${text} Reference: ${correlationId}` : text;
      }
    }

    return undefined;
  }

  async function startRevision() {
    if (!article || revising) {
      return;
    }

    setRevising(true);
    setNotice('');
    try {
      await createRevision(article.id);
      navigate(`/articles/${article.id}/revision`);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        setNotice(REVISE_CONFLICT_MESSAGE);
        setReload((value) => value + 1);
      } else {
        const { message, correlationId } = describe(failure);
        setNotice(correlationId ? `${message} Reference: ${correlationId}` : message);
      }
    } finally {
      setRevising(false);
    }
  }

  if (notFound) {
    return (
      <section className={styles.root} aria-labelledby="article-heading">
        <h1 id="article-heading">Article not found</h1>
        <EmptyState
          title="We could not find that article."
          description="It may have been deleted, or it may not be yours."
          action={<Link to="/articles">Go to My Articles</Link>}
        />
      </section>
    );
  }

  if (loadError) {
    return (
      <section className={styles.root} aria-labelledby="article-heading">
        <h1 id="article-heading">Article</h1>
        <ErrorState
          title="The article could not be loaded"
          message={loadError.message}
          correlationId={loadError.correlationId}
          onRetry={() => {
            setLoadError(undefined);
            setReload((value) => value + 1);
          }}
        />
      </section>
    );
  }

  if (!article) {
    return (
      <section className={styles.root} aria-labelledby="article-heading">
        <h1 id="article-heading">Article</h1>
        <LoadingState message="Loading the article…" />
      </section>
    );
  }

  const can = (action: string) => article.allowedActions.includes(action);
  const submitted = when(article.submittedAt);
  const published = when(article.publishedAt);
  const rejection = article.status === 'Rejected' ? article.latestReview : null;

  async function confirmDelete(): Promise<string | undefined> {
    if (!article) {
      return undefined;
    }

    try {
      await deleteArticle(article.id, article.version);
      navigate('/articles', { state: { message: `Draft “${article.title}” deleted.` } });
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        setDialog(undefined);
        setNotice(DELETE_CONFLICT_MESSAGE);
        setReload((value) => value + 1);
        return undefined;
      }

      const { message, correlationId } = describe(failure);
      return correlationId ? `${message} Reference: ${correlationId}` : message;
    }

    return undefined;
  }

  return (
    <section className={styles.root} aria-labelledby="article-heading">
      {notice && (
        <div className={styles.alert} role="alert" data-testid="article-notice">
          <p>{notice}</p>
          <button
            type="button"
            className={styles.secondary}
            onClick={refresh}
            data-testid="article-refresh"
          >
            Refresh
          </button>
        </div>
      )}
      {submitErrors.length > 0 && (
        <div className={styles.alert} role="alert" data-testid="article-submit-errors">
          <p>This article cannot be submitted yet:</p>
          <ul>
            {submitErrors.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
          {can('edit') && (
            <Link to={`/articles/${article.id}/edit`} data-testid="article-fix-in-editor">
              Fix in editor
            </Link>
          )}
        </div>
      )}
      <div role="status" data-testid="article-flash">
        {status && <p className={styles.success}>{status}</p>}
      </div>

      <div className={styles.header}>
        <h1 id="article-heading" className={styles.title} data-testid="article-view-title">
          {article.title}
        </h1>
        <span
          className={styles.badge}
          data-status={article.status}
          data-testid="article-status-badge"
        >
          {STATUS_LABELS[article.status]}
        </span>
      </div>

      {article.status === 'PendingReview' && (
        <p className={styles.notice} data-testid="article-readonly-notice">
          {PENDING_NOTICE}
        </p>
      )}
      {article.status === 'Published' && (
        <p className={styles.notice} data-testid="article-readonly-notice">
          {PUBLISHED_NOTICE}
        </p>
      )}

      {article.openRevision && (
        <div
          className={styles.review}
          role="region"
          aria-label="Revision in progress"
          data-testid="article-revision"
        >
          <strong data-testid="article-revision-status">
            Revision {article.openRevision.number}:{' '}
            {REVISION_STATUS_LABELS[article.openRevision.status]}
          </strong>
          <p>
            Readers still see the published version. This article will change only when an
            administrator approves the revision.
          </p>
          <Link to={`/articles/${article.id}/revision`} data-testid="article-open-revision">
            Open revision
          </Link>
        </div>
      )}

      {rejection && (
        <div
          className={styles.review}
          role="region"
          aria-label="Rejection"
          data-testid="article-review"
        >
          <strong>Rejected on {when(rejection.decidedAt)}</strong>
          {rejection.reason && <p data-testid="article-review-reason">{rejection.reason}</p>}
        </div>
      )}

      <dl className={styles.facts}>
        <dt>Category</dt>
        <dd data-testid="article-view-category">{article.category?.name ?? 'No category'}</dd>
        <dt>Created</dt>
        <dd>{when(article.createdAt)}</dd>
        <dt>Last updated</dt>
        <dd>{when(article.updatedAt)}</dd>
        {submitted && (
          <>
            <dt>Submitted</dt>
            <dd>{submitted}</dd>
          </>
        )}
        {published && (
          <>
            <dt>Published</dt>
            <dd>{published}</dd>
          </>
        )}
      </dl>

      <div className={styles.actions}>
        {can('edit') && (
          <Link
            to={`/articles/${article.id}/edit`}
            className={styles.secondary}
            data-testid="article-edit"
          >
            Edit
          </Link>
        )}
        {can('submit') && (
          <button
            type="button"
            className={styles.primary}
            onClick={() => setDialog('submit')}
            data-testid="article-submit"
          >
            {article.status === 'Rejected' ? 'Resubmit for review' : 'Submit for review'}
          </button>
        )}
        {can('withdraw') && (
          <button
            type="button"
            className={styles.secondary}
            onClick={() => setDialog('withdraw')}
            data-testid="article-withdraw"
          >
            Withdraw submission
          </button>
        )}
        {can('revise') && (
          <button
            type="button"
            className={styles.primary}
            onClick={startRevision}
            disabled={revising}
            data-testid="article-revise"
          >
            {revising ? 'Starting…' : 'Edit published article'}
          </button>
        )}
        {can('delete') && (
          <button
            type="button"
            className={styles.secondary}
            onClick={() => setDialog('delete')}
            data-testid="article-delete"
          >
            Delete
          </button>
        )}
      </div>

      <div className={styles.section}>
        <h2>Summary</h2>
        {article.summary ? (
          <p className={styles.text} data-testid="article-view-summary">
            {article.summary}
          </p>
        ) : (
          <p className={styles.empty}>No summary yet.</p>
        )}
      </div>
      <div className={styles.section}>
        <h2>Body</h2>
        {article.body ? (
          <p className={styles.text} data-testid="article-view-body">
            {article.body}
          </p>
        ) : (
          <p className={styles.empty}>No body yet.</p>
        )}
      </div>

      <div className={styles.section} data-testid="article-history">
        <h2>Review history</h2>
        {reviewsFailed ? (
          <p role="alert">
            The review history could not be loaded.{' '}
            <button
              type="button"
              className={styles.secondary}
              onClick={() => {
                setReviewsFailed(false);
                setReviewsReload((value) => value + 1);
              }}
            >
              Try again
            </button>
          </p>
        ) : reviews === undefined ? (
          <p className={styles.empty}>Loading the review history…</p>
        ) : reviews.length === 0 ? (
          <p className={styles.empty}>No decisions yet.</p>
        ) : (
          <ol className={styles.history}>
            {reviews.map((review) => (
              <li
                key={`${review.submissionVersion}-${review.decidedAt}`}
                data-decision={review.decision}
              >
                <strong>
                  Submission {review.submissionVersion}: {review.decision}
                </strong>{' '}
                <span className={styles.when}>{when(review.decidedAt)}</span>
                {review.reason && <p className={styles.text}>{review.reason}</p>}
              </li>
            ))}
          </ol>
        )}
      </div>

      {dialog === 'delete' && (
        <ConfirmDialog
          title="Delete draft"
          confirmLabel="Delete"
          busyLabel="Deleting…"
          confirmTestId="article-delete-confirm"
          variant="danger"
          onClose={() => setDialog(undefined)}
          onConfirm={confirmDelete}
        >
          <p>Delete draft “{article.title}”? This cannot be undone.</p>
        </ConfirmDialog>
      )}
      {dialog === 'submit' && (
        <ConfirmDialog
          title={article.status === 'Rejected' ? 'Resubmit for review' : 'Submit for review'}
          confirmLabel={article.status === 'Rejected' ? 'Resubmit' : 'Submit'}
          busyLabel="Submitting…"
          confirmTestId="article-submit-confirm"
          onClose={() => setDialog(undefined)}
          onConfirm={() => transition(submitArticle, 'Submitted for review.')}
        >
          <p>
            Submit “{article.title}” for review? It becomes read-only until an administrator decides
            or you withdraw it.
          </p>
        </ConfirmDialog>
      )}
      {dialog === 'withdraw' && (
        <ConfirmDialog
          title="Withdraw submission"
          confirmLabel="Withdraw"
          busyLabel="Withdrawing…"
          confirmTestId="article-withdraw-confirm"
          onClose={() => setDialog(undefined)}
          onConfirm={() =>
            transition(withdrawArticle, 'Submission withdrawn. The article is a draft again.')
          }
        >
          <p>
            Withdraw “{article.title}” from review? It becomes an editable draft again and you can
            submit it later.
          </p>
        </ConfirmDialog>
      )}
    </section>
  );
}
