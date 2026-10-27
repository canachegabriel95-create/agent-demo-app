import { apiFetch } from './client';

/** Role names as serialized by the API. */
export type UserRole = 'Administrator' | 'Author';

export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
  role: UserRole;
}

export interface LoginResult {
  token: string;
  expiresAt: string;
  user: AuthUser;
}

/** Signs in to the Author portal. A wrong password, an unknown email and an Administrator account all give the same 401. */
export function login(email: string, password: string): Promise<LoginResult> {
  return apiFetch<LoginResult>('/api/auth/login', {
    method: 'POST',
    body: { email, password, portal: 'Author' },
  });
}

/** Validates the stored session and returns its user. */
export function fetchCurrentUser(signal?: AbortSignal): Promise<AuthUser> {
  return apiFetch<AuthUser>('/api/auth/me', { signal });
}

/** Revokes the current session on the server. */
export function logout(): Promise<void> {
  return apiFetch<void>('/api/auth/logout', { method: 'POST' });
}
