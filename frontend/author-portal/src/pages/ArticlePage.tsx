import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { deleteArticle, getArticle, type Article, type ArticleStatus } from '../api/articles';
import { ApiError } from '../api/client';
import { Dialog, EmptyState, ErrorState, LoadingState } from '../components';
import styles from './ArticlePage.module.css';

export const STATUS_LABELS: Record<ArticleStatus, string> = {
  Draft: 'Draft',
  PendingReview: 'Pending review',
  Published: 'Published',
  Rejected: 'Rejected',
};

export const DELETE_CONFLICT_MESSAGE =
  'The article was changed or can no longer be deleted. The latest version has been loaded.';

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

interface DeleteDialogProps {
  article: Article;
  onClose: () => void;
  onDeleted: () => void;
  onConflict: () => void;
}

function DeleteDraftDialog({ article, onClose, onDeleted, onConflict }: DeleteDialogProps) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<Failure>();

  async function confirm() {
    setDeleting(true);
    setError(undefined);
    try {
      await deleteArticle(article.id, article.version);
      onDeleted();
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        onConflict();
        return;
      }

      setError(describe(failure));
      setDeleting(false);
    }
  }

  return (
    <Dialog title="Delete draft" onClose={onClose} busy={deleting} initialFocus={cancelRef}>
      <p>Delete draft “{article.title}”? This cannot be undone.</p>
      {error && (
        <p className={styles.alert} role="alert">
          {error.message}
          {error.correlationId && (
            <>
              {' '}
              Reference: <code>{error.correlationId}</code>
            </>
          )}
        </p>
      )}
      <div className={styles.dialogActions}>
        <button
          ref={cancelRef}
          type="button"
          className={styles.secondary}
          onClick={onClose}
          disabled={deleting}
        >
          Cancel
        </button>
        <button
          type="button"
          className={styles.danger}
          onClick={confirm}
          disabled={deleting}
          data-testid="article-delete-confirm"
        >
          {deleting ? 'Deleting…' : 'Delete'}
        </button>
      </div>
    </Dialog>
  );
}

/** One of the author's own articles. Everything the author typed is shown as text, never as HTML. */
export function ArticlePage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [reload, setReload] = useState(0);
  const [article, setArticle] = useState<Article>();
  const [notFound, setNotFound] = useState(false);
  const [loadError, setLoadError] = useState<Failure>();
  const [confirming, setConfirming] = useState(false);
  const [notice, setNotice] = useState('');

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

  return (
    <section className={styles.root} aria-labelledby="article-heading">
      {notice && (
        <p className={styles.alert} role="alert" data-testid="article-notice">
          {notice}
        </p>
      )}
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

      {article.latestReview?.decision === 'Rejected' && article.status === 'Rejected' && (
        <div className={styles.review} data-testid="article-review">
          <strong>Rejected on {when(article.latestReview.decidedAt)}</strong>
          {article.latestReview.reason && <p>{article.latestReview.reason}</p>}
        </div>
      )}

      <div className={styles.actions}>
        {can('edit') && (
          <Link
            to={`/articles/${article.id}/edit`}
            className={styles.primary}
            data-testid="article-edit"
          >
            Edit
          </Link>
        )}
        {can('delete') && (
          <button
            type="button"
            className={styles.secondary}
            onClick={() => setConfirming(true)}
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

      {confirming && (
        <DeleteDraftDialog
          article={article}
          onClose={() => setConfirming(false)}
          onDeleted={() =>
            navigate('/articles', { state: { message: `Draft “${article.title}” deleted.` } })
          }
          onConflict={() => {
            setConfirming(false);
            setNotice(DELETE_CONFLICT_MESSAGE);
            setReload((value) => value + 1);
          }}
        />
      )}
    </section>
  );
}
