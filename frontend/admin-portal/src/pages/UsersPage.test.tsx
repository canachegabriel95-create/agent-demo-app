import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { UserListItem } from '../api/users';
import {
  ADMIN,
  json,
  problem,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';
import { NO_MATCHES_MESSAGE } from './UsersPage';

function author(overrides: Partial<UserListItem> & Pick<UserListItem, 'id'>): UserListItem {
  return {
    fullName: 'Ann Author',
    email: 'ann@example.com',
    status: 'Active',
    registeredAt: '2026-10-20T09:00:00Z',
    ...overrides,
  };
}

const ANN = author({ id: 'u-ann' });
const BOB = author({
  id: 'u-bob',
  fullName: 'Bob Writer',
  email: 'bob@example.com',
  status: 'Suspended',
  registeredAt: '2026-10-10T09:00:00Z',
});

function pageOf(
  items: UserListItem[],
  extra: Partial<{ page: number; pageSize: number; totalCount: number }> = {},
) {
  return { items, page: 1, pageSize: 20, totalCount: items.length, ...extra };
}

let list: Handler;
let fetchMock: ReturnType<typeof stubApi>;

function listCalls(): URL[] {
  return fetchMock.mock.calls
    .filter(
      ([input, init]) =>
        new URL(String(input)).pathname === '/api/admin/users' && (init?.method ?? 'GET') === 'GET',
    )
    .map(([input]) => new URL(String(input)));
}

beforeEach(() => {
  storeSession();
  list = () => json(pageOf([ANN, BOB]));
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /api/admin/users': (init, url) => list(init, url),
    'GET /api/admin/users/u-ann': () => problem(404, 'Not Found'),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openPage(path = '/users') {
  const router = renderAt(path);
  await screen.findByRole('heading', { level: 1, name: 'Authors' });
  return router;
}

describe('listing', () => {
  it('shows name, email, status and registration date in a table', async () => {
    await openPage();

    const table = await screen.findByRole('table');
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((header) => header.textContent?.replace(/\s+/g, ' ').trim()),
    ).toEqual(expect.arrayContaining(['Name', 'Email', 'Status']));
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]!).getByRole('link', { name: 'Ann Author' })).toHaveAttribute(
      'href',
      '/users/u-ann',
    );
    expect(within(rows[0]!).getByText('ann@example.com')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('Active')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('Oct 20, 2026')).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Suspended')).toBeInTheDocument();
  });

  it('is reachable from the Authors navigation item', async () => {
    const router = renderAt('/');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('link', { name: 'Authors' }));

    expect(router.state.location.pathname).toBe('/users');
    expect(await screen.findByRole('table')).toBeInTheDocument();
  });

  it('asks for the unfiltered newest-first first page with the session token', async () => {
    await openPage();
    await screen.findByRole('table');

    expect(listCalls()[0]!.search).toBe('');
    const headers = new Headers(
      fetchMock.mock.calls.find(([input]) => String(input).includes('/api/admin/users'))![1]!
        .headers,
    );
    expect(headers.get('Authorization')).toBe('Bearer valid-token');
  });

  it('shows a loading state, then the table', async () => {
    let release!: () => void;
    list = () =>
      new Promise((resolve) => {
        release = () => resolve(json(pageOf([ANN])));
      });
    await openPage();

    expect(await screen.findByText('Loading authors…')).toBeInTheDocument();
    release();

    expect(await screen.findByRole('table')).toBeInTheDocument();
    expect(screen.queryByText('Loading authors…')).not.toBeInTheDocument();
  });

  it('shows an empty state when nothing matches', async () => {
    list = () => json(pageOf([]));
    await openPage('/users?search=zzz');

    expect(await screen.findByText(NO_MATCHES_MESSAGE)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('shows an error with a retry that loads again', async () => {
    list = () => problem(500, 'Server error');
    await openPage();
    expect(await screen.findByRole('alert')).toHaveTextContent('The authors could not be loaded');

    list = () => json(pageOf([ANN]));
    await userEvent.setup().click(screen.getByRole('button', { name: /try again/i }));

    expect(await screen.findByRole('table')).toBeInTheDocument();
  });
});

describe('search, status and sort', () => {
  it('keeps the search box and the status select the same height (no hint line under the box)', async () => {
    await openPage();
    await screen.findByRole('table');

    const search = screen.getByRole('searchbox', { name: 'Search' });

    expect(search).toHaveAttribute('placeholder', 'Name or email');
    expect(search).not.toHaveAttribute('aria-describedby');
    expect(search.parentElement!.querySelectorAll('p')).toHaveLength(0);
  });

  it('updates the request and the address once typing pauses', async () => {
    const router = await openPage();
    await screen.findByRole('table');
    const user = userEvent.setup();

    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'ann');

    await waitFor(() => expect(router.state.location.search).toBe('?search=ann'), {
      timeout: 2000,
    });
    await waitFor(() => expect(listCalls().at(-1)!.searchParams.get('search')).toBe('ann'));
    // Typing three letters must not have produced three requests.
    expect(listCalls().filter((url) => url.searchParams.has('search'))).toHaveLength(1);
  });

  it('prefills the search box from the address and requests it', async () => {
    await openPage('/users?search=bob&status=Suspended&page=2');
    await screen.findByRole('table');

    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('bob');
    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveValue('Suspended');
    const query = listCalls().at(-1)!.searchParams;
    expect(query.get('search')).toBe('bob');
    expect(query.get('status')).toBe('Suspended');
    expect(query.get('page')).toBe('2');
  });

  it('filters by status and returns to page 1', async () => {
    const router = await openPage('/users?page=3');
    await screen.findByRole('table');

    await userEvent
      .setup()
      .selectOptions(screen.getByRole('combobox', { name: 'Status' }), 'Active');

    await waitFor(() => expect(router.state.location.search).toBe('?status=Active'));
    await waitFor(() => expect(listCalls().at(-1)!.searchParams.get('status')).toBe('Active'));
    expect(listCalls().at(-1)!.searchParams.has('page')).toBe(false);
  });

  it('ignores an unknown status or direction in the address', async () => {
    await openPage('/users?status=Banned&direction=sideways');
    await screen.findByRole('table');

    expect(listCalls()[0]!.search).toBe('');
    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveValue('');
  });

  it('sorts by registration date: newest first, then oldest first, then back', async () => {
    const router = await openPage();
    await screen.findByRole('table');
    const user = userEvent.setup();
    const header = () => screen.getByRole('columnheader', { name: /Registered/ });
    expect(header()).toHaveAttribute('aria-sort', 'descending');

    await user.click(screen.getByTestId('users-sort-registered'));

    await waitFor(() => expect(router.state.location.search).toBe('?direction=asc'));
    await waitFor(() => expect(header()).toHaveAttribute('aria-sort', 'ascending'));
    const query = listCalls().at(-1)!.searchParams;
    expect(query.get('sort')).toBe('registeredAt');
    expect(query.get('direction')).toBe('asc');

    await user.click(screen.getByTestId('users-sort-registered'));

    await waitFor(() => expect(router.state.location.search).toBe(''));
    await waitFor(() => expect(header()).toHaveAttribute('aria-sort', 'descending'));
  });

  it('keeps the search and status when sorting', async () => {
    const router = await openPage('/users?search=ann&status=Active');
    await screen.findByRole('table');

    await userEvent.setup().click(screen.getByTestId('users-sort-registered'));

    await waitFor(() =>
      expect(router.state.location.search).toBe('?search=ann&status=Active&direction=asc'),
    );
  });
});

describe('pagination', () => {
  it('moves between pages through the address', async () => {
    list = (_init, url) =>
      json(
        pageOf([ANN], {
          page: new URL(url).searchParams.get('page') === '2' ? 2 : 1,
          totalCount: 45,
        }),
      );
    const router = await openPage();
    await screen.findByText('Page 1 of 3');
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: 'Next page' }));

    await waitFor(() => expect(router.state.location.search).toBe('?page=2'));
    expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument();
    expect(listCalls().at(-1)!.searchParams.get('page')).toBe('2');

    await user.click(screen.getByRole('button', { name: 'Previous page' }));

    await waitFor(() => expect(router.state.location.search).toBe(''));
  });
});
