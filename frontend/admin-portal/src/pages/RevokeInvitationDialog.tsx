import { useState } from 'react';
import { ApiError } from '../api/client';
import { revokeInvitation, type Invitation } from '../api/invitations';
import { Dialog } from '../components';
import { describeError } from './invitationFormat';
import styles from './InvitationsPage.module.css';

export interface RevokeInvitationDialogProps {
  invitation: Invitation;
  onClose: () => void;
  onRevoked: (invitation: Invitation) => void;
  /** The invitation changed since it was loaded (409): the page reloads and tells the administrator. */
  onConflict: () => void;
}

export function RevokeInvitationDialog({
  invitation,
  onClose,
  onRevoked,
  onConflict,
}: RevokeInvitationDialogProps) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<{ message: string; correlationId?: string } | null>(null);

  async function confirm() {
    setSubmitting(true);
    setError(null);
    try {
      onRevoked(await revokeInvitation(invitation.id, invitation.version));
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        onConflict();
        return;
      }

      setError(describeError(failure));
      setSubmitting(false);
    }
  }

  return (
    <Dialog title="Revoke invitation" onClose={onClose} busy={submitting}>
      <p>
        Revoke the invitation for <strong>{invitation.email}</strong>? The code
        <code className={styles.inlineCode}> {invitation.code} </code>
        will stop working and this cannot be undone.
      </p>
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
        <button
          type="button"
          className={styles.danger}
          onClick={confirm}
          disabled={submitting}
          data-testid="invitation-revoke-confirm"
        >
          {submitting ? 'Revoking…' : 'Revoke invitation'}
        </button>
      </div>
    </Dialog>
  );
}
