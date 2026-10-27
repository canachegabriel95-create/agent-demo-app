import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Article } from '../api/articles';
import { AUTHOR, json, problem, renderAt, storeSession, stubApi } from '../test/authHelpers';
import { DELETE_CONFLICT_MESSAGE } from './ArticlePage';

const SCRIPT = '<script>alert(1)</script>';
const IMG = '<img src=x onerror=alert(1)>';

const DRAFT: Article = {
  id: 'a1',
  title: 'My draft',
  summary: 'A summary',
  body: 'Line one\nLine two',
  category: { id: 'c2', name: 'Science' },
  status: 'Draft',
  submissionVersion: 0,
  createdAt: '2026-10-27T10:00:00Z',
  updatedAt: '2026-10-27T11:00:00Z',
  submittedAt: null,
  publishedAt: null,
  latestReview: null,
  allowedActions: ['edit', 'delete', 'submit'],
  version: 100,
};

let current: Article;
let getResponse: () => Response;
let deleteResponse: () => Response;
let fetchMock: ReturnType<typeof stubApi>;

function deletes() {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE');
}

beforeEach(() => {
  storeSession();
  current = DRAFT;
  getResponse = () => json(current);
  deleteResponse = () => new Response(null, { status: 204 });
  fetchMock = stubApi({
    'GET /api/auth/me': () => json(AUTHOR),
    'GET /api/articles/a1': () => getResponse(),
    'DELETE /api/articles/a1': () => deleteResponse(),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('showing an article', () => {
  it('shows the title, status, category, dates, summary and body', async () => {
    renderAt('/articles/a1');

    expect(await screen.findByRole('heading', { level: 1, name: 'My draft' })).toBeVisible();
    expect(screen.getByTestId('article-status-badge')).toHaveTextContent('Draft');
    expect(screen.getByTestId('article-view-category')).toHaveTextContent('Science');
    expect(screen.getByTestId('article-view-summary')).toHaveTextContent('A summary');
    expect(screen.getByTestId('article-view-body').textContent).toBe('Line one\nLine two');
    expect(screen.getByText('Last updated')).toBeVisible();
  });

  it('shows "No category" and empty-section hints for a bare draft', async () => {
    current = { ...DRAFT, summary: '', body: '', category: null };
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-view-category')).toHaveTextContent('No category');
    expect(screen.getByText('No summary yet.')).toBeVisible();
    expect(screen.getByText('No body yet.')).toBeVisible();
  });

  it('shows the rejection reason on a rejected article', async () => {
    current = {
      ...DRAFT,
      status: 'Rejected',
      allowedActions: ['edit', 'submit'],
      latestReview: {
        decision: 'Rejected',
        reason: 'Needs more sources.',
        decidedAt: '2026-10-27T12:00:00Z',
      },
    };
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-review')).toHaveTextContent('Needs more sources.');
    expect(screen.getByTestId('article-status-badge')).toHaveTextContent('Rejected');
  });
});

describe('safe rendering', () => {
  beforeEach(() => {
    current = {
      ...DRAFT,
      title: `Title ${IMG}`,
      summary: `Summary ${SCRIPT}`,
      body: `Body ${SCRIPT}\n${IMG}\n<b>bold?</b> &amp; &lt;`,
      latestReview: null,
    };
  });

  it('shows HTML in the title, summary and body as literal text', async () => {
    renderAt('/articles/a1');

    const body = await screen.findByTestId('article-view-body');
    expect(body.textContent).toBe(`Body ${SCRIPT}\n${IMG}\n<b>bold?</b> &amp; &lt;`);
    expect(screen.getByTestId('article-view-summary').textContent).toBe(`Summary ${SCRIPT}`);
    expect(screen.getByTestId('article-view-title').textContent).toBe(`Title ${IMG}`);
  });

  it('creates no script, img or other elements from the content', async () => {
    renderAt('/articles/a1');
    await screen.findByTestId('article-view-body');

    expect(document.querySelector('script:not([src])')).toBeNull();
    expect(document.querySelector('img')).toBeNull();
    expect(document.querySelector('b')).toBeNull();
    expect(screen.getByTestId('article-view-body').children).toHaveLength(0);
  });
});

describe('actions follow allowedActions', () => {
  it('offers Edit and Delete for a draft', async () => {
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-edit')).toHaveAttribute('href', '/articles/a1/edit');
    expect(screen.getByTestId('article-delete')).toBeVisible();
  });

  it('offers Edit but not Delete for a rejected article', async () => {
    current = { ...DRAFT, status: 'Rejected', allowedActions: ['edit', 'submit'] };
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-edit')).toBeVisible();
    expect(screen.queryByTestId('article-delete')).not.toBeInTheDocument();
  });

  it.each([
    ['PendingReview', ['withdraw']],
    ['Published', []],
  ] as const)('offers neither Edit nor Delete for a %s article', async (status, actions) => {
    current = { ...DRAFT, status, allowedActions: [...actions] };
    renderAt('/articles/a1');

    await screen.findByTestId('article-status-badge');
    expect(screen.queryByTestId('article-edit')).not.toBeInTheDocument();
    expect(screen.queryByTestId('article-delete')).not.toBeInTheDocument();
  });
});

describe('an article that is missing or not the author’s', () => {
  it('shows "Article not found" with a way back', async () => {
    getResponse = () => problem(404, 'Resource not found.');
    renderAt('/articles/a1');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Article not found' }),
    ).toBeVisible();
    expect(screen.getByRole('link', { name: 'Go to My Articles' })).toHaveAttribute(
      'href',
      '/articles',
    );
  });

  it('shows a retryable error for other failures', async () => {
    getResponse = () => problem(500, 'An unexpected error occurred.');
    renderAt('/articles/a1');

    expect(await screen.findByText('The article could not be loaded')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });
});

describe('deleting a draft', () => {
  it('asks first, naming the draft, with Cancel focused', async () => {
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-delete'));

    const dialog = await screen.findByRole('dialog', { name: 'Delete draft' });
    expect(
      within(dialog).getByText('Delete draft “My draft”? This cannot be undone.'),
    ).toBeVisible();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    expect(deletes()).toHaveLength(0);
  });

  it('keeps the draft when cancelled', async () => {
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-delete'));

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(deletes()).toHaveLength(0);
    expect(screen.getByTestId('article-view-title')).toBeVisible();
  });

  it('keeps the draft when Escape is pressed', async () => {
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-delete'));
    await screen.findByRole('dialog');

    await userEvent.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(deletes()).toHaveLength(0);
  });

  it('deletes with the version and returns to My Articles with a message', async () => {
    const router = renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-delete'));

    await userEvent.click(await screen.findByTestId('article-delete-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/articles'));
    expect(String(deletes()[0]![0])).toContain('/api/articles/a1?version=100');
    expect(await screen.findByTestId('flash-message')).toHaveTextContent(
      'Draft “My draft” deleted.',
    );
  });

  it('reloads the article and says so when the delete is refused with 409', async () => {
    deleteResponse = () => problem(409, 'Conflict');
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-delete'));
    current = { ...DRAFT, title: 'Edited elsewhere', version: 105 };

    await userEvent.click(await screen.findByTestId('article-delete-confirm'));

    expect(await screen.findByTestId('article-notice')).toHaveTextContent(DELETE_CONFLICT_MESSAGE);
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Edited elsewhere' }),
    ).toBeVisible();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows other failures inside the dialog and lets the author try again', async () => {
    deleteResponse = () => problem(500, 'An unexpected error occurred.');
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-delete'));

    await userEvent.click(await screen.findByTestId('article-delete-confirm'));

    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByRole('alert')).toBeVisible();
    expect(within(dialog).getByTestId('article-delete-confirm')).toBeEnabled();
  });
});
