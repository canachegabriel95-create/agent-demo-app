import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReviewQueueItem } from '../api/reviews';
import {
  ADMIN,
  json,
  problem,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';
import { EMPTY_QUEUE_MESSAGE } from './ReviewQueuePage';

const CATEGORIES = [
  { id: 'c-tech', name: 'Technology', slug: 'technology' },
  { id: 'c-sci', name: 'Science', slug: 'science' },
];

function item(
  overrides: Partial<ReviewQueueItem> & Pick<ReviewQueueItem, 'articleId' | 'title'>,
): ReviewQueueItem {
  return {
    category: { id: 'c-sci', name: 'Science' },
    author: { id: 'a-ann', fullName: 'Ann Author', status: 'Active' },
    submittedAt: '2026-10-20T09:30:00Z',
    submissionVersion: 1,
    kind: 'Article',
    revisionId: null,
    ...overrides,
  };
}

const FIRST = item({ articleId: 'r1', title: 'Oldest article' });
const SECOND = item({
  articleId: 'r2',
  title: 'Bob waits',
  author: { id: 'a-bob', fullName: 'Bob Writer', status: 'Suspended' },
  category: { id: 'c-tech', name: 'Technology' },
  submittedAt: '2026-10-22T15:00:00Z',
  submissionVersion: 3,
});

function pageOf(
  items: ReviewQueueItem[],
  extra: Partial<{ page: number; pageSize: number; totalCount: number }> = {},
) {
  return { items, page: 1, pageSize: 20, totalCount: items.length, ...extra };
}

let queue: Handler;
let lookup: Handler;
let fetchMock: ReturnType<typeof stubApi>;

function queueCalls(): URL[] {
  return fetchMock.mock.calls
    .filter(([input]) => new URL(String(input)).pathname === '/api/admin/reviews/queue')
    .map(([input]) => new URL(String(input)));
}

beforeEach(() => {
  storeSession();
  queue = () => json(pageOf([FIRST, SECOND]));
  lookup = () => json([{ id: 'a-ann', fullName: 'Ann Author' }]);
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /api/categories': () => json(CATEGORIES),
    'GET /api/admin/reviews/queue': (init, url) => queue(init, url),
    'GET /api/admin/authors/lookup': (init, url) => lookup(init, url),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openQueue(path = '/reviews') {
  const router = renderAt(path);
  await screen.findByRole('heading', { level: 1, name: 'Review queue' });
  return router;
}

describe('the queue table', () => {
  it('is reachable from the "Review queue" navigation item', async () => {
    const router = renderAt('/');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('link', { name: 'Review queue' }));

    expect(router.state.location.pathname).toBe('/reviews');
    expect(await screen.findByRole('table')).toBeInTheDocument();
  });

  it('shows title, author, category, submitted date and version, oldest first', async () => {
    await openQueue();

    const table = await screen.findByRole('table');
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((header) => header.textContent?.replace(/\s+/g, ' ').trim()),
    ).toEqual(expect.arrayContaining(['Title', 'Author', 'Category', 'Version']));
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(within(rows[0]!).getByRole('link', { name: 'Oldest article' })).toHaveAttribute(
      'href',
      '/reviews/r1',
    );
    expect(within(rows[0]!).getByText('Ann Author')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('Science')).toBeInTheDocument();
    expect(within(rows[0]!).getByText(/Oct 20, 2026/)).toBeInTheDocument();
    expect(within(rows[1]!).getByText('3')).toBeInTheDocument();
    expect(within(table).getByText(/oldest submission first/)).toBeInTheDocument();
  });

  it('asks for the unfiltered first page with the session token', async () => {
    await openQueue();
    await screen.findByRole('table');

    expect(queueCalls()[0]!.search).toBe('');
    const headers = new Headers(
      fetchMock.mock.calls.find(([input]) => String(input).includes('/reviews/queue'))![1]!.headers,
    );
    expect(headers.get('Authorization')).toBe('Bearer valid-token');
  });

  it('marks the articles of suspended authors with a text badge', async () => {
    await openQueue();

    const rows = within(await screen.findByRole('table'))
      .getAllByRole('row')
      .slice(1);
    expect(within(rows[1]!).getByText('Suspended')).toBeInTheDocument();
    expect(within(rows[0]!).queryByText('Suspended')).not.toBeInTheDocument();
  });
});

describe('states', () => {
  it('shows a loading state, then the table', async () => {
    let release!: () => void;
    queue = () =>
      new Promise((resolve) => {
        release = () => resolve(json(pageOf([FIRST])));
      });
    await openQueue();

    expect(await screen.findByText('Loading the review queue…')).toBeInTheDocument();
    release();

    expect(await screen.findByRole('table')).toBeInTheDocument();
  });

  it('says so when nothing is waiting for review', async () => {
    queue = () => json(pageOf([]));
    await openQueue();

    expect(await screen.findByText(EMPTY_QUEUE_MESSAGE)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('explains an empty result of filters differently', async () => {
    queue = () => json(pageOf([]));
    await openQueue('/reviews?categoryId=c-tech');

    expect(await screen.findByText(EMPTY_QUEUE_MESSAGE)).toBeInTheDocument();
    expect(screen.getByText(/matches the filters/)).toBeInTheDocument();
  });

  it('shows an error with a retry', async () => {
    let calls = 0;
    queue = () => {
      calls += 1;
      return calls === 1 ? problem(500, 'An unexpected error occurred.') : json(pageOf([FIRST]));
    };
    await openQueue();

    expect(await screen.findByText('The review queue could not be loaded')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('table')).toBeInTheDocument();
    expect(calls).toBe(2);
  });

  it('refreshes the queue on request', async () => {
    await openQueue();
    await screen.findByRole('table');

    await userEvent.click(screen.getByTestId('review-refresh'));

    await waitFor(() => expect(queueCalls()).toHaveLength(2));
  });
});

describe('sorting', () => {
  it('toggles the Submitted column between oldest and newest first, in the request and the URL', async () => {
    const router = await openQueue();
    await screen.findByRole('table');
    const user = userEvent.setup();

    await user.click(screen.getByTestId('review-sort-submitted'));

    await waitFor(() => expect(router.state.location.search).toBe('?direction=desc'));
    await waitFor(() =>
      expect(queueCalls().at(-1)!.search).toBe('?sort=submittedAt&direction=desc'),
    );
    expect(screen.getByRole('columnheader', { name: /Submitted/ })).toHaveAttribute(
      'aria-sort',
      'descending',
    );

    await user.click(screen.getByTestId('review-sort-submitted'));

    await waitFor(() => expect(router.state.location.search).toBe(''));
    await waitFor(() => expect(queueCalls().at(-1)!.search).toBe(''));
  });

  it('reads the direction from the address', async () => {
    await openQueue('/reviews?direction=desc');
    await screen.findByRole('table');

    expect(queueCalls()[0]!.search).toBe('?sort=submittedAt&direction=desc');
    expect(screen.getByRole('columnheader', { name: /Submitted/ })).toHaveAttribute(
      'aria-sort',
      'descending',
    );
  });
});

describe('filters', () => {
  it('filters by category and puts it in the URL', async () => {
    const router = await openQueue();
    await screen.findByRole('table');
    const select = screen.getByLabelText('Category');
    await waitFor(() => expect(select).toBeEnabled());

    await userEvent.selectOptions(select, 'Technology');

    await waitFor(() => expect(router.state.location.search).toBe('?categoryId=c-tech'));
    await waitFor(() => expect(queueCalls().at(-1)!.search).toBe('?categoryId=c-tech'));
  });

  it('finds an author by typing, filters by the one chosen and can clear it', async () => {
    const router = await openQueue();
    await screen.findByRole('table');
    const user = userEvent.setup();

    await user.type(screen.getByTestId('review-author-search'), 'ann');
    const option = await screen.findByRole('button', { name: 'Ann Author' });
    expect(new URL(String(fetchMock.mock.calls.at(-1)![0])).search).toBe('?search=ann');
    await user.click(option);

    await waitFor(() => expect(router.state.location.search).toBe('?authorId=a-ann'));
    await waitFor(() => expect(queueCalls().at(-1)!.search).toBe('?authorId=a-ann'));
    expect(screen.getByTestId('review-author-chip')).toHaveTextContent('Ann Author');

    await user.click(screen.getByTestId('review-author-clear'));

    await waitFor(() => expect(router.state.location.search).toBe(''));
    expect(screen.getByTestId('review-author-search')).toBeInTheDocument();
  });

  it('says when no author matches the typed text', async () => {
    lookup = () => json([]);
    await openQueue();
    await screen.findByRole('table');

    await userEvent.type(screen.getByTestId('review-author-search'), 'zzz');

    expect(await screen.findByText('No author matches.')).toBeInTheDocument();
  });

  it('applies every filter from the address and names the author from the list', async () => {
    await openQueue('/reviews?authorId=a-ann&categoryId=c-sci&direction=desc&page=2');
    await screen.findByRole('table');

    expect(queueCalls()[0]!.search).toBe(
      '?authorId=a-ann&categoryId=c-sci&sort=submittedAt&direction=desc&page=2',
    );
    expect(screen.getByTestId('review-author-chip')).toHaveTextContent('Ann Author');
    await waitFor(() => expect(screen.getByLabelText('Category')).toHaveValue('c-sci'));
  });

  it('goes back to the first page when a filter changes', async () => {
    queue = () => json(pageOf([FIRST], { page: 2, totalCount: 45 }));
    const router = await openQueue('/reviews?page=2');
    await screen.findByRole('table');
    const select = screen.getByLabelText('Category');
    await waitFor(() => expect(select).toBeEnabled());

    await userEvent.selectOptions(select, 'Science');

    await waitFor(() => expect(router.state.location.search).toBe('?categoryId=c-sci'));
  });
});

describe('paging', () => {
  it('moves between pages through the URL', async () => {
    queue = () => json(pageOf([FIRST, SECOND], { totalCount: 45 }));
    const router = await openQueue();
    await screen.findByRole('table');
    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Next page' }));

    await waitFor(() => expect(router.state.location.search).toBe('?page=2'));
    await waitFor(() => expect(queueCalls().at(-1)!.search).toBe('?page=2'));
  });
});
