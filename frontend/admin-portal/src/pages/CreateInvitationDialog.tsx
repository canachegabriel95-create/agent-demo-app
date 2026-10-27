import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ApiError } from '../api/client';
import { createInvitation, type Invitation } from '../api/invitations';
import { CopyButton, Dialog, FormField } from '../components';
import { describeError, formatDateTime } from './invitationFormat';
import styles from './InvitationsPage.module.css';

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export interface CreateInvitationDialogProps {
  onClose: () => void;
  /** Called once the invitation exists, so the list can reload while the code is still on screen. */
  onCreated: (invitation: Invitation) => void;
}

export function CreateInvitationDialog({ onClose, onCreated }: CreateInvitationDialogProps) {
  const [email, setEmail] = useState('');
  const [days, setDays] = useState('7');
  const [errors, setErrors] = useState<{ email?: string; validForDays?: string }>({});
  const [formError, setFormError] = useState<{ message: string; correlationId?: string } | null>(
    null,
  );
  const [submitting, setSubmitting] = useState(false);
  const [created, setCreated] = useState<Invitation | null>(null);
  const copyRef = useRef<HTMLButtonElement>(null);

  // The submit button disappears with the form: move focus to the first thing to do with the new code.
  useEffect(() => {
    if (created) {
      copyRef.current?.focus();
    }
  }, [created]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }

    const trimmed = email.trim();
    const validForDays = Number(days);
    const found = {
      email: !trimmed
        ? 'Email is required.'
        : EMAIL_PATTERN.test(trimmed)
          ? undefined
          : 'Enter a valid email address.',
      validForDays:
        days.trim() !== '' &&
        Number.isInteger(validForDays) &&
        validForDays >= 1 &&
        validForDays <= 30
          ? undefined
          : 'Enter a whole number of days between 1 and 30.',
    };
    setErrors(found);
    setFormError(null);
    if (found.email || found.validForDays) {
      return;
    }

    setSubmitting(true);
    try {
      const invitation = await createInvitation(trimmed, validForDays);
      setCreated(invitation);
      onCreated(invitation);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 400) {
        const server = {
          email: failure.errors.email?.[0],
          validForDays: failure.errors.validForDays?.[0],
        };
        setErrors(server);
        if (!server.email && !server.validForDays) {
          setFormError(describeError(failure));
        }
      } else if (failure instanceof ApiError && failure.status === 409) {
        // An administrator or author with this email already exists.
        setErrors({ email: describeError(failure).message });
      } else {
        setFormError(describeError(failure));
      }
    } finally {
      setSubmitting(false);
    }
  }

  if (created) {
    return (
      <Dialog title="New invitation" onClose={onClose}>
        <p>
          Invitation created for <strong>{created.email}</strong>. Share this code with them; it
          expires on {formatDateTime(created.expiresAt)}.
        </p>
        <p className={styles.newCode} data-testid="invitation-created-code">
          {created.code}
        </p>
        <div className={styles.dialogActions}>
          <CopyButton
            ref={copyRef}
            text={created.code}
            label={`Copy code for ${created.email}`}
            testId="invitation-created-copy"
          />
          <button
            type="button"
            className={styles.primary}
            onClick={onClose}
            data-testid="invitation-create-done"
          >
            Done
          </button>
        </div>
      </Dialog>
    );
  }

  return (
    <Dialog title="New invitation" onClose={onClose} busy={submitting}>
      <form onSubmit={handleSubmit} noValidate>
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
          label="Email"
          name="email"
          type="email"
          autoComplete="off"
          value={email}
          onChange={setEmail}
          required
          disabled={submitting}
          error={errors.email}
        />
        <FormField
          label="Valid for (days)"
          name="validForDays"
          type="number"
          value={days}
          onChange={setDays}
          required
          disabled={submitting}
          hint="Between 1 and 30 days."
          error={errors.validForDays}
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
            className={styles.primary}
            disabled={submitting}
            data-testid="invitation-create-submit"
          >
            {submitting ? 'Creating…' : 'Create invitation'}
          </button>
        </div>
      </form>
    </Dialog>
  );
}
