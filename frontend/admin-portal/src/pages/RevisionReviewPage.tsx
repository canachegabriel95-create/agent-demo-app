import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import {
  approveRevision,
  currentStatusOf,
  getReviewRevision,
  type ReviewRevision,
  type ReviewRevisionStatus,
} from '../api/reviews';
import { ConfirmDialog, Dialog, ErrorState, LoadingState } from '../components';
import { describeError, formatDateTime } from './invitationFormat';
import { RejectArticleDialog } from './RejectArticleDialog';
import styles from './RevisionReviewPage.module.css';

export const NOT_FOUND_TITLE = 'Revision not found';

export const STATUS_LABELS: Record<ReviewRevisionStatus, string> = {
  Draft: 'Draft',
  PendingReview: 'Pending review',
  Rejected: 'Rejected',
  Approved: 'Approved',
};

export function conflictMessage(currentStatus: string | undefined) {
  const label = STATUS_LABELS[currentStatus as ReviewRevisionStatus] ?? currentStatus;
  return `This revision has changed since you opened it${label ? ` (current status: ${label})` : ''}. Refresh to see the latest version.`;
}

interface Result {
  key: string;
  revision?: ReviewRevision;
  error?: unknown;
}

type Action = 'approve' | 'reject';

/**
 * One revision of a published article, read-only, next to the version readers see now, so the administrator can see
 * what would change. Everything the author typed is shown as text (never as HTML).
 */
export function RevisionReviewPage() {
  const { revisionId = '' } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();
  const [action, setAction] = useState<Action | null>(null);
  const [conflict, setConflict] = useState<{ status?: string; detail?: string } | null>(null);

  // The queue the administrator came from, so that a decision returns to the same filtered view.
  const queueSearch = (location.state as { queueSearch?: string } | null)?.queueSearch ?? '';

  const requestKey = `${revisionId}|${reload}`;
  useEffect(() => {
    const controller = new AbortController();
    getReviewRevision(revisionId, controller.signal).then(
      (revision) => setResult({ key: requestKey, revision }),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setResult({ key: requestKey, error });
        }
      },
    );

    return () => controller.abort();
  }, [revisionId, requestKey]);

  const current = result?.key === requestKey ? result : undefined;
  const backLink = (
    <Link to={`/reviews${queueSearch}`} className={styles.back}>
      ← Back to the review queue
    </Link>
  );

  if (current?.error) {
    if (current.error instanceof ApiError && current.error.status === 404) {
      return (
        <section aria-labelledby="revision-review-heading">
          {backLink}
          <h1 id="revision-review-heading">{NOT_FOUND_TITLE}</h1>
          <p>There is no submitted revision with this id. The link may be wrong.</p>
        </section>
      );
    }

    const { message, correlationId } = describeError(current.error);
    return (
      <section aria-label="Revision review">
        {backLink}
        <ErrorState
          title="The revision could not be loaded"
          message={message}
          correlationId={correlationId}
          onRetry={() => setReload((value) => value + 1)}
        />
      </section>
    );
  }

  const revision = current?.revision ?? result?.revision;
  if (!revision) {
    return (
      <section aria-label="Revision review">
        {backLink}
        <LoadingState message="Loading the revision…" />
      </section>
    );
  }

  const pending = revision.status === 'PendingReview';
  const published = revision.published;
  const titleChanged = revision.title !== published.title;
  const summaryChanged = revision.summary !== published.summary;
  const bodyChanged = revision.body !== published.body;
  const categoryChanged = revision.category?.id !== published.category?.id;

  function done(message: string) {
    navigate(`/reviews${queueSearch}`, { state: { flash: message } });
  }

  function handleConflict(error: ApiError) {
    setAction(null);
    setConflict({ status: currentStatusOf(error), detail: error.detail });
  }

  async function approve(): Promise<string | undefined> {
    try {
      await approveRevision(revision!.id, revision!.version, revision!.submissionVersion);
      done(`Approved the revision of “${published.title}”. It replaces the published version.`);
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
    <section aria-labelledby="revision-review-heading">
      {backLink}
      <div className={styles.titleRow}>
        <h1 id="revision-review-heading" data-testid="revision-review-title">
          Revision of “{published.title}”
        </h1>
        <span
          className={styles.badge}
          data-status={revision.status}
          data-testid="revision-review-status"
        >
          {STATUS_LABELS[revision.status]}
        </span>
      </div>

      <dl className={styles.facts}>
        <dt>Type</dt>
        <dd data-testid="revision-review-kind">Revision of a published article</dd>
        <dt>Submission</dt>
        <dd data-testid="revision-review-submission-version">{revision.submissionVersion}</dd>
        {revision.submittedAt && (
          <>
            <dt>Submitted</dt>
            <dd>{formatDateTime(revision.submittedAt)}</dd>
          </>
        )}
      </dl>

      <section className={styles.card} aria-labelledby="revision-review-author-heading">
        <h2 id="revision-review-author-heading">Author</h2>
        <dl className={styles.facts}>
          <dt>Name</dt>
          <dd>
            <Link to={`/users/${revision.author.id}`}>{revision.author.fullName}</Link>
          </dd>
          <dt>Email</dt>
          <dd data-testid="revision-review-author-email">{revision.author.email}</dd>
          <dt>Account</dt>
          <dd>
            <span
              className={styles.badge}
              data-status={revision.author.status}
              data-testid="revision-review-author-status"
            >
              {revision.author.status}
            </span>
          </dd>
        </dl>
      </section>

      {!revision.canReview && revision.reviewBlockedReason && (
        <p className={styles.info} role="status" data-testid="revision-review-blocked">
          {revision.reviewBlockedReason}
        </p>
      )}

      {pending && (
        <div className={styles.actions}>
          <button
            type="button"
            className={styles.primary}
            onClick={() => setAction('approve')}
            disabled={!revision.canReview}
            data-testid="revision-review-approve"
          >
            Approve revision
          </button>
          <button
            type="button"
            className={styles.danger}
            onClick={() => setAction('reject')}
            disabled={!revision.canReview}
            data-testid="revision-review-reject"
          >
            Reject revision
          </button>
        </div>
      )}

      <div className={styles.compare} data-testid="revision-compare">
        <section
          className={styles.card}
          aria-labelledby="revision-published-heading"
          data-testid="revision-published"
        >
          <h2 id="revision-published-heading">Published version {published.versionNumber}</h2>
          <p className={styles.when}>Live since {formatDateTime(published.publishedAt)}</p>
          <h3>Title</h3>
          <p className={styles.text} data-testid="published-title">
            {published.title}
          </p>
          <h3>Category</h3>
          <p className={styles.text} data-testid="published-category">
            {published.category?.name ?? 'No category'}
          </p>
          <h3>Summary</h3>
          <p className={styles.text} data-testid="published-summary">
            {published.summary}
          </p>
          <h3>Body</h3>
          <p className={styles.text} data-testid="published-body">
            {published.body}
          </p>
        </section>

        <section
          className={styles.card}
          aria-labelledby="revision-proposed-heading"
          data-testid="revision-proposed"
        >
          <h2 id="revision-proposed-heading">Revision</h2>
          <p className={styles.when}>What would replace the published version</p>
          <h3>
            Title
            {titleChanged && (
              <span className={styles.changedNote} data-testid="changed-title">
                {' '}
                (changed)
              </span>
            )}
          </h3>
          <p className={styles.text} data-testid="revision-title">
            {revision.title}
          </p>
          <h3>
            Category
            {categoryChanged && (
              <span className={styles.changedNote} data-testid="changed-category">
                {' '}
                (changed)
              </span>
            )}
          </h3>
          <p className={styles.text} data-testid="revision-category">
            {revision.category?.name ?? 'No category'}
          </p>
          <h3>
            Summary
            {summaryChanged && (
              <span className={styles.changedNote} data-testid="changed-summary">
                {' '}
                (changed)
              </span>
            )}
          </h3>
          <p className={styles.text} data-testid="revision-summary">
            {revision.summary}
          </p>
          <h3>
            Body
            {bodyChanged && (
              <span className={styles.changedNote} data-testid="changed-body">
                {' '}
                (changed)
              </span>
            )}
          </h3>
          <p className={styles.text} data-testid="revision-body">
            {revision.body}
          </p>
        </section>
      </div>

      <section
        className={styles.card}
        aria-labelledby="revision-review-history-heading"
        data-testid="revision-review-history"
      >
        <h2 id="revision-review-history-heading">Review history</h2>
        {revision.reviewHistory.length === 0 ? (
          <p className={styles.empty}>No decisions yet.</p>
        ) : (
          <ol className={styles.history}>
            {revision.reviewHistory.map((entry) => (
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
          title="Approve revision"
          confirmLabel="Approve"
          busyLabel="Approving…"
          confirmTestId="revision-review-approve-confirm"
          onClose={() => setAction(null)}
          onConfirm={approve}
        >
          <p>
            Replace the published version of “{published.title}” by {revision.author.fullName} with
            this revision? Readers see the new version right away; the old one is kept in the
            history.
          </p>
        </ConfirmDialog>
      )}
      {action === 'reject' && (
        <RejectArticleDialog
          article={revision}
          kind="revision"
          onClose={() => setAction(null)}
          onRejected={() =>
            done(`Rejected the revision of “${published.title}”. The author can see your reason.`)
          }
          onConflict={handleConflict}
        />
      )}
      {conflict && (
        <Dialog title="The revision has changed" onClose={() => undefined}>
          <p data-testid="revision-review-conflict-message">{conflictMessage(conflict.status)}</p>
          {conflict.detail && <p className={styles.when}>{conflict.detail}</p>}
          <div className={styles.dialogActions}>
            <button
              type="button"
              className={styles.primary}
              onClick={() => {
                setConflict(null);
                setReload((value) => value + 1);
              }}
              data-testid="revision-review-refresh"
            >
              Refresh
            </button>
          </div>
        </Dialog>
      )}
    </section>
  );
}
