import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getUser, type UserDetails } from '../api/users';
import { ErrorState, LoadingState } from '../components';
import { describeError, formatDate, formatDateTime } from './invitationFormat';
import styles from './UserDetailPage.module.css';

export const NOT_FOUND_TITLE = 'Author not found';

interface Result {
  key: string;
  user?: UserDetails;
  error?: unknown;
}

const COUNT_TILES = [
  { key: 'draft', label: 'Draft' },
  { key: 'pendingReview', label: 'Pending Review' },
  { key: 'published', label: 'Published' },
  { key: 'rejected', label: 'Rejected' },
] as const;

export function UserDetailPage() {
  const { id = '' } = useParams();
  const [reload, setReload] = useState(0);
  const [result, setResult] = useState<Result>();

  const requestKey = `${id}|${reload}`;
  useEffect(() => {
    const controller = new AbortController();
    getUser(id, controller.signal).then(
      (user) => setResult({ key: requestKey, user }),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setResult({ key: requestKey, error });
        }
      },
    );

    return () => controller.abort();
  }, [id, requestKey]);

  const current = result?.key === requestKey ? result : undefined;
  const backLink = (
    <Link to="/users" className={styles.back}>
      ← Back to authors
    </Link>
  );

  if (current?.error) {
    if (current.error instanceof ApiError && current.error.status === 404) {
      return (
        <section aria-labelledby="user-heading">
          {backLink}
          <h1 id="user-heading">{NOT_FOUND_TITLE}</h1>
          <p>There is no author with this id. They may have been removed, or the link is wrong.</p>
        </section>
      );
    }

    const { message, correlationId } = describeError(current.error);
    return (
      <section aria-label="Author">
        {backLink}
        <ErrorState
          title="The author could not be loaded"
          message={message}
          correlationId={correlationId}
          onRetry={() => setReload((value) => value + 1)}
        />
      </section>
    );
  }

  if (!current?.user) {
    return (
      <section aria-label="Author">
        {backLink}
        <LoadingState message="Loading author…" />
      </section>
    );
  }

  const user = current.user;
  return (
    <section aria-labelledby="user-heading">
      {backLink}
      <div className={styles.titleRow}>
        <h1 id="user-heading">{user.fullName}</h1>
        <span className={styles.badge} data-status={user.status} data-testid="user-status">
          {user.status}
        </span>
      </div>

      {user.status === 'Suspended' && (
        <section
          className={`${styles.card} ${styles.suspension}`}
          aria-labelledby="suspension-heading"
          data-testid="user-suspension"
        >
          <h2 id="suspension-heading">Suspended</h2>
          <dl className={styles.facts}>
            {user.suspendedAt && (
              <>
                <dt>Since</dt>
                <dd>{formatDateTime(user.suspendedAt)}</dd>
              </>
            )}
            <dt>Reason</dt>
            <dd className={styles.bio} data-testid="user-suspension-reason">
              {user.suspensionReason}
            </dd>
          </dl>
        </section>
      )}

      <section className={styles.card} aria-labelledby="profile-heading">
        <h2 id="profile-heading">Profile</h2>
        <dl className={styles.facts}>
          <dt>Name</dt>
          <dd data-testid="user-full-name">{user.fullName}</dd>
          <dt>Email</dt>
          <dd data-testid="user-email">{user.email}</dd>
          <dt>Role</dt>
          <dd>{user.role}</dd>
          <dt>Registered</dt>
          <dd>{formatDate(user.registeredAt)}</dd>
          <dt>Biography</dt>
          <dd className={styles.bio} data-testid="user-bio">
            {user.bio ?? '—'}
          </dd>
        </dl>
      </section>

      <section className={styles.card} aria-labelledby="articles-heading">
        <h2 id="articles-heading">Articles</h2>
        <ul className={styles.tiles}>
          {COUNT_TILES.map(({ key, label }) => (
            <li key={key} className={styles.tile} data-testid={`user-count-${key}`}>
              <span className={styles.tileLabel}>{label}</span>
              <span className={styles.tileValue}>{user.articleCounts[key]}</span>
            </li>
          ))}
        </ul>
      </section>
    </section>
  );
}
