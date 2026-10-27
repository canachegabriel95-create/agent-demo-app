import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { LoadingState } from '../components';
import { useAuth } from './useAuth';

/** Route guard: anonymous visitors are sent to the login page and brought back after signing in. */
export function RequireAuth() {
  const { status } = useAuth();
  const location = useLocation();

  if (status === 'loading') {
    return <LoadingState message="Checking your session…" />;
  }

  if (status === 'anonymous') {
    const returnUrl = `${location.pathname}${location.search}${location.hash}`;
    return <Navigate to={`/login?returnUrl=${encodeURIComponent(returnUrl)}`} replace />;
  }

  return <Outlet />;
}
