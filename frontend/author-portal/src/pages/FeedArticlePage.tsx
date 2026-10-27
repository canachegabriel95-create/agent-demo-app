import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getFeedArticle, type FeedArticle } from '../api/feed';
import { EmptyState, ErrorState, LoadingState } from '../components';
import styles from './FeedArticlePage.module.css';

const dateTime = new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' });

type State =
  | { kind: 'loading' }
  | { kind: 'notFound' }
  | { kind: 'error'; message: string; correlationId?: string }
  | { kind: 'ready'; article: FeedArticle };

/**
 * A published article, read-only. It has no edit or delete controls, not even for its own author: changing an
 * article is done from My Articles, and a published one cannot be changed. Everything is shown as text.
 */
export function FeedArticlePage() {
  const { id } = useParams();
  const [reload, setReload] = useState(0);
  const [loaded, setLoaded] = useState<{ key: string; state: State }>();
  const key = `${id}|${reload}`;

  useEffect(() => {
    if (!id) {
      return;
    }

    const controller = new AbortController();
    getFeedArticle(id, controller.signal).then(
      (article) => setLoaded({ key, state: { kind: 'ready', article } }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setLoaded({
          key,
          state:
            error instanceof ApiError && error.status === 404
              ? { kind: 'notFound' }
              : {
                  kind: 'error',
                  message:
                    error instanceof ApiError
                      ? (error.detail ?? error.title)
                      : 'Something went wrong. Please try again.',
                  correlationId: error instanceof ApiError ? error.correlationId : undefined,
                },
        });
      },
    );

    return () => controller.abort();
  }, [id, key]);

  const state: State = loaded?.key === key ? loaded.state : { kind: 'loading' };

  if (state.kind === 'notFound') {
    return (
      <section className={styles.root} aria-labelledby="feed-article-heading">
        <h1 id="feed-article-heading">Article not found</h1>
        <EmptyState
          title="We could not find that article."
          description="It may not be published, or it may no longer exist."
          action={<Link to="/feed">Back to the feed</Link>}
        />
      </section>
    );
  }

  if (state.kind === 'error') {
    return (
      <section className={styles.root} aria-labelledby="feed-article-heading">
        <h1 id="feed-article-heading">Article</h1>
        <ErrorState
          title="The article could not be loaded"
          message={state.message}
          correlationId={state.correlationId}
          onRetry={() => setReload((value) => value + 1)}
        />
      </section>
    );
  }

  if (state.kind === 'loading') {
    return (
      <section className={styles.root} aria-labelledby="feed-article-heading">
        <h1 id="feed-article-heading">Article</h1>
        <LoadingState message="Loading the article…" />
      </section>
    );
  }

  const { article } = state;
  return (
    <article className={styles.root} aria-labelledby="feed-article-heading">
      <p>
        <Link to="/feed">← Back to the feed</Link>
      </p>
      <h1 id="feed-article-heading" className={styles.title} data-testid="feed-article-title">
        {article.title}
      </h1>
      <p className={styles.meta} data-testid="feed-article-meta">
        {article.category?.name ?? 'No category'} · {article.authorDisplayName} ·{' '}
        {dateTime.format(new Date(article.publishedAt))}
      </p>
      <p className={styles.summary} data-testid="feed-article-summary">
        {article.summary}
      </p>
      <div className={styles.body} data-testid="feed-article-body">
        {article.body}
      </div>
    </article>
  );
}
