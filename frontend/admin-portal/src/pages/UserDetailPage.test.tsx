import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { UserDetails } from '../api/users';
import {
  ADMIN,
  json,
  problem,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';
import { NOT_FOUND_TITLE } from './UserDetailPage';

const ACTIVE: UserDetails = {
  id: 'u-ann',
  fullName: 'Ann Author',
  email: 'ann@example.com',
  bio: 'Writes about testing.\nSecond line.',
  role: 'Author',
  status: 'Active',
  registeredAt: '2026-10-20T09:00:00Z',
  suspendedAt: null,
  suspensionReason: null,
  articleCounts: { draft: 3, pendingReview: 1, published: 2, rejected: 4 },
  version: 100,
};

const SUSPENDED: UserDetails = {
  ...ACTIVE,
  id: 'u-bob',
  fullName: 'Bob Writer',
  email: 'bob@example.com',
  bio: null,
  status: 'Suspended',
  suspendedAt: '2026-10-22T11:30:00Z',
  suspensionReason: 'Posted spam links repeatedly.',
};

let detail: Handler;
let fetchMock: ReturnType<typeof stubApi>;

beforeEach(() => {
  storeSession();
  detail = () => json(ACTIVE);
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /api/admin/users': () => json({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
    'GET /api/admin/users/u-ann': (init, url) => detail(init, url),
    'GET /api/admin/users/u-bob': (init, url) => detail(init, url),
    'GET /api/admin/users/missing': (init, url) => detail(init, url),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('author detail', () => {
  it('shows the profile, registration date, status and the four article counts', async () => {
    renderAt('/users/u-ann');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Ann Author' }),
    ).toBeInTheDocument();
    expect(screen.getByTestId('user-status')).toHaveTextContent('Active');
    expect(screen.getByTestId('user-email')).toHaveTextContent('ann@example.com');
    expect(screen.getByText('Oct 20, 2026')).toBeInTheDocument();
    for (const [key, label, value] of [
      ['draft', 'Draft', '3'],
      ['pendingReview', 'Pending Review', '1'],
      ['published', 'Published', '2'],
      ['rejected', 'Rejected', '4'],
    ] as const) {
      const tile = screen.getByTestId(`user-count-${key}`);
      expect(within(tile).getByText(label)).toBeInTheDocument();
      expect(within(tile).getByText(value)).toBeInTheDocument();
    }
    expect(screen.queryByTestId('user-suspension')).not.toBeInTheDocument();
  });

  it('sends the session token and the id to the API', async () => {
    renderAt('/users/u-ann');
    await screen.findByRole('heading', { level: 1, name: 'Ann Author' });

    const call = fetchMock.mock.calls.find(([input]) =>
      String(input).endsWith('/api/admin/users/u-ann'),
    )!;
    expect(new Headers(call[1]!.headers).get('Authorization')).toBe('Bearer valid-token');
  });

  it('renders the biography as text that keeps its line breaks', async () => {
    detail = () => json({ ...ACTIVE, bio: '<img src=x onerror=alert(1)> <b>bold</b>\nnext line' });
    renderAt('/users/u-ann');

    const bio = await screen.findByTestId('user-bio');

    expect(bio.textContent).toBe('<img src=x onerror=alert(1)> <b>bold</b>\nnext line');
    expect(bio.querySelector('img, b')).toBeNull();
    expect(getComputedStyle(bio).whiteSpace).toBe('pre-wrap');
  });

  it('shows a dash when there is no biography', async () => {
    detail = () => json({ ...ACTIVE, bio: null });
    renderAt('/users/u-ann');

    expect(await screen.findByTestId('user-bio')).toHaveTextContent('—');
  });

  it('shows the suspension reason and date of a suspended author', async () => {
    detail = () => json(SUSPENDED);
    renderAt('/users/u-bob');

    const box = await screen.findByTestId('user-suspension');

    expect(screen.getByTestId('user-status')).toHaveTextContent('Suspended');
    expect(within(box).getByTestId('user-suspension-reason')).toHaveTextContent(
      'Posted spam links repeatedly.',
    );
    expect(within(box).getByText(/Oct 22, 2026/)).toBeInTheDocument();
  });

  it('shows a loading state first', async () => {
    let release!: () => void;
    detail = () =>
      new Promise((resolve) => {
        release = () => resolve(json(ACTIVE));
      });
    renderAt('/users/u-ann');

    expect(await screen.findByText('Loading author…')).toBeInTheDocument();
    release();

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Ann Author' }),
    ).toBeInTheDocument();
  });

  it('shows "Author not found" with a link back for an unknown id', async () => {
    detail = () => problem(404, 'Not Found');
    const router = renderAt('/users/missing');

    expect(
      await screen.findByRole('heading', { level: 1, name: NOT_FOUND_TITLE }),
    ).toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole('link', { name: /back to authors/i }));

    expect(router.state.location.pathname).toBe('/users');
  });

  it('shows an error with a retry for other failures', async () => {
    detail = () => problem(500, 'Server error');
    renderAt('/users/u-ann');
    expect(await screen.findByRole('alert')).toHaveTextContent('The author could not be loaded');
    expect(screen.queryByText(NOT_FOUND_TITLE)).not.toBeInTheDocument();

    detail = () => json(ACTIVE);
    await userEvent.setup().click(screen.getByRole('button', { name: /try again/i }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Ann Author' }),
    ).toBeInTheDocument();
  });

  it('is reached by clicking a name in the list', async () => {
    fetchMock = stubApi({
      'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
      'GET /api/admin/users': () =>
        json({
          items: [
            {
              id: 'u-ann',
              fullName: 'Ann Author',
              email: 'ann@example.com',
              status: 'Active',
              registeredAt: ACTIVE.registeredAt,
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        }),
      'GET /api/admin/users/u-ann': () => json(ACTIVE),
    });
    const router = renderAt('/users');

    await userEvent.setup().click(await screen.findByRole('link', { name: 'Ann Author' }));

    expect(await screen.findByTestId('user-count-draft')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/users/u-ann');
  });
});
