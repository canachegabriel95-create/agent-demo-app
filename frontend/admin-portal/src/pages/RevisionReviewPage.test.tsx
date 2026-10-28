import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReviewRevision } from '../api/reviews';
import { ADMIN, json, problem, renderAt, storeSession, stubApi } from '../test/authHelpers';
import { conflictMessage, NOT_FOUND_TITLE } from './RevisionReviewPage';

const SCRIPT = '<script>alert(1)</script>';

const PENDING: ReviewRevision = {
  id: 'rev1',
  articleId: 'a1',
  title: 'A revised title',
  summary: 'A revised summary of the article.',
  body: 'Revised line one\nRevised line two',
  category: { id: 'c-tech', name: 'Technology' },
  status: 'PendingReview',
  submissionVersion: 2,
  submittedAt: '2026-10-22T15:00:00Z',
  author: { id: 'u-ann', fullName: 'Ann Author', email: 'ann@demo.local', status: 'Active' },
  published: {
    versionNumber: 1,
    title: 'The published title',
    summary: 'The published summary of the article.',
    body: 'Published line one\nPublished line two',
    category: { id: 'c-sci', name: 'Science' },
    publishedAt: '2026-10-10T09:00:00Z',
  },
  reviewHistory: [
    {
      submissionVersion: 1,
      decision: 'Rejected',
      reason: 'First round: add sources.',
      decidedAt: '2026-10-20T10:00:00Z',
      reviewer: { id: 'r1', fullName: 'Alice Admin' },
    },
  ],
  canReview: true,
  reviewBlockedReason: null,
  version: 500,
};

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
    'GET /api/admin/revisions/rev1': () => getResponse(),
    'POST /api/admin/revisions/rev1/approve': () => approveResponse(),
    'POST /api/admin/revisions/rev1/reject': () => rejectResponse(),
    'GET /api/admin/reviews/queue': () => json({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openReview(path = '/reviews/revisions/rev1') {
  const router = renderAt(path);
  await screen.findByTestId('revision-review-title');
  return router;
}

describe('what is shown', () => {
  it('names the revision, its status and the author with account status', async () => {
    await openReview();

    expect(
      screen.getByRole('heading', { level: 1, name: 'Revision of “The published title”' }),
    ).toBeVisible();
    expect(screen.getByTestId('revision-review-status')).toHaveTextContent('Pending review');
    expect(screen.getByTestId('revision-review-kind')).toHaveTextContent(
      'Revision of a published article',
    );
    expect(screen.getByTestId('revision-review-submission-version')).toHaveTextContent('2');
    expect(screen.getByRole('link', { name: 'Ann Author' })).toHaveAttribute(
      'href',
      '/users/u-ann',
    );
    expect(screen.getByTestId('revision-review-author-email')).toHaveTextContent('ann@demo.local');
    expect(screen.getByTestId('revision-review-author-status')).toHaveTextContent('Active');
  });

  it('shows the published version next to the revision', async () => {
    await openReview();

    const published = screen.getByTestId('revision-published');
    const proposed = screen.getByTestId('revision-proposed');
    expect(within(published).getByRole('heading', { name: 'Published version 1' })).toBeVisible();
    expect(within(published).getByTestId('published-title')).toHaveTextContent(
      'The published title',
    );
    expect(within(published).getByTestId('published-category')).toHaveTextContent('Science');
    expect(within(published).getByTestId('published-body').textContent).toBe(
      'Published line one\nPublished line two',
    );
    expect(within(proposed).getByTestId('revision-title')).toHaveTextContent('A revised title');
    expect(within(proposed).getByTestId('revision-category')).toHaveTextContent('Technology');
    expect(within(proposed).getByTestId('revision-body').textContent).toBe(
      'Revised line one\nRevised line two',
    );
  });

  it('marks what changed and nothing else', async () => {
    current = {
      ...PENDING,
      title: PENDING.published.title,
      category: PENDING.published.category,
      summary: PENDING.published.summary,
    };
    await openReview();

    expect(screen.getByTestId('changed-body')).toBeVisible();
    expect(screen.queryByTestId('changed-title')).not.toBeInTheDocument();
    expect(screen.queryByTestId('changed-category')).not.toBeInTheDocument();
    expect(screen.queryByTestId('changed-summary')).not.toBeInTheDocument();
  });

  it('marks every changed field', async () => {
    await openReview();

    for (const field of ['title', 'category', 'summary', 'body']) {
      expect(screen.getByTestId(`changed-${field}`)).toBeVisible();
    }
  });

  it('lists earlier decisions on this revision with who decided', async () => {
    await openReview();

    const history = screen.getByTestId('revision-review-history');
    expect(within(history).getByText('Submission 1: Rejected')).toBeVisible();
    expect(within(history).getByText(/by Alice Admin/)).toBeVisible();
    expect(within(history).getByText('First round: add sources.')).toBeVisible();
  });

  it('says so when there are no decisions yet', async () => {
    current = { ...PENDING, reviewHistory: [] };
    await openReview();

    expect(
      within(screen.getByTestId('revision-review-history')).getByText('No decisions yet.'),
    ).toBeVisible();
  });
});

describe('safe rendering', () => {
  it('shows HTML in both versions as literal text', async () => {
    current = {
      ...PENDING,
      title: `Rev ${SCRIPT}`,
      body: `Body ${SCRIPT}`,
      published: { ...PENDING.published, title: `Pub ${SCRIPT}`, body: `Old ${SCRIPT}` },
    };
    await openReview();

    expect(screen.getByTestId('revision-body').textContent).toBe(`Body ${SCRIPT}`);
    expect(screen.getByTestId('published-body').textContent).toBe(`Old ${SCRIPT}`);
    expect(screen.getByTestId('revision-title').textContent).toBe(`Rev ${SCRIPT}`);
    expect(document.querySelector('script')).toBeNull();
  });
});

describe('approving', () => {
  it('asks for confirmation, sends version and submission, and returns to the queue with a message', async () => {
    const router = await openReview();

    await userEvent.click(screen.getByTestId('revision-review-approve'));
    expect(await screen.findByRole('dialog')).toHaveTextContent(/Replace the published version/);
    await userEvent.click(screen.getByTestId('revision-review-approve-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/reviews'));
    expect(bodyOf(posts('/approve')[0]!)).toEqual({ version: 500, submissionVersion: 2 });
    expect(await screen.findByTestId('review-flash')).toHaveTextContent(
      /replaces the published version/,
    );
  });

  it('can be cancelled without sending anything', async () => {
    await openReview();

    await userEvent.click(screen.getByTestId('revision-review-approve'));
    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }));

    expect(posts('/approve')).toHaveLength(0);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('returns to the queue with the filters the administrator came from', async () => {
    const router = renderAt('/reviews');
    router.navigate('/reviews/revisions/rev1', { state: { queueSearch: '?categoryId=c-tech' } });
    await screen.findByTestId('revision-review-title');

    await userEvent.click(screen.getByTestId('revision-review-approve'));
    await userEvent.click(await screen.findByTestId('revision-review-approve-confirm'));

    await waitFor(() => expect(router.state.location.search).toBe('?categoryId=c-tech'));
  });

  it('explains a conflict, shows the current status and reloads on Refresh', async () => {
    approveResponse = () => conflict('Approved');
    await openReview();

    await userEvent.click(screen.getByTestId('revision-review-approve'));
    await userEvent.click(await screen.findByTestId('revision-review-approve-confirm'));

    expect(await screen.findByTestId('revision-review-conflict-message')).toHaveTextContent(
      conflictMessage('Approved'),
    );
    current = { ...PENDING, status: 'Approved', canReview: false, version: 600 };
    await userEvent.click(screen.getByTestId('revision-review-refresh'));

    await waitFor(() =>
      expect(screen.getByTestId('revision-review-status')).toHaveTextContent('Approved'),
    );
    expect(screen.queryByTestId('revision-review-approve')).not.toBeInTheDocument();
  });

  it('shows another failure in the dialog and keeps it open', async () => {
    approveResponse = () => problem(500, 'Server error');
    await openReview();

    await userEvent.click(screen.getByTestId('revision-review-approve'));
    await userEvent.click(await screen.findByTestId('revision-review-approve-confirm'));

    expect(await screen.findByRole('alert')).toBeVisible();
    expect(screen.getByRole('dialog')).toBeVisible();
  });
});

describe('rejecting', () => {
  it('needs a reason of at least ten characters', async () => {
    await openReview();
    await userEvent.click(screen.getByTestId('revision-review-reject'));

    await userEvent.type(await screen.findByLabelText(/Reason/), 'too short');
    await userEvent.click(screen.getByTestId('review-reject-confirm'));

    expect(
      await screen.findByText(/The reason must be between 10 and 1000 characters/),
    ).toBeVisible();
    expect(posts('/reject')).toHaveLength(0);
  });

  it('sends the trimmed reason to the revision endpoint and returns to the queue', async () => {
    const router = await openReview();
    await userEvent.click(screen.getByTestId('revision-review-reject'));
    expect(await screen.findByRole('dialog')).toHaveTextContent(
      /The published version stays as it is/,
    );

    await userEvent.type(screen.getByLabelText(/Reason/), '  Please add concrete sources.  ');
    await userEvent.click(screen.getByTestId('review-reject-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/reviews'));
    expect(bodyOf(posts('/reject')[0]!)).toEqual({
      version: 500,
      submissionVersion: 2,
      reason: 'Please add concrete sources.',
    });
    expect(await screen.findByTestId('review-flash')).toHaveTextContent(/Rejected the revision/);
  });

  it('explains a conflict from the reject dialog', async () => {
    rejectResponse = () => conflict('Draft');
    await openReview();
    await userEvent.click(screen.getByTestId('revision-review-reject'));

    await userEvent.type(await screen.findByLabelText(/Reason/), 'Please add concrete sources.');
    await userEvent.click(screen.getByTestId('review-reject-confirm'));

    expect(await screen.findByTestId('revision-review-conflict-message')).toHaveTextContent(
      'current status: Draft',
    );
  });
});

describe('when the revision cannot be reviewed', () => {
  it('disables the decision and says why for a suspended author', async () => {
    current = {
      ...PENDING,
      author: { ...PENDING.author, status: 'Suspended' },
      canReview: false,
      reviewBlockedReason: 'Author account is suspended.',
    };
    await openReview();

    expect(screen.getByTestId('revision-review-blocked')).toHaveTextContent(
      'Author account is suspended.',
    );
    expect(screen.getByTestId('revision-review-approve')).toBeDisabled();
    expect(screen.getByTestId('revision-review-reject')).toBeDisabled();
    expect(screen.getByTestId('revision-review-author-status')).toHaveTextContent('Suspended');
  });

  it('offers no decision for a revision that was already decided', async () => {
    current = {
      ...PENDING,
      status: 'Approved',
      canReview: false,
      reviewBlockedReason: 'Revision is not pending review (status Approved).',
    };
    await openReview();

    expect(screen.getByTestId('revision-review-status')).toHaveTextContent('Approved');
    expect(screen.queryByTestId('revision-review-approve')).not.toBeInTheDocument();
    expect(screen.queryByTestId('revision-review-reject')).not.toBeInTheDocument();
    expect(screen.getByTestId('revision-review-blocked')).toBeVisible();
  });
});

describe('loading', () => {
  it('explains an unknown or never-submitted revision', async () => {
    getResponse = () => problem(404, 'Resource not found.');
    renderAt('/reviews/revisions/rev1');

    expect(await screen.findByRole('heading', { name: NOT_FOUND_TITLE })).toBeVisible();
    expect(screen.getByRole('link', { name: /Back to the review queue/ })).toHaveAttribute(
      'href',
      '/reviews',
    );
  });

  it('offers another try after a failure', async () => {
    getResponse = () => problem(500, 'Server error');
    renderAt('/reviews/revisions/rev1');

    expect(await screen.findByText('The revision could not be loaded')).toBeVisible();
    getResponse = () => json(current);
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('revision-review-title')).toBeVisible();
  });
});
