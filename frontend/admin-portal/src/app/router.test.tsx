import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ADMIN,
  healthy,
  json,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';

let health: Handler;

function stubSignedInApi() {
  return stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /health': (init, url) => health(init, url),
    'GET /api/admin/dashboard': () =>
      json({
        activeAuthors: 0,
        suspendedAuthors: 0,
        availableInvitations: 0,
        pendingReviews: 0,
        publishedArticles: 0,
      }),
  });
}

beforeEach(() => {
  health = () => healthy();
  storeSession();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('layout shell', () => {
  it('has a banner with the app name, a main navigation and a main region', async () => {
    stubSignedInApi();
    renderAt('/');

    const banner = await screen.findByRole('banner');
    expect(within(banner).getByText('Content Review – Admin')).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Main' }))
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['Home', 'Review queue', 'Authors', 'Invitations']);
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(await screen.findByText(/API is Healthy/)).toBeInTheDocument();
  });

  it('offers a skip link to the main content', () => {
    stubSignedInApi();
    renderAt('/login');

    expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute(
      'href',
      '#main-content',
    );
  });
});

describe('routes', () => {
  it('shows the sign-in form at /login for a visitor without a session', () => {
    window.sessionStorage.clear();
    stubSignedInApi();
    renderAt('/login');

    expect(screen.getByRole('heading', { level: 1, name: 'Sign in' })).toBeInTheDocument();
  });

  it('shows a not-found page with a way home for unknown routes when signed in', async () => {
    stubSignedInApi();
    renderAt('/does/not/exist');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Page not found' }),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole('link', { name: 'Go to the home page' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Admin Portal' }),
    ).toBeInTheDocument();
  });
});

describe('API health indicator on the home page', () => {
  it('shows a loading state first, then the healthy checks', async () => {
    const fetchMock = stubSignedInApi();
    renderAt('/');

    expect(await screen.findByText('API is Healthy')).toBeInTheDocument();
    expect(screen.getByText('api: Healthy')).toBeInTheDocument();
    expect(screen.getByText('database: Healthy')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5080/health',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });

  it('shows which check is failing when the API answers 503', async () => {
    health = () =>
      json(
        {
          status: 'Unhealthy',
          checks: [
            { name: 'api', status: 'Healthy' },
            { name: 'database', status: 'Unhealthy' },
          ],
        },
        503,
      );
    stubSignedInApi();

    renderAt('/');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('API unavailable');
    expect(alert).toHaveTextContent('The API reports a problem.');
    expect(screen.getByText('database: Unhealthy')).toBeInTheDocument();
  });

  it('explains when the API cannot be reached and lets the user retry', async () => {
    let calls = 0;
    health = () => {
      calls += 1;
      return calls === 1 ? Promise.reject(new TypeError('Failed to fetch')) : healthy();
    };
    stubSignedInApi();

    renderAt('/');

    expect(await screen.findByRole('alert')).toHaveTextContent('The API cannot be reached');

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('API is Healthy')).toBeInTheDocument();
    expect(calls).toBe(2);
  });

  it('does not report an error for a request that was cancelled by leaving the page', async () => {
    let rejectFetch: (reason: unknown) => void = () => {};
    health = (init) =>
      new Promise((_resolve, reject) => {
        rejectFetch = reject;
        init?.signal?.addEventListener('abort', () =>
          reject(new DOMException('Aborted', 'AbortError')),
        );
      });
    stubSignedInApi();

    const router = renderAt('/');
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Checking the API…'));
    await router.navigate('/login');
    rejectFetch(new DOMException('Aborted', 'AbortError'));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
