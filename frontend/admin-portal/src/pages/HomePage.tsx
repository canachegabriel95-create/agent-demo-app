import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getDashboard, type AdminDashboard } from '../api/dashboard';
import { ApiHealthIndicator } from '../components/ApiHealthIndicator';
import { ErrorState } from '../components';
import styles from './HomePage.module.css';

interface Card {
  key: keyof AdminDashboard;
  label: string;
  testId: string;
  /** Where the card leads: the list the number counts. */
  to: string;
}

// There is no administrator article list: the published articles lead to the approvals in the audit history.
export const CARDS: Card[] = [
  {
    key: 'activeAuthors',
    label: 'Active authors',
    testId: 'dash-active-authors',
    to: '/users?status=Active',
  },
  {
    key: 'suspendedAuthors',
    label: 'Suspended authors',
    testId: 'dash-suspended-authors',
    to: '/users?status=Suspended',
  },
  {
    key: 'availableInvitations',
    label: 'Available invitations',
    testId: 'dash-available-invitations',
    to: '/invitations?status=Available',
  },
  {
    key: 'pendingReviews',
    label: 'Pending reviews',
    testId: 'dash-pending-reviews',
    to: '/reviews',
  },
  {
    key: 'publishedArticles',
    label: 'Published articles',
    testId: 'dash-published-articles',
    to: '/audit?action=ArticleApproved',
  },
];

type State =
  | { kind: 'loading' }
  | { kind: 'error'; message: string; correlationId?: string }
  | { kind: 'ready'; data: AdminDashboard };

export function HomePage() {
  const [state, setState] = useState<State>({ kind: 'loading' });
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    getDashboard(controller.signal).then(
      (data) => setState({ kind: 'ready', data }),
      (error: unknown) => {
        if (controller.signal.aborted) return;
        // A refresh that fails keeps the numbers already shown; only a first load (or a retry) shows the error.
        setState((previous) =>
          previous.kind === 'ready'
            ? previous
            : {
                kind: 'error',
                message:
                  error instanceof ApiError
                    ? (error.detail ?? error.title)
                    : 'The dashboard could not be loaded.',
                correlationId: error instanceof ApiError ? error.correlationId : undefined,
              },
        );
      },
    );
    return () => controller.abort();
  }, [reload]);

  // Coming back to the tab (after working in another page or window) shows the current numbers.
  useEffect(() => {
    const refresh = () => setReload((n) => n + 1);
    window.addEventListener('focus', refresh);
    return () => window.removeEventListener('focus', refresh);
  }, []);

  const retry = useCallback(() => {
    setState({ kind: 'loading' });
    setReload((n) => n + 1);
  }, []);

  return (
    <>
      <h1>Admin Portal</h1>
      <p>Manage invitations, users and article reviews.</p>

      {state.kind === 'loading' && (
        <ul
          className={styles.cards}
          aria-busy="true"
          aria-label="Loading the dashboard"
          data-testid="dash-loading"
        >
          {CARDS.map((card) => (
            <li key={card.key} className={`${styles.card} ${styles.skeleton}`} aria-hidden="true" />
          ))}
        </ul>
      )}

      {state.kind === 'error' && (
        <ErrorState
          title="The dashboard could not be loaded"
          message={state.message}
          correlationId={state.correlationId}
          onRetry={retry}
        />
      )}

      {state.kind === 'ready' && (
        <ul className={styles.cards}>
          {CARDS.map((card) => (
            <li key={card.key}>
              <Link to={card.to} className={styles.card} data-testid={card.testId}>
                <span className={styles.count}>{state.data[card.key]}</span>
                <span className={styles.label}>{card.label}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      <ApiHealthIndicator />
    </>
  );
}
