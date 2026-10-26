import { Link } from 'react-router-dom';
import { EmptyState } from '../components';

export function NotFoundPage() {
  return (
    <>
      <h1>Page not found</h1>
      <EmptyState
        title="We could not find that page."
        description="The address may be mistyped or the page may have moved."
        action={<Link to="/">Go to the home page</Link>}
      />
    </>
  );
}
