import { useState, type FormEvent } from 'react';
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { sanitizeReturnUrl } from '../auth/returnUrl';
import { FormField } from '../components';
import styles from './LoginPage.module.css';

export const INVALID_CREDENTIALS_MESSAGE = 'Invalid email or password.';
export const SESSION_EXPIRED_MESSAGE = 'Your session has expired. Please sign in again.';

function messageFor(error: unknown): string {
  if (error instanceof ApiError) {
    // Wrong password, unknown email, suspended account and an Author account all give the same 401.
    if (error.status === 401) {
      return INVALID_CREDENTIALS_MESSAGE;
    }

    if (error.status === 429) {
      return 'Too many sign-in attempts. Please wait a minute and try again.';
    }
  }

  return 'Unable to sign in right now. Please try again.';
}

export function LoginPage() {
  const { status, signIn, sessionExpired } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const returnUrl = sanitizeReturnUrl(params.get('returnUrl'));

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [fieldErrors, setFieldErrors] = useState<{ email?: string; password?: string }>({});
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (status === 'authenticated') {
    return <Navigate to={returnUrl} replace />;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) {
      return;
    }

    const trimmedEmail = email.trim();
    const errors = {
      email: trimmedEmail ? undefined : 'Email is required.',
      password: password ? undefined : 'Password is required.',
    };
    setFieldErrors(errors);
    setError(null);
    if (errors.email || errors.password) {
      return;
    }

    setSubmitting(true);
    try {
      await signIn(trimmedEmail, password);
      navigate(returnUrl, { replace: true });
    } catch (failure) {
      setError(messageFor(failure));
      setSubmitting(false);
    }
  }

  return (
    <section className={styles.root} aria-labelledby="login-heading">
      <h1 id="login-heading">Sign in</h1>
      {sessionExpired && !error && (
        <p className={styles.notice} role="status" data-testid="login-expired">
          {SESSION_EXPIRED_MESSAGE}
        </p>
      )}
      <form className={styles.form} onSubmit={handleSubmit} noValidate>
        {error && (
          <p className={styles.error} role="alert" data-testid="login-error">
            {error}
          </p>
        )}
        <FormField
          label="Email"
          name="email"
          type="email"
          autoComplete="username"
          value={email}
          onChange={setEmail}
          required
          disabled={submitting}
          error={fieldErrors.email}
        />
        <FormField
          label="Password"
          name="password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={setPassword}
          required
          disabled={submitting}
          error={fieldErrors.password}
        />
        <button
          type="submit"
          className={styles.submit}
          disabled={submitting}
          data-testid="login-submit"
        >
          {submitting ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </section>
  );
}
