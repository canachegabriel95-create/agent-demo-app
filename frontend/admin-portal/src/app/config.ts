export const APP_NAME = 'Content Review – Admin';

/** sessionStorage key holding the bearer token for this portal. */
export const SESSION_KEY = 'cr.admin.session';

export const DEFAULT_API_BASE_URL = 'http://localhost:5080';

export function getApiBaseUrl(): string {
  return import.meta.env.VITE_API_BASE_URL?.trim() || DEFAULT_API_BASE_URL;
}
