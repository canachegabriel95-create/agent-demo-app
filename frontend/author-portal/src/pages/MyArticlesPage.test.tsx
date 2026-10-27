import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MyArticleItem } from '../api/myArticles';
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
import { CHANGED_MESSAGE, NO_ARTICLES_TITLE, NO_MATCHES_TITLE } from './MyArticlesPage';

const SCIENCE = { id: 'c1', name: 'Science' };

function item(overrides: Partial<MyArticleItem> = {}): MyArticleItem {
  return {
    id: 'a1',
    title: 'A draft article',
    category: SCIENCE,
    status: 'Draft',
    updatedAt: '2026-10-20T09:00:00Z',
    submittedAt: null,
    publishedAt: null,
    latestRejectionReason: null,
    allowedActions: ['edit', 'delete', 'submit'],
    version: 10,
    openRevision: null,
    ...overrides,
  };
}

function page(items: MyArticleItem[], totalCount = items.length, pageNumber = 1) {
  return json({ items, page: pageNumber, pageSize: 20, totalCount });
}

let list: Handler;
let fetchMock: ReturnType<typeof stubApi>;
let handlers: Record<string, Handler>;

function listCalls() {
  return fetchMock.mock.calls
    .filter(([input, init]) => {
      const url = new URL(String(input));
      return url.pathname === '/api/me/articles' && (init?.method ?? 'GET') === 'GET';
    })
    .map(([input]) => new URL(String(input)).searchParams);
}

function lastQuery() {
  return listCalls().at(-1)!;
}

beforeEach(() => {
  storeSession();
  list = () => page([item()]);
  handlers = {
    'GET /api/auth/me': () => json({ ...AUTHOR, status: 'Active' }),
    'GET /health': () => healthy(),
    'GET /api/categories': () =>
      json([
        { ...SCIENCE, slug: 'science' },
        { id: 'c2', name: 'Arts', slug: 'arts' },
      ]),
    'GET /api/me/articles': (init, url) => list(init, url),
  };
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function open(path = '/articles') {
  fetchMock = stubApi(handlers);
  return renderAt(path);
}

describe('rows', () => {
  it('shows title, category, status and the date that fits the status', async () => {
    list = () =>
      page([
        item({ id: 'd', title: 'Draft one', status: 'Draft' }),
        item({
          id: 'p',
          title: 'Pending one',
          status: 'PendingReview',
          submittedAt: '2026-10-21T10:00:00Z',
          allowedActions: ['withdraw'],
        }),
        item({
          id: 'u',
          title: 'Published one',
          status: 'Published',
          publishedAt: '2026-10-22T11:00:00Z',
          category: null,
          allowedActions: [],
        }),
        item({
          id: 'r',
          title: 'Rejected one',
          status: 'Rejected',
          allowedActions: ['edit', 'submit'],
          latestRejectionReason: 'Too short.',
        }),
      ]);
    open();

    const rows = await screen.findAllByTestId('article-row');
    expect(rows).toHaveLength(4);
    expect(within(rows[0]!).getByRole('link', { name: 'Draft one' })).toHaveAttribute(
      'href',
      '/articles/d',
    );
    expect(within(rows[0]!).getByTestId('article-row-status')).toHaveTextContent('Draft');
    expect(within(rows[0]!).getByTestId('article-row-category')).toHaveTextContent('Science');
    expect(within(rows[0]!).getByTestId('article-row-date')).toHaveTextContent(
      /^Updated Oct 20, 2026/,
    );
    expect(within(rows[1]!).getByTestId('article-row-status')).toHaveTextContent('Pending review');
    expect(within(rows[1]!).getByTestId('article-row-date')).toHaveTextContent(
      /^Submitted Oct 21, 2026/,
    );
    expect(within(rows[2]!).getByTestId('article-row-date')).toHaveTextContent(
      /^Published Oct 22, 2026/,
    );
    expect(within(rows[2]!).getByTestId('article-row-category')).toHaveTextContent('No category');
    expect(within(rows[3]!).getByTestId('article-row-date')).toHaveTextContent(/^Updated /);
    expect(listCalls()[0]!.toString()).toBe('');
  });

  it('shows only the actions the lifecycle allows', async () => {
    list = () =>
      page([
        item({ id: 'd', title: 'Draft one' }),
        item({
          id: 'p',
          title: 'Pending one',
          status: 'PendingReview',
          allowedActions: ['withdraw'],
        }),
        item({ id: 'u', title: 'Published one', status: 'Published', allowedActions: [] }),
        item({
          id: 'r',
          title: 'Rejected one',
          status: 'Rejected',
          allowedActions: ['edit', 'submit'],
        }),
      ]);
    open();

    const rows = await screen.findAllByTestId('article-row');
    // Each row's actions, in order: buttons by their visible start, plus whether it has an Edit link.
    const actions = (row: HTMLElement | undefined) => {
      const scope = within(row!);
      return [
        ...scope.queryAllByRole('link', { name: /^Edit/ }).map(() => 'Edit'),
        ...scope.queryAllByRole('button').map((button) => button.textContent),
      ];
    };
    expect(actions(rows[0])).toEqual(['Edit', 'Submit', 'Delete']);
    expect(actions(rows[1])).toEqual(['Withdraw']);
    expect(actions(rows[2])).toEqual([]);
    expect(actions(rows[3])).toEqual(['Edit', 'Resubmit', 'See feedback']);
    expect(within(rows[0]!).getByRole('link', { name: 'Edit Draft one' })).toHaveAttribute(
      'href',
      '/articles/d/edit',
    );
  });

  it('shows the latest rejection reason as text when See feedback is opened', async () => {
    list = () =>
      page([
        item({
          status: 'Rejected',
          allowedActions: ['edit', 'submit'],
          latestRejectionReason: 'Fix <b>this</b>\nand that.',
        }),
      ]);
    open();

    const toggle = await screen.findByRole('button', { name: 'See feedback for A draft article' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByTestId('article-row-feedback')).not.toBeInTheDocument();

    await userEvent.click(toggle);

    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const feedback = screen.getByTestId('article-row-feedback');
    expect(feedback.textContent).toBe('Fix <b>this</b>\nand that.');
    expect(feedback.querySelector('b')).toBeNull();

    await userEvent.click(toggle);
    expect(screen.queryByTestId('article-row-feedback')).not.toBeInTheDocument();
  });
});

describe('filters', () => {
  it('maps the search box (after a pause), selects and paging to the request and the address', async () => {
    list = () => page([item()], 45);
    const router = open();
    await screen.findByTestId('article-row');

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), '  cats ');
    await waitFor(() => expect(lastQuery().get('search')).toBe('cats'));
    expect(router.state.location.search).toBe('?search=cats');
    // Typing did not send a request per keystroke.
    expect(listCalls().filter((query) => query.has('search'))).toHaveLength(1);

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'Rejected');
    await waitFor(() => expect(lastQuery().get('status')).toBe('Rejected'));

    await userEvent.selectOptions(await screen.findByLabelText('Category'), 'Arts');
    await waitFor(() => expect(lastQuery().get('categoryId')).toBe('c2'));

    await userEvent.selectOptions(screen.getByLabelText('Sort by'), 'Submitted date');
    await waitFor(() => expect(lastQuery().get('sort')).toBe('submittedAt'));

    await userEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await waitFor(() => expect(lastQuery().get('page')).toBe('2'));
    expect(router.state.location.search).toBe(
      '?search=cats&status=Rejected&categoryId=c2&sort=submittedAt&page=2',
    );

    // A changed filter starts again at the first page.
    await userEvent.selectOptions(screen.getByLabelText('Sort by'), 'Last updated');
    await waitFor(() => expect(lastQuery().has('page')).toBe(false));
    expect(lastQuery().has('sort')).toBe(false);
  });

  it('applies the address on arrival, as from a dashboard card', async () => {
    open('/articles?status=Rejected&search=cats&sort=title&page=2');

    await screen.findByTestId('article-row');

    expect(Object.fromEntries(lastQuery())).toEqual({
      status: 'Rejected',
      search: 'cats',
      sort: 'title',
      page: '2',
    });
    expect(screen.getByLabelText('Status')).toHaveValue('Rejected');
    expect(screen.getByLabelText('Sort by')).toHaveValue('title');
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('cats');
  });

  it('ignores an unknown status or sort in the address', async () => {
    open('/articles?status=Bogus&sort=nope');

    await screen.findByTestId('article-row');

    expect(listCalls()[0]!.toString()).toBe('');
  });
});

describe('empty, loading and error states', () => {
  it('invites an author without articles to write one', async () => {
    list = () => page([]);
    open();

    expect(await screen.findByText(NO_ARTICLES_TITLE)).toBeInTheDocument();
    expect(screen.getByTestId('articles-empty-new')).toHaveAttribute('href', '/articles/new');
    expect(screen.queryByText(NO_MATCHES_TITLE)).not.toBeInTheDocument();
  });

  it('tells apart "no matches" and clears the filters', async () => {
    list = (_init, url) => (new URL(url).search ? page([]) : page([item()]));
    const router = open('/articles?status=Published&search=zzz');

    expect(await screen.findByText(NO_MATCHES_TITLE)).toBeInTheDocument();
    expect(screen.queryByText(NO_ARTICLES_TITLE)).not.toBeInTheDocument();

    await userEvent.click(screen.getByTestId('articles-clear-filters'));

    expect(await screen.findByTestId('article-row')).toBeInTheDocument();
    expect(router.state.location.search).toBe('');
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('');
    expect(screen.getByLabelText('Status')).toHaveValue('');
  });

  it('shows a loading state until the list arrives', async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    list = async () => {
      await gate;
      return page([item()]);
    };
    open();

    expect(await screen.findByText('Loading your articles…')).toBeInTheDocument();
    release();
    expect(await screen.findByTestId('article-row')).toBeInTheDocument();
  });

  it('shows an error and loads the list again on retry', async () => {
    let calls = 0;
    list = () => {
      calls += 1;
      return calls === 1 ? problem(500, 'Boom') : page([item()]);
    };
    open();

    expect(await screen.findByRole('alert')).toHaveTextContent('Your articles could not be loaded');
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('article-row')).toBeInTheDocument();
    expect(calls).toBe(2);
  });
});

describe('row actions', () => {
  it('deletes a draft after confirmation, with its version, then reloads the list', async () => {
    let items = [item({ title: 'Doomed' })];
    list = () => page(items);
    handlers['DELETE /api/articles/a1'] = (_init, url) => {
      expect(new URL(url).searchParams.get('version')).toBe('10');
      items = [];
      return new Response(null, { status: 204 });
    };
    open();

    await userEvent.click(await screen.findByTestId('article-row-delete'));
    expect(screen.getByRole('dialog')).toHaveTextContent('Delete draft “Doomed”?');
    await userEvent.click(screen.getByTestId('article-row-delete-confirm'));

    expect(await screen.findByText(NO_ARTICLES_TITLE)).toBeInTheDocument();
    expect(screen.getByTestId('flash-message')).toHaveTextContent('Draft “Doomed” deleted.');
  });

  it('does not delete when the confirmation is cancelled', async () => {
    open();

    await userEvent.click(await screen.findByTestId('article-row-delete'));
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'DELETE')).toBe(false);
  });

  it('submits a draft and shows the new status from the reloaded list', async () => {
    let items = [item({ title: 'Ready' })];
    list = () => page(items);
    handlers['POST /api/articles/a1/submit'] = (init) => {
      expect(JSON.parse(String(init?.body))).toEqual({ version: 10 });
      items = [item({ title: 'Ready', status: 'PendingReview', allowedActions: ['withdraw'] })];
      return json({});
    };
    open();

    await userEvent.click(await screen.findByTestId('article-row-submit'));
    await userEvent.click(screen.getByTestId('article-row-submit-confirm'));

    await waitFor(() =>
      expect(screen.getByTestId('article-row-status')).toHaveTextContent('Pending review'),
    );
    expect(screen.getByTestId('flash-message')).toHaveTextContent('“Ready” submitted for review.');
    expect(screen.getByTestId('article-row-withdraw')).toBeInTheDocument();
    expect(screen.queryByTestId('article-row-submit')).not.toBeInTheDocument();
  });

  it('keeps the dialog open and names what is missing when the submission is invalid', async () => {
    handlers['POST /api/articles/a1/submit'] = () =>
      json(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { summary: ['Summary is required.'], body: ['Body is too short.'] },
        },
        400,
        'application/problem+json',
      );
    open();

    await userEvent.click(await screen.findByTestId('article-row-submit'));
    await userEvent.click(screen.getByTestId('article-row-submit-confirm'));

    expect(await screen.findByRole('dialog')).toHaveTextContent(
      'This article cannot be submitted yet: Summary is required. Body is too short. Open the article to fix it.',
    );
  });

  it('withdraws a pending article', async () => {
    let items = [item({ status: 'PendingReview', allowedActions: ['withdraw'] })];
    list = () => page(items);
    handlers['POST /api/articles/a1/withdraw'] = (init) => {
      expect(JSON.parse(String(init?.body))).toEqual({ version: 10 });
      items = [item()];
      return json({});
    };
    open();

    await userEvent.click(await screen.findByTestId('article-row-withdraw'));
    await userEvent.click(screen.getByTestId('article-row-withdraw-confirm'));

    await waitFor(() =>
      expect(screen.getByTestId('article-row-status')).toHaveTextContent('Draft'),
    );
    expect(screen.getByTestId('flash-message')).toHaveTextContent('withdrawn');
  });

  it('explains a conflict (an administrator decided first) and shows the current state', async () => {
    let items = [item({ status: 'PendingReview', allowedActions: ['withdraw'] })];
    list = () => page(items);
    handlers['POST /api/articles/a1/withdraw'] = () => {
      items = [item({ status: 'Published', allowedActions: [] })];
      return problem(409, 'Conflict');
    };
    open();

    await userEvent.click(await screen.findByTestId('article-row-withdraw'));
    await userEvent.click(screen.getByTestId('article-row-withdraw-confirm'));

    expect(await screen.findByTestId('articles-notice')).toHaveTextContent(CHANGED_MESSAGE);
    await waitFor(() =>
      expect(screen.getByTestId('article-row-status')).toHaveTextContent('Published'),
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows the message sent by the article page once', async () => {
    const router = open('/');
    await screen.findByText('Author Portal');

    await router.navigate('/articles', { state: { message: 'Draft “Old” deleted.' } });

    expect(await screen.findByTestId('flash-message')).toHaveTextContent('Draft “Old” deleted.');
    // The entry was replaced, so the message is not repeated after a reload.
    await waitFor(() => expect(router.state.location.state).toBeNull());
  });
});
