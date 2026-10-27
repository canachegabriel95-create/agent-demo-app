import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ApiError, setUnauthorizedHandler } from '../api/client';
import * as authApi from '../api/auth';
import type { AuthUser } from '../api/auth';
import { clearToken, getToken, setToken } from '../api/session';
import { AuthContext, type AuthContextValue, type AuthStatus } from './AuthContext';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>(() => (getToken() ? 'loading' : 'anonymous'));
  const [user, setUser] = useState<AuthUser | null>(null);
  const [sessionExpired, setSessionExpired] = useState(false);

  // Any 401 from the API means the stored session is no longer valid: forget it and let the route guard
  // send the user to the login page, which explains what happened.
  useEffect(
    () =>
      setUnauthorizedHandler(() => {
        clearToken();
        setUser(null);
        setStatus('anonymous');
        setSessionExpired(true);
      }),
    [],
  );

  // Validate a session left in sessionStorage (page reload) before showing protected pages.
  useEffect(() => {
    if (!getToken()) {
      return;
    }

    const controller = new AbortController();
    authApi
      .fetchCurrentUser(controller.signal)
      .then((current) => {
        setUser(current);
        setStatus('authenticated');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        // A 401 was already handled above; for other failures (API down) stay signed out but keep the token.
        if (!(error instanceof ApiError) || error.status !== 401) {
          setStatus('anonymous');
        }
      });

    return () => controller.abort();
  }, []);

  const signIn = useCallback(async (email: string, password: string) => {
    // A stale token must not be sent with the login request (and must not trigger the expiry handling).
    clearToken();
    const result = await authApi.login(email, password);
    setToken(result.token);
    setUser(result.user);
    setSessionExpired(false);
    setStatus('authenticated');
  }, []);

  const signOut = useCallback(async () => {
    try {
      await authApi.logout();
    } catch {
      // The server session may already be gone or unreachable; the local session must end regardless.
    }

    clearToken();
    setUser(null);
    setSessionExpired(false);
    setStatus('anonymous');
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, sessionExpired, signIn, signOut }),
    [status, user, sessionExpired, signIn, signOut],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
