import { ApiHealthIndicator } from '../components/ApiHealthIndicator';

export function HomePage() {
  return (
    <>
      <h1>Author Portal</h1>
      <p>Write articles, submit them for review and track their status.</p>
      <ApiHealthIndicator />
    </>
  );
}
