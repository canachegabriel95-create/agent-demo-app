import { useState, type FormEvent } from 'react';
import { ApiError } from '../api/client';
import { REASON_MAX, REASON_MIN, suspendUser, type UserDetails } from '../api/users';
import { Dialog, FormField } from '../components';
import { describeError } from './invitationFormat';
import styles from './UserDetailPage.module.css';

export const USER_CONFLICT_MESSAGE = 'This author was changed by someone else. Reload to continue.';

export interface SuspendUserDialogProps {
  user: UserDetails;
  onClose: () => void;
  onSuspended: (updated: UserDetails) => void;
  /** The administrator chose Reload after a conflict: reload the author and close the dialog. */
  onReload: () => void;
}

export function SuspendUserDialog({
  user,
  onClose,
  onSuspended,
  onReload,
}: SuspendUserDialogProps) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string>();
  const [formError, setFormError] = useState<{ message: string; correlationId?: string } | null>(
    null,
  );
  const [conflict, setConflict] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting || conflict) {
      return;
    }

    const text = reason.trim();
    const found = !text
      ? 'A reason is required.'
      : text.length < REASON_MIN || text.length > REASON_MAX
        ? `The reason must be between ${REASON_MIN} and ${REASON_MAX} characters.`
        : undefined;
    setError(found);
    setFormError(null);
    if (found) {
      return;
    }

    setSubmitting(true);
    try {
      onSuspended(await suspendUser(user.id, text, user.version));
    } catch (failure) {
      setSubmitting(false);
      if (failure instanceof ApiError && failure.status === 409) {
        setConflict(true);
      } else if (failure instanceof ApiError && failure.status === 400 && failure.errors.reason) {
        setError(failure.errors.reason[0]);
      } else {
        setFormError(describeError(failure));
      }
    }
  }

  return (
    <Dialog title={`Suspend ${user.fullName}`} onClose={onClose} busy={submitting}>
      <form onSubmit={handleSubmit} noValidate>
        <p>
          The author is signed out everywhere straight away and cannot sign in again until you
          reactivate the account. Their articles are kept.
        </p>
        {conflict && (
          <p className={styles.formError} role="alert" data-testid="suspend-conflict">
            {USER_CONFLICT_MESSAGE}
          </p>
        )}
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
          rows={4}
          value={reason}
          onChange={(value) => {
            setReason(value);
            setError(undefined);
          }}
          required
          disabled={submitting || conflict}
          hint={`${reason.trim().length} / ${REASON_MAX} characters (at least ${REASON_MIN})`}
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
          {conflict ? (
            <button
              type="button"
              className={styles.primary}
              onClick={onReload}
              data-testid="suspend-reload"
            >
              Reload
            </button>
          ) : (
            <button
              type="submit"
              className={styles.danger}
              disabled={submitting}
              data-testid="suspend-confirm"
            >
              {submitting ? 'Suspending…' : 'Suspend author'}
            </button>
          )}
        </div>
      </form>
    </Dialog>
  );
}
