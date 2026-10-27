import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ADMIN,
  healthy,
  json,
  problem,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';

const NUMBERS = {
  activeAuthors: 12,
  suspendedAuthors: 3,
  availableInvitations: 5,
  pendingReviews: 7,
  publishedArticles: 40,
};

let dashboard: Handler;
let fetchMock: ReturnType<typeof stubApi>;

function dashboardCalls() {
  return fetchMock.mock.calls.filter(
    ([input]) => new URL(String(input)).pathname === '/api/admin/dashboard',
  ).length;
}

beforeEach(() => {
  storeSession();
  dashboard = () => json(NUMBERS);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function open() {
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /health': () => healthy(),
    'GET /api/admin/dashboard': (init, url) => dashboard(init, url),
  });
  return renderAt('/');
}

describe('dashboard', () => {
  it('shows the five numbers', async () => {
    open();

    expect(await screen.findByTestId('dash-active-authors')).toHaveTextContent('12');
    expect(screen.getByTestId('dash-active-authors')).toHaveTextContent('Active authors');
    expect(screen.getByTestId('dash-suspended-authors')).toHaveTextContent('3');
    expect(screen.getByTestId('dash-available-invitations')).toHaveTextContent('5');
    expect(screen.getByTestId('dash-pending-reviews')).toHaveTextContent('7');
    expect(screen.getByTestId('dash-published-articles')).toHaveTextContent('40');
  });

  it('shows zeros as zeros', async () => {
    dashboard = () =>
      json({
        activeAuthors: 0,
        suspendedAuthors: 0,
        availableInvitations: 0,
        pendingReviews: 0,
        publishedArticles: 0,
      });
    open();

    expect(await screen.findByTestId('dash-pending-reviews')).toHaveTextContent('0');
  });

  it.each([
    ['dash-active-authors', '/users?status=Active'],
    ['dash-suspended-authors', '/users?status=Suspended'],
    ['dash-available-invitations', '/invitations?status=Available'],
    ['dash-pending-reviews', '/reviews'],
    ['dash-published-articles', '/audit?action=ArticleApproved'],
  ])('%s links to %s', async (testId, href) => {
    open();

    expect(await screen.findByTestId(testId)).toHaveAttribute('href', href);
  });

  it('opens the review queue from the Pending reviews card', async () => {
    const router = open();

    await userEvent.click(await screen.findByTestId('dash-pending-reviews'));

    expect(router.state.location.pathname).toBe('/reviews');
  });

  it('opens the filtered list of a card', async () => {
    const router = open();

    await userEvent.click(await screen.findByTestId('dash-available-invitations'));

    expect(router.state.location.pathname).toBe('/invitations');
    expect(router.state.location.search).toBe('?status=Available');
  });

  it('shows a loading skeleton until the numbers arrive', async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    dashboard = async () => {
      await gate;
      return json(NUMBERS);
    };
    open();

    expect(await screen.findByTestId('dash-loading')).toBeInTheDocument();
    expect(screen.queryByTestId('dash-active-authors')).not.toBeInTheDocument();

    release();
    expect(await screen.findByTestId('dash-active-authors')).toHaveTextContent('12');
    expect(screen.queryByTestId('dash-loading')).not.toBeInTheDocument();
  });

  it('shows an error and loads the numbers again on retry', async () => {
    let calls = 0;
    dashboard = () => {
      calls += 1;
      return calls === 1 ? problem(500, 'Boom') : json(NUMBERS);
    };
    open();

    const alert = await screen.findByText('The dashboard could not be loaded');
    expect(alert).toBeInTheDocument();
    expect(screen.queryByTestId('dash-active-authors')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('dash-active-authors')).toHaveTextContent('12');
  });

  it('refreshes the numbers when the window regains focus and keeps them if that fails', async () => {
    let current = NUMBERS;
    let failing = false;
    dashboard = () => (failing ? problem(500, 'Boom') : json(current));
    open();
    expect(await screen.findByTestId('dash-pending-reviews')).toHaveTextContent('7');

    current = { ...NUMBERS, pendingReviews: 8 };
    act(() => {
      window.dispatchEvent(new Event('focus'));
    });
    await waitFor(() => expect(screen.getByTestId('dash-pending-reviews')).toHaveTextContent('8'));
    expect(dashboardCalls()).toBe(2);

    failing = true;
    act(() => {
      window.dispatchEvent(new Event('focus'));
    });
    await waitFor(() => expect(dashboardCalls()).toBe(3));
    expect(screen.getByTestId('dash-pending-reviews')).toHaveTextContent('8');
    expect(screen.queryByText('The dashboard could not be loaded')).not.toBeInTheDocument();
  });

  it('keeps the API health indicator below the numbers', async () => {
    open();

    expect(await screen.findByText('API is Healthy')).toBeInTheDocument();
  });
});
