import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Article, ArticleReviewEntry } from '../api/articles';
import { AUTHOR, json, problem, renderAt, storeSession, stubApi } from '../test/authHelpers';
import {
  DELETE_CONFLICT_MESSAGE,
  PENDING_NOTICE,
  PUBLISHED_NOTICE,
  REVISE_CONFLICT_MESSAGE,
  STATUS_CONFLICT_MESSAGE,
} from './ArticlePage';

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
  openRevision: null,
  publishedVersion: 0,
  version: 100,
};

let current: Article;
let getResponse: () => Response;
let deleteResponse: () => Response;
let reviews: ArticleReviewEntry[];
let reviewsResponse: () => Response;
let submitResponse: () => Response;
let withdrawResponse: () => Response;
let fetchMock: ReturnType<typeof stubApi>;

function deletes() {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE');
}

beforeEach(() => {
  storeSession();
  current = DRAFT;
  getResponse = () => json(current);
  deleteResponse = () => new Response(null, { status: 204 });
  reviews = [];
  reviewsResponse = () => json(reviews);
  submitResponse = () =>
    json({
      ...current,
      status: 'PendingReview',
      submissionVersion: 1,
      allowedActions: ['withdraw'],
      version: 101,
    });
  withdrawResponse = () =>
    json({
      ...current,
      status: 'Draft',
      allowedActions: ['edit', 'delete', 'submit'],
      version: 102,
    });
  fetchMock = stubApi({
    'GET /api/auth/me': () => json(AUTHOR),
    'GET /api/articles/a1': () => getResponse(),
    'DELETE /api/articles/a1': () => deleteResponse(),
    'GET /api/articles/a1/reviews': () => reviewsResponse(),
    'POST /api/articles/a1/submit': () => submitResponse(),
    'POST /api/articles/a1/withdraw': () => withdrawResponse(),
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
    ['Published', ['revise']],
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

const REJECTED: Article = {
  ...DRAFT,
  status: 'Rejected',
  submissionVersion: 1,
  allowedActions: ['edit', 'submit'],
  latestReview: {
    decision: 'Rejected',
    reason: 'Needs more sources. <b>Really</b>',
    decidedAt: '2026-10-27T12:00:00Z',
  },
};

function posts(path: string) {
  return fetchMock.mock.calls.filter(
    ([input, init]) => init?.method === 'POST' && String(input).endsWith(path),
  );
}

describe('lifecycle buttons follow allowedActions', () => {
  it('offers "Submit for review" for a draft', async () => {
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-submit')).toHaveTextContent('Submit for review');
    expect(screen.queryByTestId('article-withdraw')).not.toBeInTheDocument();
  });

  it('offers "Resubmit for review" for a rejected article', async () => {
    current = REJECTED;
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-submit')).toHaveTextContent('Resubmit for review');
    expect(screen.getByTestId('article-edit')).toBeVisible();
    expect(screen.queryByTestId('article-delete')).not.toBeInTheDocument();
  });

  it('offers only "Withdraw submission" while pending review, with a notice', async () => {
    current = { ...DRAFT, status: 'PendingReview', allowedActions: ['withdraw'] };
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-withdraw')).toHaveTextContent('Withdraw submission');
    expect(screen.getByTestId('article-readonly-notice')).toHaveTextContent(PENDING_NOTICE);
    expect(screen.queryByTestId('article-edit')).not.toBeInTheDocument();
    expect(screen.queryByTestId('article-delete')).not.toBeInTheDocument();
    expect(screen.queryByTestId('article-submit')).not.toBeInTheDocument();
  });

  it('shows a published article as not editable, with only the revise action', async () => {
    current = { ...DRAFT, status: 'Published', allowedActions: ['revise'], publishedVersion: 1 };
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-readonly-notice')).toHaveTextContent(
      PUBLISHED_NOTICE,
    );
    expect(
      screen.queryByRole('button', { name: /submit|withdraw|delete/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByTestId('article-edit')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Revise this article' })).toBeVisible();
  });
});

describe('revising a published article', () => {
  const PUBLISHED: Article = {
    ...DRAFT,
    status: 'Published',
    allowedActions: ['revise'],
    publishedVersion: 1,
    publishedAt: '2026-10-27T12:00:00Z',
  };
  const REVISION = {
    id: 'r1',
    articleId: 'a1',
    title: 'My draft',
    summary: 'A summary',
    body: 'Line one\nLine two',
    category: { id: 'c2', name: 'Science' },
    status: 'Draft',
    submissionVersion: 0,
    createdAt: '2026-10-28T10:00:00Z',
    updatedAt: '2026-10-28T10:00:00Z',
    submittedAt: null,
    latestReview: null,
    reviews: [],
    allowedActions: ['edit', 'delete', 'submit'],
    version: 7,
  };
  let createResponse: () => Response;

  beforeEach(() => {
    current = PUBLISHED;
    createResponse = () => json(REVISION, 201);
    fetchMock.mockRestore();
    fetchMock = stubApi({
      'GET /api/auth/me': () => json(AUTHOR),
      'GET /api/articles/a1': () => getResponse(),
      'GET /api/articles/a1/reviews': () => reviewsResponse(),
      'POST /api/articles/a1/revisions': () => createResponse(),
      'GET /api/revisions/r1': () => json(REVISION),
      'GET /api/categories': () => json([]),
    });
  });

  it('starts a revision and opens it', async () => {
    const router = renderAt('/articles/a1');

    await userEvent.click(await screen.findByTestId('article-revise'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/revisions/r1'));
    expect(
      fetchMock.mock.calls.some(
        ([url, init]) =>
          String(url).endsWith('/api/articles/a1/revisions') && init?.method === 'POST',
      ),
    ).toBe(true);
    expect(await screen.findByRole('heading', { level: 1, name: /Revision of/ })).toBeVisible();
  });

  it('does not offer to revise while a revision is open, and links to it with its status', async () => {
    current = {
      ...PUBLISHED,
      allowedActions: [],
      openRevision: {
        id: 'r1',
        status: 'PendingReview',
        updatedAt: '2026-10-28T10:00:00Z',
        submittedAt: '2026-10-28T10:00:00Z',
      },
    };
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-revision-status')).toHaveTextContent(
      'Revision pending review',
    );
    expect(screen.getByTestId('article-revision-link')).toHaveAttribute('href', '/revisions/r1');
    expect(screen.queryByTestId('article-revise')).not.toBeInTheDocument();
  });

  it('does not show a revision panel for an article without one', async () => {
    renderAt('/articles/a1');

    await screen.findByTestId('article-revise');
    expect(screen.queryByTestId('article-revision')).not.toBeInTheDocument();
  });

  it('explains a conflict when a revision is already in progress and stays on the article', async () => {
    createResponse = () => problem(409, 'Conflict');
    const router = renderAt('/articles/a1');

    await userEvent.click(await screen.findByTestId('article-revise'));

    expect(await screen.findByTestId('article-notice')).toHaveTextContent(REVISE_CONFLICT_MESSAGE);
    expect(router.state.location.pathname).toBe('/articles/a1');
    expect(screen.getByTestId('article-revise')).toBeEnabled();
  });

  it('shows another failure instead of silently doing nothing', async () => {
    createResponse = () => problem(500, 'Server error');
    renderAt('/articles/a1');

    await userEvent.click(await screen.findByTestId('article-revise'));

    expect(await screen.findByTestId('article-notice')).toBeVisible();
  });
});

describe('submitting and withdrawing', () => {
  it('asks first, then submits with the version and shows the new status', async () => {
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-submit'));

    const dialog = await screen.findByRole('dialog', { name: 'Submit for review' });
    expect(within(dialog).getByText(/read-only until an administrator decides/)).toBeVisible();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    expect(posts('/submit')).toHaveLength(0);

    await userEvent.click(within(dialog).getByTestId('article-submit-confirm'));

    expect(await screen.findByTestId('article-flash')).toHaveTextContent('Submitted for review.');
    expect(JSON.parse(String(posts('/submit')[0]![1]?.body))).toEqual({ version: 100 });
    expect(screen.getByTestId('article-status-badge')).toHaveTextContent('Pending review');
    expect(screen.getByTestId('article-withdraw')).toBeVisible();
    expect(screen.queryByTestId('article-submit')).not.toBeInTheDocument();
    expect(screen.queryByTestId('article-edit')).not.toBeInTheDocument();
  });

  it('does nothing when the submit confirmation is cancelled', async () => {
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-submit'));

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(posts('/submit')).toHaveLength(0);
  });

  it('lists each broken submission rule with a link to the editor on a 400', async () => {
    submitResponse = () =>
      json(
        {
          title: 'Validation',
          status: 400,
          errors: {
            summary: ['Summary is required.'],
            categoryId: ['Category is required.'],
          },
        },
        400,
        'application/problem+json',
      );
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-submit'));

    await userEvent.click(await screen.findByTestId('article-submit-confirm'));

    const panel = await screen.findByTestId('article-submit-errors');
    expect(within(panel).getByText('Summary is required.')).toBeVisible();
    expect(within(panel).getByText('Category is required.')).toBeVisible();
    expect(within(panel).getByTestId('article-fix-in-editor')).toHaveAttribute(
      'href',
      '/articles/a1/edit',
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByTestId('article-status-badge')).toHaveTextContent('Draft');
  });

  it('asks the author to refresh on a 409 and reloads when they do', async () => {
    submitResponse = () => problem(409, 'Conflict');
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-submit'));
    await userEvent.click(await screen.findByTestId('article-submit-confirm'));

    expect(await screen.findByTestId('article-notice')).toHaveTextContent(STATUS_CONFLICT_MESSAGE);
    current = { ...DRAFT, status: 'Published', allowedActions: ['revise'], version: 120 };

    await userEvent.click(screen.getByTestId('article-refresh'));

    expect(await screen.findByTestId('article-readonly-notice')).toHaveTextContent(
      PUBLISHED_NOTICE,
    );
    expect(screen.queryByTestId('article-notice')).not.toBeInTheDocument();
  });

  it('withdraws after confirmation and offers the draft actions again', async () => {
    current = {
      ...DRAFT,
      status: 'PendingReview',
      submissionVersion: 1,
      allowedActions: ['withdraw'],
    };
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-withdraw'));

    const dialog = await screen.findByRole('dialog', { name: 'Withdraw submission' });
    expect(posts('/withdraw')).toHaveLength(0);
    await userEvent.click(within(dialog).getByTestId('article-withdraw-confirm'));

    expect(await screen.findByTestId('article-flash')).toHaveTextContent('Submission withdrawn.');
    expect(JSON.parse(String(posts('/withdraw')[0]![1]?.body))).toEqual({ version: 100 });
    expect(screen.getByTestId('article-status-badge')).toHaveTextContent('Draft');
    expect(screen.getByTestId('article-submit')).toBeVisible();
    expect(screen.getByTestId('article-edit')).toBeVisible();
  });

  it('tells the author to refresh when an administrator decided before the withdraw', async () => {
    current = { ...DRAFT, status: 'PendingReview', allowedActions: ['withdraw'] };
    withdrawResponse = () => problem(409, 'Conflict');
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-withdraw'));

    await userEvent.click(await screen.findByTestId('article-withdraw-confirm'));

    expect(await screen.findByTestId('article-notice')).toHaveTextContent(STATUS_CONFLICT_MESSAGE);
    expect(screen.getByTestId('article-refresh')).toBeVisible();
  });

  it('keeps the dialog open with the message for other failures', async () => {
    submitResponse = () => problem(500, 'An unexpected error occurred.');
    renderAt('/articles/a1');
    await userEvent.click(await screen.findByTestId('article-submit'));

    await userEvent.click(await screen.findByTestId('article-submit-confirm'));

    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByRole('alert')).toBeVisible();
    expect(within(dialog).getByTestId('article-submit-confirm')).toBeEnabled();
  });
});

describe('rejection feedback and review history', () => {
  it('shows the latest rejection with its date and reason as plain text, above the content', async () => {
    current = REJECTED;
    renderAt('/articles/a1');

    const panel = await screen.findByTestId('article-review');
    expect(panel).toHaveTextContent(/Rejected on .*2026/);
    expect(screen.getByTestId('article-review-reason').textContent).toBe(
      'Needs more sources. <b>Really</b>',
    );
    expect(panel.querySelector('b')).toBeNull();
    expect(
      panel.compareDocumentPosition(screen.getByTestId('article-view-body')) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('does not show a rejection panel for a draft that was withdrawn after a rejection', async () => {
    current = { ...REJECTED, status: 'Draft', allowedActions: ['edit', 'delete', 'submit'] };
    renderAt('/articles/a1');

    await screen.findByTestId('article-status-badge');
    expect(screen.queryByTestId('article-review')).not.toBeInTheDocument();
  });

  it('lists the decisions of every submission, newest first, without who decided', async () => {
    current = {
      ...REJECTED,
      status: 'Published',
      allowedActions: ['revise'],
      submissionVersion: 2,
    };
    reviews = [
      {
        submissionVersion: 2,
        decision: 'Approved',
        reason: null,
        decidedAt: '2026-10-27T14:00:00Z',
      },
      {
        submissionVersion: 1,
        decision: 'Rejected',
        reason: 'First round feedback.',
        decidedAt: '2026-10-27T12:00:00Z',
      },
    ];
    renderAt('/articles/a1');

    const history = await screen.findByTestId('article-history');
    const items = await within(history).findAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Submission 2: Approved');
    expect(items[1]).toHaveTextContent('Submission 1: Rejected');
    expect(items[1]).toHaveTextContent('First round feedback.');
  });

  it('says so when there are no decisions yet', async () => {
    renderAt('/articles/a1');

    expect(await screen.findByText('No decisions yet.')).toBeVisible();
  });

  it('offers a retry when the history cannot be loaded', async () => {
    reviewsResponse = () => problem(500, 'An unexpected error occurred.');
    renderAt('/articles/a1');

    expect(await screen.findByText('The review history could not be loaded.')).toBeVisible();
    reviewsResponse = () => json(reviews);

    await userEvent.click(
      within(screen.getByTestId('article-history')).getByRole('button', { name: 'Try again' }),
    );

    expect(await screen.findByText('No decisions yet.')).toBeVisible();
  });
});
