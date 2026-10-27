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
import { RejectRevisionDialog } from './RejectRevisionDialog';
import styles from './ReviewPage.module.css';

export const REVISION_NOT_FOUND_TITLE = 'Revision not found';

export const REVISION_STATUS_LABELS: Record<ReviewRevisionStatus, string> = {
  Draft: 'Draft',
  PendingReview: 'Pending review',
  Rejected: 'Rejected',
  Approved: 'Approved',
};

export function revisionConflictMessage(currentStatus: string | undefined) {
  const label = REVISION_STATUS_LABELS[currentStatus as ReviewRevisionStatus] ?? currentStatus;
  return `This revision has changed since you opened it${label ? ` (current status: ${label})` : ''}. Refresh to see the latest version.`;
}

interface Result {
  key: string;
  revision?: ReviewRevision;
  error?: unknown;
}

type Action = 'approve' | 'reject';

/**
 * One submitted revision of a published article, read-only, next to the version readers see now. Everything the
 * author typed is shown as text (never as HTML).
 */
export function RevisionReviewPage() {
  const { revisionId = '' } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();
  const [action, setAction] = useState<Action | null>(null);
  const [conflict, setConflict] = useState<{ status?: string; detail?: string } | null>(null);

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
        <section aria-labelledby="review-heading">
          {backLink}
          <h1 id="review-heading">{REVISION_NOT_FOUND_TITLE}</h1>
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
      done(
        `Approved the revision of “${revision!.published.title}”. It replaced the published version.`,
      );
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
        <h1 id="review-heading" data-testid="revision-review-title">
          Revision {revision.number}: {revision.title}
        </h1>
        <span
          className={styles.badge}
          data-status={revision.status === 'Approved' ? 'Published' : revision.status}
          data-testid="revision-review-status"
        >
          {REVISION_STATUS_LABELS[revision.status]}
        </span>
      </div>

      <dl className={styles.facts}>
        <dt>Submission</dt>
        <dd data-testid="revision-review-submission-version">{revision.submissionVersion}</dd>
        <dt>Submitted</dt>
        <dd>{formatDateTime(revision.submittedAt)}</dd>
        <dt>Published version</dt>
        <dd data-testid="revision-review-published-version">
          {revision.published.publishedVersion}
        </dd>
      </dl>

      <section className={styles.card} aria-labelledby="review-author-heading">
        <h2 id="review-author-heading">Author</h2>
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
            Approve
          </button>
          <button
            type="button"
            className={styles.danger}
            onClick={() => setAction('reject')}
            disabled={!revision.canReview}
            data-testid="revision-review-reject"
          >
            Reject
          </button>
        </div>
      )}

      <div className={styles.compare}>
        <section
          className={styles.card}
          aria-labelledby="revision-published-heading"
          data-testid="revision-review-published"
        >
          <h2 id="revision-published-heading">Published now</h2>
          <h3>Title</h3>
          <p className={styles.text} data-testid="revision-review-published-title">
            {revision.published.title}
          </p>
          <h3>Category</h3>
          <p data-testid="revision-review-published-category">
            {revision.published.category?.name ?? 'No category'}
          </p>
          <h3>Summary</h3>
          <p className={styles.text} data-testid="revision-review-published-summary">
            {revision.published.summary}
          </p>
          <h3>Body</h3>
          <p className={styles.text} data-testid="revision-review-published-body">
            {revision.published.body}
          </p>
        </section>
        <section
          className={styles.card}
          aria-labelledby="revision-proposed-heading"
          data-testid="revision-review-proposed"
        >
          <h2 id="revision-proposed-heading">Revision</h2>
          <h3>Title</h3>
          <p className={styles.text} data-testid="revision-review-proposed-title">
            {revision.title}
          </p>
          <h3>Category</h3>
          <p data-testid="revision-review-proposed-category">
            {revision.category?.name ?? 'No category'}
          </p>
          <h3>Summary</h3>
          <p className={styles.text} data-testid="revision-review-proposed-summary">
            {revision.summary}
          </p>
          <h3>Body</h3>
          <p className={styles.text} data-testid="revision-review-proposed-body">
            {revision.body}
          </p>
        </section>
      </div>

      <section
        className={styles.card}
        aria-labelledby="review-history-heading"
        data-testid="revision-review-history"
      >
        <h2 id="review-history-heading">Review history of this revision</h2>
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
            Approve the revision of “{revision.published.title}” by {revision.author.fullName}? Its
            content replaces the published version in the feed.
          </p>
        </ConfirmDialog>
      )}
      {action === 'reject' && (
        <RejectRevisionDialog
          revision={revision}
          onClose={() => setAction(null)}
          onRejected={() =>
            done(
              `Rejected the revision of “${revision.published.title}”. The published version is unchanged.`,
            )
          }
          onConflict={handleConflict}
        />
      )}
      {conflict && (
        <Dialog title="The revision has changed" onClose={() => undefined}>
          <p data-testid="revision-review-conflict-message">
            {revisionConflictMessage(conflict.status)}
          </p>
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
