import { useEffect, useState, type FormEvent } from 'react';
import { useBlocker } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getProfile, updateProfile, type Profile } from '../api/profile';
import { useAuth } from '../auth/useAuth';
import { ErrorState, FormField, LoadingState } from '../components';
import { BIO_MAX, FULL_NAME_MAX, FULL_NAME_MIN, validateProfile } from '../validation/profile';
import styles from './ProfilePage.module.css';

export const SAVED_MESSAGE = 'Profile saved';
export const CONFLICT_MESSAGE = 'This profile was changed by someone else. Reload to continue.';
export const LEAVE_MESSAGE = 'You have unsaved changes. Leave this page without saving?';

const date = new Intl.DateTimeFormat('en', { dateStyle: 'long' });

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

export function ProfilePage() {
  const { updateUser } = useAuth();
  const [reload, setReload] = useState(0);
  const [profile, setProfile] = useState<Profile>();
  const [loadError, setLoadError] = useState<Failure>();
  const [fullName, setFullName] = useState('');
  const [bio, setBio] = useState('');
  const [errors, setErrors] = useState<{ fullName?: string; bio?: string }>({});
  const [formError, setFormError] = useState<Failure>();
  const [conflict, setConflict] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getProfile(controller.signal).then(
      (loaded) => {
        setProfile(loaded);
        setFullName(loaded.fullName);
        setBio(loaded.bio ?? '');
        setLoadError(undefined);
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(describe(error));
        }
      },
    );

    return () => controller.abort();
  }, [reload]);

  const dirty =
    profile !== undefined && (fullName !== profile.fullName || bio !== (profile.bio ?? ''));

  // Leaving the page (a link, the back button, Log out) with unsaved changes asks first.
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      dirty && !saving && currentLocation.pathname !== nextLocation.pathname,
  );
  useEffect(() => {
    if (blocker.state !== 'blocked') {
      return;
    }

    if (window.confirm(LEAVE_MESSAGE)) {
      blocker.proceed();
    } else {
      blocker.reset();
    }
  }, [blocker]);

  // Closing or reloading the tab is not a navigation the router sees.
  useEffect(() => {
    if (!dirty) {
      return;
    }

    function warn(event: BeforeUnloadEvent) {
      event.preventDefault();
    }

    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [dirty]);

  function edit(change: () => void, field: 'fullName' | 'bio') {
    change();
    setSaved(false);
    setErrors((previous) => ({ ...previous, [field]: undefined }));
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!profile || saving || conflict) {
      return;
    }

    const found = validateProfile(fullName, bio);
    setErrors(found);
    setFormError(undefined);
    setSaved(false);
    if (found.fullName || found.bio) {
      return;
    }

    setSaving(true);
    try {
      const updated = await updateProfile(
        { fullName: fullName.trim(), bio: bio.trim() },
        profile.version,
      );
      setProfile(updated);
      setFullName(updated.fullName);
      setBio(updated.bio ?? '');
      updateUser({ fullName: updated.fullName });
      setSaved(true);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        setConflict(true);
      } else if (failure instanceof ApiError && failure.status === 400) {
        const server = { fullName: failure.errors.fullName?.[0], bio: failure.errors.bio?.[0] };
        setErrors(server);
        if (!server.fullName && !server.bio) {
          setFormError(describe(failure));
        }
      } else {
        setFormError(describe(failure));
      }
    } finally {
      setSaving(false);
    }
  }

  function handleReload() {
    setConflict(false);
    setSaved(false);
    setErrors({});
    setFormError(undefined);
    setProfile(undefined);
    setReload((value) => value + 1);
  }

  if (loadError) {
    return (
      <section className={styles.root} aria-labelledby="profile-heading">
        <h1 id="profile-heading">Profile</h1>
        <ErrorState
          title="Your profile could not be loaded"
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

  if (!profile) {
    return (
      <section className={styles.root} aria-labelledby="profile-heading">
        <h1 id="profile-heading">Profile</h1>
        <LoadingState message="Loading your profile…" />
      </section>
    );
  }

  return (
    <section className={styles.root} aria-labelledby="profile-heading">
      <h1 id="profile-heading">Profile</h1>

      <section className={styles.card} aria-labelledby="account-heading">
        <h2 id="account-heading">Account</h2>
        <dl className={styles.facts}>
          <dt>Email</dt>
          <dd data-testid="profile-email">{profile.email}</dd>
          <dt>Role</dt>
          <dd data-testid="profile-role">{profile.role}</dd>
          <dt>Registered on</dt>
          <dd data-testid="profile-registered">{date.format(new Date(profile.registeredAt))}</dd>
        </dl>
        <p className={styles.note}>Your email and role cannot be changed here.</p>
      </section>

      <section className={styles.card} aria-labelledby="details-heading">
        <h2 id="details-heading">Your details</h2>
        {conflict && (
          <div className={styles.alert} role="alert" data-testid="profile-conflict">
            <p>{CONFLICT_MESSAGE}</p>
            <button
              type="button"
              className={styles.secondary}
              onClick={handleReload}
              data-testid="profile-reload"
            >
              Reload
            </button>
          </div>
        )}
        {formError && (
          <div className={styles.alert} role="alert">
            <p>
              {formError.message}
              {formError.correlationId && (
                <>
                  {' '}
                  Reference: <code>{formError.correlationId}</code>
                </>
              )}
            </p>
          </div>
        )}
        <form className={styles.form} onSubmit={handleSubmit} noValidate>
          <FormField
            label="Full name"
            name="fullName"
            autoComplete="name"
            value={fullName}
            onChange={(value) => edit(() => setFullName(value), 'fullName')}
            required
            disabled={saving || conflict}
            hint={`${fullName.trim().length} / ${FULL_NAME_MAX} characters (at least ${FULL_NAME_MIN})`}
            error={errors.fullName}
            testId="profile-fullname"
          />
          <FormField
            label="Biography"
            name="bio"
            multiline
            rows={6}
            value={bio}
            onChange={(value) => edit(() => setBio(value), 'bio')}
            disabled={saving || conflict}
            hint={`${bio.trim().length} / ${BIO_MAX} characters`}
            error={errors.bio}
            testId="profile-bio"
          />
          <div className={styles.actions}>
            <button
              type="submit"
              className={styles.primary}
              disabled={saving || conflict}
              data-testid="profile-save"
            >
              {saving ? 'Saving…' : 'Save'}
            </button>
            <div role="status" data-testid="profile-status">
              {saved && <p className={styles.success}>{SAVED_MESSAGE}</p>}
            </div>
          </div>
        </form>
      </section>
    </section>
  );
}
