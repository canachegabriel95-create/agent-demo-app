import { SESSION_KEY } from '../app/config';

// sessionStorage can throw (blocked storage, private windows); the app must still work without it.

export function getToken(): string | null {
  try {
    return window.sessionStorage.getItem(SESSION_KEY);
  } catch {
    return null;
  }
}

export function setToken(token: string): void {
  try {
    window.sessionStorage.setItem(SESSION_KEY, token);
  } catch {
    // ignore: the user simply has to sign in again
  }
}

export function clearToken(): void {
  try {
    window.sessionStorage.removeItem(SESSION_KEY);
  } catch {
    // ignore
  }
}
