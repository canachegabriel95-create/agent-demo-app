export const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5080';
export const ADMIN_PORTAL_URL = process.env.E2E_ADMIN_PORTAL_URL ?? 'http://localhost:5173';
export const AUTHOR_PORTAL_URL = process.env.E2E_AUTHOR_PORTAL_URL ?? 'http://localhost:5174';

/** The provisioned administrator. There are no defaults: credentials are never kept in the repository. */
export function adminCredentials() {
  const email = process.env.E2E_ADMIN_EMAIL;
  const password = process.env.E2E_ADMIN_PASSWORD;
  if (!email || !password) {
    throw new Error('Set E2E_ADMIN_EMAIL and E2E_ADMIN_PASSWORD to the provisioned administrator.');
  }

  return { email, password };
}
