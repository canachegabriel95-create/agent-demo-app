import { EmptyState } from '../components';

/** Placeholder for sections whose pages arrive with later stories (My Articles, Feed). */
export function ComingSoonPage({ title }: { title: string }) {
  return (
    <>
      <h1>{title}</h1>
      <EmptyState
        title="Coming soon"
        description={`${title} will be available in a later release.`}
      />
    </>
  );
}
