import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getArticleStats, type ArticleStatusCounts } from '../api/myArticles';
import type { ArticleStatus } from '../api/articles';
import { ApiHealthIndicator } from '../components/ApiHealthIndicator';
import { ErrorState } from '../components';
import styles from './HomePage.module.css';

const CARDS: {
  status: ArticleStatus;
  key: keyof ArticleStatusCounts;
  label: string;
  testId: string;
}[] = [
  { status: 'Draft', key: 'draft', label: 'Draft', testId: 'stat-draft' },
  {
    status: 'PendingReview',
    key: 'pendingReview',
    label: 'Pending review',
    testId: 'stat-pending-review',
  },
  { status: 'Published', key: 'published', label: 'Published', testId: 'stat-published' },
  { status: 'Rejected', key: 'rejected', label: 'Rejected', testId: 'stat-rejected' },
];

export function attentionMessage(rejected: number) {
  return `${rejected} ${rejected === 1 ? 'article needs' : 'articles need'} your attention`;
}

type State =
  | { kind: 'loading' }
  | { kind: 'error'; message: string; correlationId?: string }
  | { kind: 'ready'; counts: ArticleStatusCounts };

export function HomePage() {
  const [state, setState] = useState<State>({ kind: 'loading' });
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    getArticleStats(controller.signal)
      .then((counts) => setState({ kind: 'ready', counts }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({
          kind: 'error',
          message:
            error instanceof ApiError ? error.message : 'The article counts could not be loaded.',
          correlationId: error instanceof ApiError ? error.correlationId : undefined,
        });
      });
    return () => controller.abort();
  }, [reload]);

  const retry = useCallback(() => {
    setState({ kind: 'loading' });
    setReload((n) => n + 1);
  }, []);

  return (
    <>
      <h1>Author Portal</h1>
      <p>Write articles, submit them for review and track their status.</p>
      <p>
        <Link to="/articles/new" className={styles.primary} data-testid="dashboard-new-article">
          New article
        </Link>
      </p>

      {state.kind === 'loading' && (
        <ul
          className={styles.cards}
          aria-busy="true"
          aria-label="Loading article counts"
          data-testid="stats-loading"
        >
          {CARDS.map((card) => (
            <li
              key={card.status}
              className={`${styles.card} ${styles.skeleton}`}
              aria-hidden="true"
            />
          ))}
        </ul>
      )}

      {state.kind === 'error' && (
        <ErrorState message={state.message} correlationId={state.correlationId} onRetry={retry} />
      )}

      {state.kind === 'ready' && (
        <>
          {state.counts.rejected > 0 && (
            <p role="status" className={styles.attention} data-testid="attention-message">
              {attentionMessage(state.counts.rejected)}
            </p>
          )}
          <ul className={styles.cards}>
            {CARDS.map((card) => (
              <li key={card.status}>
                <Link
                  to={`/articles?status=${card.status}`}
                  className={`${styles.card} ${
                    card.status === 'Rejected' && state.counts.rejected > 0 ? styles.highlight : ''
                  }`}
                  data-testid={card.testId}
                >
                  <span className={styles.count}>{state.counts[card.key]}</span>
                  <span className={styles.label}>{card.label}</span>
                </Link>
              </li>
            ))}
          </ul>
        </>
      )}

      <ApiHealthIndicator />
    </>
  );
}
