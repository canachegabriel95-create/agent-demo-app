import { useState } from 'react';
import { ApiError } from '../api/client';
import { reactivateUser, type UserDetails } from '../api/users';
import { Dialog } from '../components';
import { describeError } from './invitationFormat';
import styles from './UserDetailPage.module.css';
import { USER_CONFLICT_MESSAGE } from './SuspendUserDialog';

export interface ReactivateUserDialogProps {
  user: UserDetails;
  onClose: () => void;
  onReactivated: (updated: UserDetails) => void;
  /** The administrator chose Reload after a conflict: reload the author and close the dialog. */
  onReload: () => void;
}

export function ReactivateUserDialog({
  user,
  onClose,
  onReactivated,
  onReload,
}: ReactivateUserDialogProps) {
  const [submitting, setSubmitting] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [error, setError] = useState<{ message: string; correlationId?: string } | null>(null);

  async function confirm() {
    setSubmitting(true);
    setError(null);
    try {
      onReactivated(await reactivateUser(user.id, user.version));
    } catch (failure) {
      setSubmitting(false);
      if (failure instanceof ApiError && failure.status === 409) {
        setConflict(true);
      } else {
        setError(describeError(failure));
      }
    }
  }

  return (
    <Dialog title={`Reactivate ${user.fullName}`} onClose={onClose} busy={submitting}>
      <p>
        <strong>{user.fullName}</strong> will be able to sign in again. The sessions that were ended
        by the suspension stay ended, so the author must sign in again.
      </p>
      {conflict && (
        <p className={styles.formError} role="alert" data-testid="reactivate-conflict">
          {USER_CONFLICT_MESSAGE}
        </p>
      )}
      {error && (
        <p className={styles.formError} role="alert">
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
        <button type="button" className={styles.secondary} onClick={onClose} disabled={submitting}>
          Cancel
        </button>
        {conflict ? (
          <button
            type="button"
            className={styles.primary}
            onClick={onReload}
            data-testid="reactivate-reload"
          >
            Reload
          </button>
        ) : (
          <button
            type="button"
            className={styles.primary}
            onClick={confirm}
            disabled={submitting}
            data-testid="reactivate-confirm"
          >
            {submitting ? 'Reactivating…' : 'Reactivate author'}
          </button>
        )}
      </div>
    </Dialog>
  );
}
