import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RouterProvider, createMemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { appRoutes } from './router';

const fetchMock = vi.fn<typeof fetch>();

function renderAt(path: string) {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] });
  render(<RouterProvider router={router} />);
  return router;
}

function healthResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(
    healthResponse({
      status: 'Healthy',
      checks: [
        { name: 'api', status: 'Healthy' },
        { name: 'database', status: 'Healthy' },
      ],
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('layout shell', () => {
  it('has a banner with the app name, a main navigation and a main region', async () => {
    renderAt('/');

    const banner = screen.getByRole('banner');
    expect(within(banner).getByText('Content Review – Admin')).toBeInTheDocument();
    expect(
      within(screen.getByRole('navigation', { name: 'Main' })).getAllByRole('link'),
    ).toHaveLength(2);
    expect(screen.getByRole('main')).toBeInTheDocument();
    expect(await screen.findByText(/API is Healthy/)).toBeInTheDocument();
  });

  it('offers a skip link to the main content', () => {
    renderAt('/login');

    expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute(
      'href',
      '#main-content',
    );
  });

  it('navigates between pages from the navigation', async () => {
    renderAt('/');

    await userEvent.click(screen.getByRole('link', { name: 'Sign in' }));

    expect(screen.getByRole('heading', { level: 1, name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('aria-current', 'page');
  });
});

describe('routes', () => {
  it('shows the login placeholder at /login', () => {
    renderAt('/login');

    expect(screen.getByRole('heading', { level: 1, name: 'Sign in' })).toBeInTheDocument();
  });

  it('shows a not-found page with a way home for unknown routes', async () => {
    renderAt('/does/not/exist');

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('link', { name: 'Go to the home page' }));

    expect(screen.getByRole('heading', { level: 1, name: 'Admin Portal' })).toBeInTheDocument();
  });
});

describe('API health indicator on the home page', () => {
  it('shows a loading state first, then the healthy checks', async () => {
    renderAt('/');

    expect(screen.getByRole('status')).toHaveTextContent('Checking the API…');
    expect(await screen.findByText('API is Healthy')).toBeInTheDocument();
    expect(screen.getByText('api: Healthy')).toBeInTheDocument();
    expect(screen.getByText('database: Healthy')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5080/health',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    );
  });

  it('shows which check is failing when the API answers 503', async () => {
    fetchMock.mockResolvedValue(
      healthResponse(
        {
          status: 'Unhealthy',
          checks: [
            { name: 'api', status: 'Healthy' },
            { name: 'database', status: 'Unhealthy' },
          ],
        },
        503,
      ),
    );

    renderAt('/');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('API unavailable');
    expect(alert).toHaveTextContent('The API reports a problem.');
    expect(screen.getByText('database: Unhealthy')).toBeInTheDocument();
  });

  it('explains when the API cannot be reached and lets the user retry', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));

    renderAt('/');

    expect(await screen.findByRole('alert')).toHaveTextContent('The API cannot be reached');

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('API is Healthy')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('does not report an error for a request that was cancelled by leaving the page', async () => {
    let rejectFetch: (reason: unknown) => void = () => {};
    fetchMock.mockImplementation(
      (_url, init) =>
        new Promise((_resolve, reject) => {
          rejectFetch = reject;
          init?.signal?.addEventListener('abort', () =>
            reject(new DOMException('Aborted', 'AbortError')),
          );
        }),
    );

    const router = renderAt('/');
    await router.navigate('/login');
    rejectFetch(new DOMException('Aborted', 'AbortError'));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
