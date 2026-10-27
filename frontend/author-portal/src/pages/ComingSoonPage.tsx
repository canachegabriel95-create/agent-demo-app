import { useLocation } from 'react-router-dom';
import { EmptyState } from '../components';

/** Placeholder for sections whose pages arrive with later stories (My Articles, Feed). */
export function ComingSoonPage({ title }: { title: string }) {
  // A page that sends the author here after an action (e.g. deleting a draft) passes a message to show.
  const message = (useLocation().state as { message?: string } | null)?.message;
  return (
    <>
      <h1>{title}</h1>
      {message && (
        <p role="status" data-testid="flash-message">
          {message}
        </p>
      )}
      <EmptyState
        title="Coming soon"
        description={`${title} will be available in a later release.`}
      />
    </>
  );
}
