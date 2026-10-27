import { useState, type FormEvent } from 'react';
import { ApiError } from '../api/client';
import {
  BIO_MAX,
  FULL_NAME_MAX,
  FULL_NAME_MIN,
  updateUserProfile,
  type UserDetails,
} from '../api/users';
import { Dialog, FormField } from '../components';
import { describeError } from './invitationFormat';
import styles from './UserDetailPage.module.css';

export const PROFILE_CONFLICT_MESSAGE =
  'This profile was changed by someone else. Reload to continue.';

export interface EditProfileDialogProps {
  user: UserDetails;
  onClose: () => void;
  /** The profile was saved; `updated` is the author as the API now has it. */
  onSaved: (updated: UserDetails) => void;
  /** The administrator chose Reload after a conflict: reload the author and close the dialog. */
  onReload: () => void;
}

export function EditProfileDialog({ user, onClose, onSaved, onReload }: EditProfileDialogProps) {
  const [fullName, setFullName] = useState(user.fullName);
  const [bio, setBio] = useState(user.bio ?? '');
  const [errors, setErrors] = useState<{ fullName?: string; bio?: string }>({});
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

    const name = fullName.trim();
    const found = {
      fullName: !name
        ? 'Full name is required.'
        : name.length < FULL_NAME_MIN || name.length > FULL_NAME_MAX
          ? `Full name must be between ${FULL_NAME_MIN} and ${FULL_NAME_MAX} characters.`
          : undefined,
      bio:
        bio.trim().length > BIO_MAX
          ? `Biography must be at most ${BIO_MAX} characters.`
          : undefined,
    };
    setErrors(found);
    setFormError(null);
    if (found.fullName || found.bio) {
      return;
    }

    setSubmitting(true);
    try {
      onSaved(await updateUserProfile(user.id, { fullName: name, bio: bio.trim() }, user.version));
    } catch (failure) {
      setSubmitting(false);
      if (failure instanceof ApiError && failure.status === 409) {
        setConflict(true);
      } else if (failure instanceof ApiError && failure.status === 400) {
        const server = { fullName: failure.errors.fullName?.[0], bio: failure.errors.bio?.[0] };
        setErrors(server);
        if (!server.fullName && !server.bio) {
          setFormError(describeError(failure));
        }
      } else {
        setFormError(describeError(failure));
      }
    }
  }

  return (
    <Dialog title="Edit profile" onClose={onClose} busy={submitting}>
      <form onSubmit={handleSubmit} noValidate>
        {conflict && (
          <p className={styles.formError} role="alert" data-testid="profile-conflict">
            {PROFILE_CONFLICT_MESSAGE}
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
          label="Display name"
          name="fullName"
          autoComplete="off"
          value={fullName}
          onChange={(value) => {
            setFullName(value);
            setErrors((previous) => ({ ...previous, fullName: undefined }));
          }}
          required
          disabled={submitting || conflict}
          hint={`${fullName.trim().length} / ${FULL_NAME_MAX} characters (at least ${FULL_NAME_MIN})`}
          error={errors.fullName}
        />
        <FormField
          label="Biography"
          name="bio"
          multiline
          rows={5}
          value={bio}
          onChange={(value) => {
            setBio(value);
            setErrors((previous) => ({ ...previous, bio: undefined }));
          }}
          disabled={submitting || conflict}
          hint={`${bio.trim().length} / ${BIO_MAX} characters`}
          error={errors.bio}
        />
        <p className={styles.readOnlyNote}>Email, role and password cannot be changed here.</p>
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
              data-testid="profile-reload"
            >
              Reload
            </button>
          ) : (
            <button
              type="submit"
              className={styles.primary}
              disabled={submitting}
              data-testid="profile-save"
            >
              {submitting ? 'Saving…' : 'Save'}
            </button>
          )}
        </div>
      </form>
    </Dialog>
  );
}
