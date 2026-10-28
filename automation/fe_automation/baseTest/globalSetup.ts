import { apiLogin } from './login';

// The API allows only 10 logins per minute, so each portal signs in once per run.
// Values set on process.env here are passed on to every worker.
export default async function globalSetup(): Promise<void> {
  process.env.ADMIN_SESSION_TOKEN = await apiLogin('admin');
  process.env.AUTHOR_SESSION_TOKEN = await apiLogin('author');
}
