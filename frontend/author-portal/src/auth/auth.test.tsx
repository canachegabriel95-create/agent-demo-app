import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SESSION_KEY } from '../app/config';
import {
  AUTHOR,
  healthy,
  json,
  problem,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';

afterEach(() => {
  vi.unstubAllGlobals();
});

const me: Handler = () => json({ ...AUTHOR, status: 'Active' });
const loginOk: Handler = () =>
  json({ token: 'new-token', expiresAt: '2026-10-27T17:00:00Z', user: AUTHOR });
const health: Handler = () => healthy();

async function fillAndSubmit(email = 'author@demo.local', password = 'Str0ng!Passw0rd#1') {
  const user = userEvent.setup();
  if (email) await user.type(screen.getByLabelText(/^Email/), email);
  if (password) await user.type(screen.getByLabelText(/^Password/), password);
  await user.click(screen.getByTestId('login-submit'));
}

describe('route guard', () => {
  it('sends an anonymous visitor to /login with the page they wanted as returnUrl', async () => {
    stubApi({});
    const router = renderAt('/users?page=2');

    await screen.findByRole('heading', { level: 1, name: 'Sign in' });
    expect(router.state.location.pathname).toBe('/login');
    expect(router.state.location.search).toBe('?returnUrl=%2Fusers%3Fpage%3D2');
  });

  it('protects unknown pages too', async () => {
    stubApi({});
    const router = renderAt('/some/secret/page');

    await screen.findByRole('heading', { level: 1, name: 'Sign in' });
    expect(router.state.location.search).toBe('?returnUrl=%2Fsome%2Fsecret%2Fpage');
  });

  it('shows protected pages again after a reload when the stored session is still valid', async () => {
    storeSession();
    const fetchMock = stubApi({ 'GET /api/auth/me': me, 'GET /health': health });
    renderAt('/');

    expect(await screen.findByRole('heading', { level: 1, name: 'Author Portal' })).toBeVisible();
    expect(screen.getByTestId('current-user')).toHaveTextContent('Jane Author');
    const meCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/api/auth/me'))!;
    expect(new Headers(meCall[1]!.headers).get('Authorization')).toBe('Bearer valid-token');
  });

  it('shows a loading state while the stored session is being validated', async () => {
    storeSession();
    stubApi({ 'GET /api/auth/me': () => new Promise(() => {}) });
    renderAt('/');

    expect(await screen.findByRole('status')).toHaveTextContent('Checking your session…');
    expect(screen.queryByRole('heading', { name: 'Author Portal' })).not.toBeInTheDocument();
  });

  it('treats an invalid stored session as signed out and explains that it expired', async () => {
    storeSession('stale');
    stubApi({ 'GET /api/auth/me': () => problem(401, 'Authentication is required.') });
    const router = renderAt('/');

    expect(await screen.findByTestId('login-expired')).toHaveTextContent(
      'Your session has expired. Please sign in again.',
    );
    expect(router.state.location.pathname).toBe('/login');
    expect(window.sessionStorage.getItem(SESSION_KEY)).toBeNull();
  });

  it('sends a signed-in administrator who opens /login to the home page', async () => {
    storeSession();
    stubApi({ 'GET /api/auth/me': me, 'GET /health': health });
    const router = renderAt('/login');

    await screen.findByRole('heading', { level: 1, name: 'Author Portal' });
    expect(router.state.location.pathname).toBe('/');
  });
});

describe('login form', () => {
  it('has labelled fields, the right input types and an accessible submit button', () => {
    stubApi({});
    renderAt('/login');

    expect(screen.getByLabelText(/^Email/)).toHaveAttribute('type', 'email');
    expect(screen.getByLabelText(/^Email/)).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByLabelText(/^Password/)).toHaveAttribute('type', 'password');
    expect(screen.getByLabelText(/^Password/)).toHaveAttribute('autocomplete', 'current-password');
    expect(screen.getByRole('button', { name: 'Sign in' })).toBe(
      screen.getByTestId('login-submit'),
    );
  });

  it('signs in as an administrator and lands on the home page', async () => {
    const fetchMock = stubApi({
      'POST /api/auth/login': loginOk,
      'GET /health': health,
    });
    const router = renderAt('/login');

    await fillAndSubmit('  author@demo.local ');

    expect(await screen.findByRole('heading', { level: 1, name: 'Author Portal' })).toBeVisible();
    expect(router.state.location.pathname).toBe('/');
    expect(screen.getByTestId('current-user')).toHaveTextContent('Jane Author');
    expect(window.sessionStorage.getItem(SESSION_KEY)).toBe('new-token');

    const call = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/api/auth/login'))!;
    expect(JSON.parse(String(call[1]!.body))).toEqual({
      email: 'author@demo.local',
      password: 'Str0ng!Passw0rd#1',
      portal: 'Author',
    });
  });

  it('returns to the page the administrator wanted', async () => {
    stubApi({ 'POST /api/auth/login': loginOk, 'GET /health': health });
    const router = renderAt('/login?returnUrl=%2Fusers%3Fpage%3D2');

    await fillAndSubmit();

    await waitFor(() => expect(router.state.location.pathname).toBe('/users'));
    expect(router.state.location.search).toBe('?page=2');
  });

  it.each([
    ['an absolute URL', 'https://evil.example/phish'],
    ['a protocol-relative URL', '//evil.example/phish'],
    ['a backslash trick', '/\\evil.example'],
    ['a script URL', 'javascript:alert(1)'],
  ])('ignores %s as returnUrl and lands on the home page', async (_name, returnUrl) => {
    stubApi({ 'POST /api/auth/login': loginOk, 'GET /health': health });
    const router = renderAt(`/login?returnUrl=${encodeURIComponent(returnUrl)}`);

    await fillAndSubmit();

    await screen.findByRole('heading', { level: 1, name: 'Author Portal' });
    expect(router.state.location.pathname).toBe('/');
  });

  it('shows the generic message in an alert for wrong credentials', async () => {
    stubApi({ 'POST /api/auth/login': () => problem(401, 'Invalid email or password.') });
    renderAt('/login');

    await fillAndSubmit('author@demo.local', 'Wrong!Passw0rd#1');

    const alert = await screen.findByRole('alert');
    expect(alert).toBe(screen.getByTestId('login-error'));
    expect(alert).toHaveTextContent('Invalid email or password.');
    expect(window.sessionStorage.getItem(SESSION_KEY)).toBeNull();
    expect(screen.getByTestId('login-submit')).toBeEnabled();
  });

  it('shows the same message for an Administrator account, whatever the server says', async () => {
    stubApi({ 'POST /api/auth/login': () => problem(401, 'Something else') });
    renderAt('/login');

    await fillAndSubmit('admin@demo.local');

    expect(await screen.findByTestId('login-error')).toHaveTextContent(
      'Invalid email or password.',
    );
  });

  it('does not treat a failed sign-in as an expired session', async () => {
    stubApi({ 'POST /api/auth/login': () => problem(401, 'Invalid email or password.') });
    renderAt('/login');

    await fillAndSubmit();

    await screen.findByTestId('login-error');
    expect(screen.queryByTestId('login-expired')).not.toBeInTheDocument();
  });

  it('tells the user to wait when sign-in is rate limited', async () => {
    stubApi({ 'POST /api/auth/login': () => problem(429, 'Too many login attempts.') });
    renderAt('/login');

    await fillAndSubmit();

    expect(await screen.findByTestId('login-error')).toHaveTextContent('Too many sign-in attempts');
  });

  it('shows a neutral message when the API cannot be reached', async () => {
    stubApi({ 'POST /api/auth/login': () => Promise.reject(new TypeError('Failed to fetch')) });
    renderAt('/login');

    await fillAndSubmit();

    expect(await screen.findByTestId('login-error')).toHaveTextContent(
      'Unable to sign in right now',
    );
  });

  it('requires both fields before calling the API', async () => {
    const fetchMock = stubApi({});
    renderAt('/login');

    await userEvent.click(screen.getByTestId('login-submit'));

    expect(await screen.findByText('Email is required.')).toBeVisible();
    expect(screen.getByText('Password is required.')).toBeVisible();
    expect(screen.getByLabelText(/^Email/)).toBeInvalid();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('disables the button and the fields while the request is running, and prevents double submit', async () => {
    let finish: (response: Response) => void = () => {};
    const fetchMock = stubApi({
      'POST /api/auth/login': () => new Promise<Response>((resolve) => (finish = resolve)),
      'GET /health': health,
    });
    renderAt('/login');
    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/^Email/), 'author@demo.local');
    await user.type(screen.getByLabelText(/^Password/), 'Str0ng!Passw0rd#1');

    await user.click(screen.getByTestId('login-submit'));
    await user.click(screen.getByTestId('login-submit'));

    expect(screen.getByTestId('login-submit')).toBeDisabled();
    expect(screen.getByLabelText(/^Email/)).toBeDisabled();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    finish(json({ token: 't', expiresAt: '2026-10-27T17:00:00Z', user: AUTHOR }));
    await screen.findByRole('heading', { level: 1, name: 'Author Portal' });
  });
});

describe('logout', () => {
  async function signedIn(logout: Handler) {
    storeSession();
    const fetchMock = stubApi({
      'GET /api/auth/me': me,
      'GET /health': health,
      'POST /api/auth/logout': logout,
    });
    const router = renderAt('/');
    await screen.findByTestId('logout-button');
    return { fetchMock, router };
  }

  it('revokes the session on the server, clears storage and shows the login page', async () => {
    const { fetchMock, router } = await signedIn(() => new Response(null, { status: 204 }));

    await userEvent.click(screen.getByTestId('logout-button'));

    await screen.findByRole('heading', { level: 1, name: 'Sign in' });
    expect(router.state.location.pathname).toBe('/login');
    expect(window.sessionStorage.getItem(SESSION_KEY)).toBeNull();
    expect(screen.queryByTestId('logout-button')).not.toBeInTheDocument();
    const logoutCall = fetchMock.mock.calls.find(([url]) =>
      String(url).endsWith('/api/auth/logout'),
    )!;
    expect(logoutCall[1]!.method).toBe('POST');
    expect(new Headers(logoutCall[1]!.headers).get('Authorization')).toBe('Bearer valid-token');
  });

  it('still signs out locally when the API call fails', async () => {
    const { router } = await signedIn(() => Promise.reject(new TypeError('Failed to fetch')));

    await userEvent.click(screen.getByTestId('logout-button'));

    await screen.findByRole('heading', { level: 1, name: 'Sign in' });
    expect(router.state.location.pathname).toBe('/login');
    expect(window.sessionStorage.getItem(SESSION_KEY)).toBeNull();
  });

  it('still signs out locally when the server session is already gone (401)', async () => {
    await signedIn(() => problem(401, 'Authentication is required.'));

    await userEvent.click(screen.getByTestId('logout-button'));

    await screen.findByRole('heading', { level: 1, name: 'Sign in' });
    expect(window.sessionStorage.getItem(SESSION_KEY)).toBeNull();
  });

  it('does not show protected pages after logging out', async () => {
    const { router } = await signedIn(() => new Response(null, { status: 204 }));
    await userEvent.click(screen.getByTestId('logout-button'));
    await screen.findByRole('heading', { level: 1, name: 'Sign in' });

    await router.navigate('/');

    await waitFor(() => expect(router.state.location.search).toBe('?returnUrl=%2F'));
    expect(screen.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Author Portal' })).not.toBeInTheDocument();
  });
});

describe('expired session', () => {
  it('clears the session and shows the expiry message when any API call returns 401', async () => {
    storeSession();
    let calls = 0;
    stubApi({
      // Valid at start, rejected later (e.g. suspended or revoked in the meantime).
      'GET /api/auth/me': () =>
        ++calls === 1 ? me(undefined, '') : problem(401, 'Authentication is required.'),
      'GET /health': () => problem(401, 'Authentication is required.'),
    });
    const router = renderAt('/');

    expect(await screen.findByTestId('login-expired')).toHaveTextContent(
      'Your session has expired. Please sign in again.',
    );
    expect(router.state.location.pathname).toBe('/login');
    expect(router.state.location.search).toContain('returnUrl=');
    expect(window.sessionStorage.getItem(SESSION_KEY)).toBeNull();
  });

  it('removes the expiry message after a successful sign-in and logout', async () => {
    storeSession('stale');
    stubApi({
      'GET /api/auth/me': () => problem(401, 'Authentication is required.'),
      'POST /api/auth/login': loginOk,
      'GET /health': health,
      'POST /api/auth/logout': () => new Response(null, { status: 204 }),
    });
    renderAt('/');
    await screen.findByTestId('login-expired');

    await fillAndSubmit();
    await userEvent.click(await screen.findByTestId('logout-button'));

    await screen.findByRole('heading', { level: 1, name: 'Sign in' });
    expect(screen.queryByTestId('login-expired')).not.toBeInTheDocument();
  });
});

describe('registration entry points', () => {
  it('links to the registration page from the login page', async () => {
    stubApi({});
    const router = renderAt('/login');

    const link = screen.getByTestId('register-link');
    expect(link).toHaveTextContent('Have an invitation? Create an account');
    expect(link).toHaveAttribute('href', '/register');

    await userEvent.click(link);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Create an account' }),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe('/register');
  });

  it('lets an anonymous visitor open /register without being sent to the login page', async () => {
    stubApi({});
    const router = renderAt('/register');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Create an account' }),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe('/register');
  });

  it('shows a success banner after registration (?registered=1)', () => {
    stubApi({});
    renderAt('/login?registered=1');

    expect(screen.getByTestId('login-registered')).toHaveTextContent(
      'Your account has been created. Sign in to continue.',
    );
    expect(screen.getByRole('status')).toBe(screen.getByTestId('login-registered'));
  });

  it.each(['/login', '/login?registered=0', '/login?registered=yes'])(
    'shows no banner at %s',
    (url) => {
      stubApi({});
      renderAt(url);

      expect(screen.queryByTestId('login-registered')).not.toBeInTheDocument();
    },
  );

  it('hides the banner when a sign-in fails, and keeps the returnUrl after registration', async () => {
    stubApi({ 'POST /api/auth/login': () => problem(401, 'Invalid email or password.') });
    renderAt('/login?registered=1');

    await fillAndSubmit('author@demo.local', 'Wrong!Passw0rd#1');

    await screen.findByTestId('login-error');
    expect(screen.queryByTestId('login-registered')).not.toBeInTheDocument();
  });
});
