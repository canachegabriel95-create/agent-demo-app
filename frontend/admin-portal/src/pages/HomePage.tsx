import { ApiHealthIndicator } from '../components/ApiHealthIndicator';

export function HomePage() {
  return (
    <>
      <h1>Admin Portal</h1>
      <p>Manage invitations, users and article reviews.</p>
      <ApiHealthIndicator />
    </>
  );
}
