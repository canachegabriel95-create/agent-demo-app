/** Where the local stack listens and which administrator the tests sign in as (never a real account). */
export const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5080';
export const ADMIN_URL = process.env.E2E_ADMIN_URL ?? 'http://localhost:5173';
export const AUTHOR_URL = process.env.E2E_AUTHOR_URL ?? 'http://localhost:5174';
export const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@demo.local';
// The provisioned administrator's password is yours to set; this is the documented demo value.
export const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'Str0ng!Passw0rd#1';

export const ADMIN_SESSION_KEY = 'cr.admin.session';
export const AUTHOR_SESSION_KEY = 'cr.author.session';
