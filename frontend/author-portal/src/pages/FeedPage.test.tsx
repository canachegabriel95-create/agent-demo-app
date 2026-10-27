import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { FeedArticle, FeedItem } from '../api/feed';
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
import { NO_ARTICLES_TITLE, NO_MATCHES_TITLE } from './FeedPage';

function item(overrides: Partial<FeedItem> = {}): FeedItem {
  return {
    id: 'f1',
    title: 'A published article',
    summary: 'What the article is about.',
    category: { id: 'c1', name: 'Science' },
    authorDisplayName: 'Bob Writer',
    publishedAt: '2026-10-22T11:00:00Z',
    ...overrides,
  };
}

function page(items: FeedItem[], totalCount = items.length) {
  return json({ items, page: 1, pageSize: 20, totalCount });
}

let feed: Handler;
let detail: Handler;
let fetchMock: ReturnType<typeof stubApi>;

function feedCalls() {
  return fetchMock.mock.calls
    .filter(([input]) => new URL(String(input)).pathname === '/api/feed')
    .map(([input]) => new URL(String(input)).searchParams);
}

beforeEach(() => {
  storeSession();
  feed = () => page([item()]);
  detail = () => json({ ...item(), body: 'The full text.' } satisfies FeedArticle);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function open(path = '/feed') {
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...AUTHOR, status: 'Active' }),
    'GET /health': () => healthy(),
    'GET /api/categories': () =>
      json([
        { id: 'c1', name: 'Science', slug: 'science' },
        { id: 'c2', name: 'Arts', slug: 'arts' },
      ]),
    'GET /api/feed': (init, url) => feed(init, url),
    'GET /api/feed/f1': (init, url) => detail(init, url),
    'GET /api/feed/missing': () => problem(404, 'Not Found'),
  });
  return renderAt(path);
}

describe('feed list', () => {
  it('shows title link, summary, category, author display name and date on each card', async () => {
    open();

    const card = await screen.findByTestId('feed-card-f1');
    expect(within(card).getByRole('link', { name: 'A published article' })).toHaveAttribute(
      'href',
      '/feed/f1',
    );
    expect(card).toHaveTextContent('What the article is about.');
    expect(card).toHaveTextContent('Science');
    expect(card).toHaveTextContent('Bob Writer');
    expect(card).toHaveTextContent('Oct 22, 2026');
    expect(feedCalls()[0]!.toString()).toBe('');
  });

  it('shows article text as text, never as HTML', async () => {
    feed = () =>
      page([
        item({ title: 'Hello <b>bold</b>', summary: '<img src=x onerror=alert(1)>\nsecond line' }),
      ]);
    open();

    const card = await screen.findByTestId('feed-card-f1');

    expect(card).toHaveTextContent('Hello <b>bold</b>');
    expect(card).toHaveTextContent('<img src=x onerror=alert(1)>');
    expect(card.querySelector('b, img')).toBeNull();
  });

  it('has no edit or delete controls', async () => {
    open();

    await screen.findByTestId('feed-card-f1');

    expect(screen.queryByRole('button', { name: /edit|delete|submit|withdraw/i })).toBeNull();
    expect(screen.queryByRole('link', { name: /edit/i })).toBeNull();
  });
});

describe('filters', () => {
  it('maps search (after a pause), category and paging to the request and the address', async () => {
    feed = () => page([item()], 45);
    const router = open();
    await screen.findByTestId('feed-card-f1');

    await userEvent.type(screen.getByTestId('feed-search'), '  cats ');
    await waitFor(() => expect(feedCalls().at(-1)!.get('search')).toBe('cats'));
    expect(router.state.location.search).toBe('?search=cats');
    expect(feedCalls().filter((query) => query.has('search'))).toHaveLength(1);

    await userEvent.selectOptions(await screen.findByTestId('feed-category'), 'Arts');
    await waitFor(() => expect(feedCalls().at(-1)!.get('categoryId')).toBe('c2'));

    await userEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await waitFor(() => expect(feedCalls().at(-1)!.get('page')).toBe('2'));
    expect(router.state.location.search).toBe('?search=cats&categoryId=c2&page=2');

    await userEvent.selectOptions(screen.getByTestId('feed-category'), 'All');
    await waitFor(() => expect(feedCalls().at(-1)!.has('categoryId')).toBe(false));
    expect(feedCalls().at(-1)!.has('page')).toBe(false);
  });

  it('applies the address on arrival', async () => {
    open('/feed?search=cats&categoryId=c1&page=2');

    await screen.findByTestId('feed-card-f1');

    expect(Object.fromEntries(feedCalls().at(-1)!)).toEqual({
      search: 'cats',
      categoryId: 'c1',
      page: '2',
    });
    expect(screen.getByTestId('feed-search')).toHaveValue('cats');
    expect(screen.getByTestId('feed-category')).toHaveValue('c1');
  });
});

describe('states', () => {
  it('says there is nothing published yet', async () => {
    feed = () => page([]);
    open();

    expect(await screen.findByText(NO_ARTICLES_TITLE)).toBeInTheDocument();
    expect(screen.queryByText(NO_MATCHES_TITLE)).not.toBeInTheDocument();
  });

  it('tells apart "no matches" and clears the filters', async () => {
    feed = (_init, url) => (new URL(url).search ? page([]) : page([item()]));
    const router = open('/feed?search=zzz');

    expect(await screen.findByText(NO_MATCHES_TITLE)).toBeInTheDocument();
    await userEvent.click(screen.getByTestId('feed-clear-filters'));

    expect(await screen.findByTestId('feed-card-f1')).toBeInTheDocument();
    expect(router.state.location.search).toBe('');
    expect(screen.getByTestId('feed-search')).toHaveValue('');
  });

  it('shows a loading state, then an error with retry', async () => {
    let calls = 0;
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    feed = async () => {
      calls += 1;
      if (calls === 1) {
        await gate;
        return problem(500, 'Boom');
      }
      return page([item()]);
    };
    open();

    expect(await screen.findByText('Loading the feed…')).toBeInTheDocument();
    release();
    expect(await screen.findByRole('alert')).toHaveTextContent('The feed could not be loaded');
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('feed-card-f1')).toBeInTheDocument();
  });
});

describe('reader', () => {
  it('opens a card and shows the full article read-only', async () => {
    const router = open();

    await userEvent.click(await screen.findByRole('link', { name: 'A published article' }));

    expect(router.state.location.pathname).toBe('/feed/f1');
    expect(await screen.findByTestId('feed-article-title')).toHaveTextContent(
      'A published article',
    );
    expect(screen.getByTestId('feed-article-meta')).toHaveTextContent(
      'Science · Bob Writer · Oct 22, 2026',
    );
    expect(screen.getByTestId('feed-article-summary')).toHaveTextContent(
      'What the article is about.',
    );
    expect(screen.getByTestId('feed-article-body')).toHaveTextContent('The full text.');
    expect(screen.queryByRole('button', { name: /edit|delete|submit|withdraw/i })).toBeNull();
    expect(screen.queryByRole('link', { name: /edit/i })).toBeNull();
  });

  it('shows the body as text with its line breaks, never as HTML', async () => {
    const body = '<script>window.__pwned = 1</script>\n<b>not bold</b>\nlast line';
    detail = () => json({ ...item(), body });
    open('/feed/f1');

    const element = await screen.findByTestId('feed-article-body');

    expect(element.textContent).toBe(body);
    expect(element.querySelector('script, b')).toBeNull();
    expect(getComputedStyle(element).whiteSpace).toBe('pre-wrap');
    expect((window as unknown as { __pwned?: number }).__pwned).toBeUndefined();
  });

  it('shows a not-found state for an article that is not published or does not exist', async () => {
    open('/feed/missing');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Article not found' }),
    ).toBeVisible();
    expect(screen.getByRole('link', { name: 'Back to the feed' })).toHaveAttribute('href', '/feed');
  });

  it('shows an error with retry when the article cannot be loaded', async () => {
    let calls = 0;
    detail = () => {
      calls += 1;
      return calls === 1 ? problem(500, 'Boom') : json({ ...item(), body: 'Second try.' });
    };
    open('/feed/f1');

    expect(await screen.findByRole('alert')).toHaveTextContent('The article could not be loaded');
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('feed-article-body')).toHaveTextContent('Second try.');
  });
});
