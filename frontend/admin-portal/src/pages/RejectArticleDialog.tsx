import { useState, type FormEvent } from 'react';
import { ApiError } from '../api/client';
import {
  REVIEW_REASON_MAX,
  REVIEW_REASON_MIN,
  rejectArticle,
  rejectRevision,
  type ReviewArticle,
  type ReviewRevision,
} from '../api/reviews';
import { Dialog, FormField } from '../components';
import { describeError } from './invitationFormat';
import styles from './ReviewPage.module.css';

export interface RejectArticleDialogProps {
  /** The submitted article, or the revision of a published article when `kind` is `revision`. */
  article: ReviewArticle | ReviewRevision;
  /** What is being rejected; a revision leaves the published version as it is. */
  kind?: 'article' | 'revision';
  onClose: () => void;
  onRejected: (updated: ReviewArticle | ReviewRevision) => void;
  /** The article is not as it was when opened (409): the page explains and offers a refresh. */
  onConflict: (error: ApiError) => void;
}

/** Asks for the reason the author will read, then sends the article back. */
export function RejectArticleDialog({
  article,
  kind = 'article',
  onClose,
  onRejected,
  onConflict,
}: RejectArticleDialogProps) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string>();
  const [formError, setFormError] = useState<{ message: string; correlationId?: string }>();
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }

    const text = reason.trim();
    const found = !text
      ? 'A reason is required.'
      : text.length < REVIEW_REASON_MIN || text.length > REVIEW_REASON_MAX
        ? `The reason must be between ${REVIEW_REASON_MIN} and ${REVIEW_REASON_MAX} characters.`
        : undefined;
    setError(found);
    setFormError(undefined);
    if (found) {
      return;
    }

    setSubmitting(true);
    try {
      const reject = kind === 'revision' ? rejectRevision : rejectArticle;
      onRejected(await reject(article.id, article.version, article.submissionVersion, text));
    } catch (failure) {
      setSubmitting(false);
      if (failure instanceof ApiError && failure.status === 409) {
        onConflict(failure);
      } else if (failure instanceof ApiError && failure.status === 400 && failure.errors.reason) {
        setError(failure.errors.reason[0]);
      } else {
        setFormError(describeError(failure));
      }
    }
  }

  return (
    <Dialog
      title={kind === 'revision' ? 'Reject revision' : 'Reject article'}
      onClose={onClose}
      busy={submitting}
    >
      <form onSubmit={handleSubmit} noValidate>
        <p>
          “{article.title}” goes back to {article.author.fullName}, who can edit it and submit it
          again. They will read your reason.
          {kind === 'revision' && ' The published version stays as it is.'}
        </p>
        {formError && (
          <p className={styles.formError} role="alert">
            {formError.message}
            {formError.correlationId && (
              <>
                {' '}
                Reference: <code>{formError.correlationId}</code>
              </>
            )}
          </p>
        )}
        <FormField
          label="Reason"
          name="reason"
          multiline
          rows={5}
          value={reason}
          onChange={(value) => {
            setReason(value);
            setError(undefined);
          }}
          required
          disabled={submitting}
          hint={`${reason.trim().length} / ${REVIEW_REASON_MAX} characters (at least ${REVIEW_REASON_MIN})`}
          error={error}
        />
        <div className={styles.dialogActions}>
          <button
            type="button"
            className={styles.secondary}
            onClick={onClose}
            disabled={submitting}
          >
            Cancel
          </button>
          <button
            type="submit"
            className={styles.danger}
            disabled={submitting}
            data-testid="review-reject-confirm"
          >
            {submitting ? 'Rejecting…' : kind === 'revision' ? 'Reject revision' : 'Reject article'}
          </button>
        </div>
      </form>
    </Dialog>
  );
}
