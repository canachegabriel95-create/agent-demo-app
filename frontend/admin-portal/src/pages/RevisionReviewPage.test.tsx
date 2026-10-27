import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReviewQueueItem, ReviewRevision } from '../api/reviews';
import { ADMIN, json, problem, renderAt, storeSession, stubApi } from '../test/authHelpers';
import { REVISION_NOT_FOUND_TITLE, revisionConflictMessage } from './RevisionReviewPage';

const SCRIPT = '<script>alert(1)</script>';

const PENDING: ReviewRevision = {
  id: 'r1',
  articleId: 'a1',
  number: 1,
  title: 'The revised title',
  summary: 'The revised summary',
  body: 'Revised line one\nRevised line two',
  category: { id: 'c-tech', name: 'Technology' },
  status: 'PendingReview',
  submissionVersion: 2,
  submittedAt: '2026-10-22T15:00:00Z',
  published: {
    title: 'The published title',
    summary: 'The published summary',
    body: 'Published body',
    category: { id: 'c-sci', name: 'Science' },
    publishedVersion: 1,
    publishedAt: '2026-10-20T10:00:00Z',
  },
  author: { id: 'u-ann', fullName: 'Ann Author', email: 'ann@demo.local', status: 'Active' },
  reviewHistory: [
    {
      submissionVersion: 1,
      decision: 'Rejected',
      reason: 'First round: add sources.',
      decidedAt: '2026-10-21T10:00:00Z',
      reviewer: { id: 'rv1', fullName: 'Alice Admin' },
    },
  ],
  canReview: true,
  reviewBlockedReason: null,
  version: 500,
};

const QUEUE_ITEMS: ReviewQueueItem[] = [
  {
    articleId: 'a1',
    title: PENDING.title,
    category: { id: 'c-tech', name: 'Technology' },
    author: { id: 'u-ann', fullName: 'Ann Author', status: 'Active' },
    submittedAt: PENDING.submittedAt,
    submissionVersion: 2,
    kind: 'Revision',
    revisionId: 'r1',
  },
  {
    articleId: 'a2',
    title: 'A brand new article',
    category: { id: 'c-sci', name: 'Science' },
    author: { id: 'u-bob', fullName: 'Bob Writer', status: 'Active' },
    submittedAt: '2026-10-23T09:00:00Z',
    submissionVersion: 1,
    kind: 'Article',
    revisionId: null,
  },
];

let current: ReviewRevision;
let getResponse: () => Response;
let approveResponse: () => Response;
let rejectResponse: () => Response;
let fetchMock: ReturnType<typeof stubApi>;

function posts(path: string) {
  return fetchMock.mock.calls.filter(
    ([input, init]) => init?.method === 'POST' && String(input).endsWith(path),
  );
}

function bodyOf(call: Parameters<typeof fetch>) {
  return JSON.parse(String(call[1]?.body)) as Record<string, unknown>;
}

function conflict(status = 'Approved') {
  return json(
    {
      title: 'Conflict',
      status: 409,
      detail: 'Changed.',
      currentStatus: status,
      currentVersion: 600,
    },
    409,
    'application/problem+json',
  );
}

beforeEach(() => {
  storeSession();
  current = PENDING;
  getResponse = () => json(current);
  approveResponse = () => json({ ...current, status: 'Approved', canReview: false });
  rejectResponse = () => json({ ...current, status: 'Rejected', canReview: false });
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /api/categories': () => json([{ id: 'c-sci', name: 'Science', slug: 'science' }]),
    'GET /api/admin/revisions/r1': () => getResponse(),
    'POST /api/admin/revisions/r1/approve': () => approveResponse(),
    'POST /api/admin/revisions/r1/reject': () => rejectResponse(),
    'GET /api/admin/reviews/queue': () =>
      json({ items: QUEUE_ITEMS, page: 1, pageSize: 20, totalCount: 2 }),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openRevision(path = '/reviews/revisions/r1') {
  const router = renderAt(path);
  await screen.findByTestId('revision-review-title');
  return router;
}

describe('the review queue with revisions', () => {
  it('tells new articles and revisions apart and opens a revision on its own page', async () => {
    const router = renderAt('/reviews');
    const table = await screen.findByRole('table');

    const kinds = within(table)
      .getAllByTestId('review-row-kind')
      .map((cell) => cell.textContent);
    expect(kinds).toEqual(['Revision', 'New article']);

    await userEvent.click(within(table).getByRole('link', { name: PENDING.title }));
    await waitFor(() => expect(router.state.location.pathname).toBe('/reviews/revisions/r1'));
    expect(await screen.findByTestId('revision-review-title')).toBeVisible();
  });

  it('opens a new article on the article page', async () => {
    const router = renderAt('/reviews');
    const table = await screen.findByRole('table');

    expect(within(table).getByRole('link', { name: 'A brand new article' })).toHaveAttribute(
      'href',
      '/reviews/a2',
    );
    expect(router.state.location.pathname).toBe('/reviews');
  });
});

describe('what is shown', () => {
  it('shows the published version and the revision side by side', async () => {
    await openRevision();

    expect(screen.getByTestId('revision-review-title')).toHaveTextContent(
      'Revision 1: The revised title',
    );
    expect(screen.getByTestId('revision-review-status')).toHaveTextContent('Pending review');
    expect(screen.getByTestId('revision-review-published-version')).toHaveTextContent('1');
    expect(screen.getByTestId('revision-review-submission-version')).toHaveTextContent('2');

    const published = within(screen.getByTestId('revision-review-published'));
    expect(published.getByTestId('revision-review-published-title')).toHaveTextContent(
      'The published title',
    );
    expect(published.getByTestId('revision-review-published-category')).toHaveTextContent(
      'Science',
    );
    const proposed = within(screen.getByTestId('revision-review-proposed'));
    expect(proposed.getByTestId('revision-review-proposed-title')).toHaveTextContent(
      'The revised title',
    );
    expect(proposed.getByTestId('revision-review-proposed-category')).toHaveTextContent(
      'Technology',
    );
    expect(screen.getByTestId('revision-review-author-email')).toHaveTextContent('ann@demo.local');
    expect(screen.getByRole('link', { name: 'Ann Author' })).toHaveAttribute(
      'href',
      '/users/u-ann',
    );
  });

  it('shows the user text as text, never as markup', async () => {
    current = { ...PENDING, summary: `Summary ${SCRIPT}`, body: `Body ${SCRIPT}` };
    await openRevision();

    expect(screen.getByTestId('revision-review-proposed-summary').textContent).toBe(
      `Summary ${SCRIPT}`,
    );
    expect(document.querySelector('script:not([src])')).toBeNull();
  });

  it('shows the earlier decisions with who made them', async () => {
    await openRevision();

    const history = within(screen.getByTestId('revision-review-history'));
    expect(history.getByRole('listitem')).toHaveTextContent('Submission 1: Rejected');
    expect(history.getByRole('listitem')).toHaveTextContent('by Alice Admin');
    expect(history.getByText('First round: add sources.')).toBeVisible();
  });

  it('has no control that edits the revision', async () => {
    await openRevision();

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });
});

describe('loading, not found and errors', () => {
  it('shows a loading state while the revision is being fetched', async () => {
    fetchMock.mockImplementation(async (input) => {
      const path = new URL(String(input)).pathname;
      if (path === '/api/auth/me') {
        return json({ ...ADMIN, status: 'Active' });
      }

      return new Promise<Response>(() => undefined);
    });
    renderAt('/reviews/revisions/r1');

    expect(await screen.findByText('Loading the revision…')).toBeVisible();
  });

  it('says so when the revision does not exist', async () => {
    getResponse = () => problem(404, 'Resource not found.');
    renderAt('/reviews/revisions/r1');

    expect(
      await screen.findByRole('heading', { level: 1, name: REVISION_NOT_FOUND_TITLE }),
    ).toBeVisible();
  });

  it('shows an error with a retry', async () => {
    getResponse = () => problem(500, 'An unexpected error occurred.');
    renderAt('/reviews/revisions/r1');

    expect(await screen.findByText('The revision could not be loaded')).toBeVisible();
    getResponse = () => json(current);
    await userEvent.click(screen.getByRole('button', { name: /try again|retry/i }));
    expect(await screen.findByTestId('revision-review-title')).toBeVisible();
  });
});

describe('approving', () => {
  it('sends the version and the submission, then returns to the queue with a message', async () => {
    const router = await openRevision();

    await userEvent.click(screen.getByTestId('revision-review-approve'));
    await userEvent.click(await screen.findByTestId('revision-review-approve-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/reviews'));
    expect(bodyOf(posts('/approve')[0]!)).toEqual({ version: 500, submissionVersion: 2 });
    expect(await screen.findByTestId('review-flash')).toHaveTextContent(
      'It replaced the published version.',
    );
  });

  it('explains a conflict with the current status and refreshes on request', async () => {
    approveResponse = () => conflict('Approved');
    await openRevision();

    await userEvent.click(screen.getByTestId('revision-review-approve'));
    await userEvent.click(await screen.findByTestId('revision-review-approve-confirm'));

    expect(await screen.findByTestId('revision-review-conflict-message')).toHaveTextContent(
      revisionConflictMessage('Approved'),
    );
    current = { ...PENDING, status: 'Approved', canReview: false, version: 600 };
    await userEvent.click(screen.getByTestId('revision-review-refresh'));
    await waitFor(() =>
      expect(screen.getByTestId('revision-review-status')).toHaveTextContent('Approved'),
    );
    expect(screen.queryByTestId('revision-review-approve')).not.toBeInTheDocument();
  });
});

describe('rejecting', () => {
  it('needs a reason of at least ten characters', async () => {
    await openRevision();
    await userEvent.click(screen.getByTestId('revision-review-reject'));

    await userEvent.type(await screen.findByLabelText(/reason/i), 'too short');
    await userEvent.click(screen.getByTestId('revision-reject-confirm'));

    expect(await screen.findByText(/must be between 10 and 1000 characters/)).toBeVisible();
    expect(posts('/reject')).toHaveLength(0);
  });

  it('sends the reason and returns to the queue', async () => {
    const router = await openRevision();
    await userEvent.click(screen.getByTestId('revision-review-reject'));

    await userEvent.type(await screen.findByLabelText(/reason/i), 'The new introduction is wrong.');
    await userEvent.click(screen.getByTestId('revision-reject-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/reviews'));
    expect(bodyOf(posts('/reject')[0]!)).toEqual({
      version: 500,
      submissionVersion: 2,
      reason: 'The new introduction is wrong.',
    });
    expect(await screen.findByTestId('review-flash')).toHaveTextContent(
      'The published version is unchanged.',
    );
  });

  it('explains a conflict instead of rejecting', async () => {
    rejectResponse = () => conflict('Draft');
    await openRevision();
    await userEvent.click(screen.getByTestId('revision-review-reject'));

    await userEvent.type(await screen.findByLabelText(/reason/i), 'The new introduction is wrong.');
    await userEvent.click(screen.getByTestId('revision-reject-confirm'));

    expect(await screen.findByTestId('revision-review-conflict-message')).toHaveTextContent(
      'current status: Draft',
    );
  });
});

describe('blocked and decided revisions', () => {
  it('disables the decision for a suspended author and says why', async () => {
    current = {
      ...PENDING,
      canReview: false,
      reviewBlockedReason: 'Author account is suspended.',
      author: { ...PENDING.author, status: 'Suspended' },
    };
    await openRevision();

    expect(screen.getByTestId('revision-review-blocked')).toHaveTextContent(
      'Author account is suspended.',
    );
    expect(screen.getByTestId('revision-review-approve')).toBeDisabled();
    expect(screen.getByTestId('revision-review-reject')).toBeDisabled();
  });

  it('offers no decision once the revision was decided', async () => {
    current = {
      ...PENDING,
      status: 'Rejected',
      canReview: false,
      reviewBlockedReason: 'Revision is not pending review (status Rejected).',
    };
    await openRevision();

    expect(screen.queryByTestId('revision-review-approve')).not.toBeInTheDocument();
    expect(screen.getByTestId('revision-review-blocked')).toBeVisible();
  });
});
