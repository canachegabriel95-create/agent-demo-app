import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import {
  approveArticle,
  currentStatusOf,
  getReviewArticle,
  type ReviewArticle,
  type ReviewArticleStatus,
} from '../api/reviews';
import { ConfirmDialog, Dialog, ErrorState, LoadingState } from '../components';
import { describeError, formatDateTime } from './invitationFormat';
import { RejectArticleDialog } from './RejectArticleDialog';
import styles from './ReviewPage.module.css';

export const NOT_FOUND_TITLE = 'Article not found';

export const STATUS_LABELS: Record<ReviewArticleStatus, string> = {
  Draft: 'Draft',
  PendingReview: 'Pending review',
  Published: 'Published',
  Rejected: 'Rejected',
};

export function conflictMessage(currentStatus: string | undefined) {
  const label = STATUS_LABELS[currentStatus as ReviewArticleStatus] ?? currentStatus;
  return `This article has changed since you opened it${label ? ` (current status: ${label})` : ''}. Refresh to see the latest version.`;
}

interface Result {
  key: string;
  article?: ReviewArticle;
  error?: unknown;
}

type Action = 'approve' | 'reject';

/**
 * One submitted article, read-only: what the author submitted, who they are, what was decided before, and the
 * decision. Everything the author typed is shown as text (never as HTML).
 */
export function ReviewPage() {
  const { articleId = '' } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();
  const [action, setAction] = useState<Action | null>(null);
  const [conflict, setConflict] = useState<{ status?: string; detail?: string } | null>(null);

  // The queue the administrator came from, so that a decision returns to the same filtered view.
  const queueSearch = (location.state as { queueSearch?: string } | null)?.queueSearch ?? '';

  const requestKey = `${articleId}|${reload}`;
  useEffect(() => {
    const controller = new AbortController();
    getReviewArticle(articleId, controller.signal).then(
      (article) => setResult({ key: requestKey, article }),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setResult({ key: requestKey, error });
        }
      },
    );

    return () => controller.abort();
  }, [articleId, requestKey]);

  const current = result?.key === requestKey ? result : undefined;
  const backLink = (
    <Link to={`/reviews${queueSearch}`} className={styles.back}>
      ← Back to the review queue
    </Link>
  );

  if (current?.error) {
    if (current.error instanceof ApiError && current.error.status === 404) {
      return (
        <section aria-labelledby="review-heading">
          {backLink}
          <h1 id="review-heading">{NOT_FOUND_TITLE}</h1>
          <p>There is no submitted article with this id. The link may be wrong.</p>
        </section>
      );
    }

    const { message, correlationId } = describeError(current.error);
    return (
      <section aria-label="Article review">
        {backLink}
        <ErrorState
          title="The article could not be loaded"
          message={message}
          correlationId={correlationId}
          onRetry={() => setReload((value) => value + 1)}
        />
      </section>
    );
  }

  const article = current?.article ?? result?.article;
  if (!article) {
    return (
      <section aria-label="Article review">
        {backLink}
        <LoadingState message="Loading the article…" />
      </section>
    );
  }

  const pending = article.status === 'PendingReview';

  function done(message: string) {
    navigate(`/reviews${queueSearch}`, { state: { flash: message } });
  }

  function handleConflict(error: ApiError) {
    setAction(null);
    setConflict({ status: currentStatusOf(error), detail: error.detail });
  }

  async function approve(): Promise<string | undefined> {
    try {
      await approveArticle(article!.id, article!.version, article!.submissionVersion);
      done(`Approved “${article!.title}”. It is now published.`);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        handleConflict(failure);
        return undefined;
      }

      const { message, correlationId } = describeError(failure);
      return correlationId ? `${message} Reference: ${correlationId}` : message;
    }

    return undefined;
  }

  return (
    <section aria-labelledby="review-heading">
      {backLink}
      <div className={styles.titleRow}>
        <h1 id="review-heading" data-testid="review-title">
          {article.title}
        </h1>
        <span className={styles.badge} data-status={article.status} data-testid="review-status">
          {STATUS_LABELS[article.status]}
        </span>
      </div>

      <dl className={styles.facts}>
        <dt>Category</dt>
        <dd data-testid="review-category">{article.category?.name ?? 'No category'}</dd>
        <dt>Submission</dt>
        <dd data-testid="review-submission-version">{article.submissionVersion}</dd>
        <dt>Submitted</dt>
        <dd>{formatDateTime(article.submittedAt)}</dd>
      </dl>

      <section className={styles.card} aria-labelledby="review-author-heading">
        <h2 id="review-author-heading">Author</h2>
        <dl className={styles.facts}>
          <dt>Name</dt>
          <dd>
            <Link to={`/users/${article.author.id}`}>{article.author.fullName}</Link>
          </dd>
          <dt>Email</dt>
          <dd data-testid="review-author-email">{article.author.email}</dd>
          <dt>Account</dt>
          <dd>
            <span
              className={styles.badge}
              data-status={article.author.status}
              data-testid="review-author-status"
            >
              {article.author.status}
            </span>
          </dd>
        </dl>
      </section>

      {!article.canReview && article.reviewBlockedReason && (
        <p className={styles.info} role="status" data-testid="review-blocked">
          {article.reviewBlockedReason}
        </p>
      )}

      {pending && (
        <div className={styles.actions}>
          <button
            type="button"
            className={styles.primary}
            onClick={() => setAction('approve')}
            disabled={!article.canReview}
            data-testid="review-approve"
          >
            Approve
          </button>
          <button
            type="button"
            className={styles.danger}
            onClick={() => setAction('reject')}
            disabled={!article.canReview}
            data-testid="review-reject"
          >
            Reject
          </button>
        </div>
      )}

      <section className={styles.card} aria-labelledby="review-content-heading">
        <h2 id="review-content-heading">Submitted content</h2>
        <h3>Summary</h3>
        <p className={styles.text} data-testid="review-summary">
          {article.summary}
        </p>
        <h3>Body</h3>
        <p className={styles.text} data-testid="review-body">
          {article.body}
        </p>
      </section>

      <section
        className={styles.card}
        aria-labelledby="review-history-heading"
        data-testid="review-history"
      >
        <h2 id="review-history-heading">Review history</h2>
        {article.reviewHistory.length === 0 ? (
          <p className={styles.empty}>No decisions yet.</p>
        ) : (
          <ol className={styles.history}>
            {article.reviewHistory.map((entry) => (
              <li key={`${entry.submissionVersion}-${entry.decidedAt}`}>
                <strong>
                  Submission {entry.submissionVersion}: {entry.decision}
                </strong>{' '}
                <span className={styles.when}>
                  by {entry.reviewer.fullName}, {formatDateTime(entry.decidedAt)}
                </span>
                {entry.reason && <p className={styles.text}>{entry.reason}</p>}
              </li>
            ))}
          </ol>
        )}
      </section>

      {action === 'approve' && (
        <ConfirmDialog
          title="Approve article"
          confirmLabel="Approve"
          busyLabel="Approving…"
          confirmTestId="review-approve-confirm"
          onClose={() => setAction(null)}
          onConfirm={approve}
        >
          <p>
            Publish “{article.title}” by {article.author.fullName}? It becomes visible in the
            published feed.
          </p>
        </ConfirmDialog>
      )}
      {action === 'reject' && (
        <RejectArticleDialog
          article={article}
          onClose={() => setAction(null)}
          onRejected={() => done(`Rejected “${article.title}”. The author can see your reason.`)}
          onConflict={handleConflict}
        />
      )}
      {conflict && (
        <Dialog title="The article has changed" onClose={() => undefined}>
          <p data-testid="review-conflict-message">{conflictMessage(conflict.status)}</p>
          {conflict.detail && <p className={styles.when}>{conflict.detail}</p>}
          <div className={styles.dialogActions}>
            <button
              type="button"
              className={styles.primary}
              onClick={() => {
                setConflict(null);
                setReload((value) => value + 1);
              }}
              data-testid="review-refresh"
            >
              Refresh
            </button>
          </div>
        </Dialog>
      )}
    </section>
  );
}
