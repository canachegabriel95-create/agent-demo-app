import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReviewArticle } from '../api/reviews';
import { ADMIN, json, problem, renderAt, storeSession, stubApi } from '../test/authHelpers';
import { conflictMessage, NOT_FOUND_TITLE } from './ReviewPage';

const SCRIPT = '<script>alert(1)</script>';
const IMG = '<img src=x onerror=alert(1)>';

const PENDING: ReviewArticle = {
  id: 'a1',
  title: 'An article for review',
  summary: 'The summary',
  body: 'Line one\nLine two',
  category: { id: 'c-sci', name: 'Science' },
  status: 'PendingReview',
  submissionVersion: 2,
  submittedAt: '2026-10-22T15:00:00Z',
  publishedAt: null,
  author: { id: 'u-ann', fullName: 'Ann Author', email: 'ann@demo.local', status: 'Active' },
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

let current: ReviewArticle;
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

function conflict(status = 'Published') {
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
  approveResponse = () => json({ ...current, status: 'Published', canReview: false });
  rejectResponse = () => json({ ...current, status: 'Rejected', canReview: false });
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /api/categories': () => json([{ id: 'c-sci', name: 'Science', slug: 'science' }]),
    'GET /api/admin/articles/a1': () => getResponse(),
    'POST /api/admin/articles/a1/approve': () => approveResponse(),
    'POST /api/admin/articles/a1/reject': () => rejectResponse(),
    'GET /api/admin/reviews/queue': () =>
      json({
        items: [
          {
            articleId: 'a1',
            title: PENDING.title,
            category: { id: 'c-sci', name: 'Science' },
            author: { id: 'u-ann', fullName: 'Ann Author', status: 'Active' },
            submittedAt: PENDING.submittedAt,
            submissionVersion: 2,
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      }),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openReview(path = '/reviews/a1') {
  const router = renderAt(path);
  await screen.findByTestId('review-title');
  return router;
}

describe('what is shown', () => {
  it('shows the header, the author with account status and the submission details', async () => {
    await openReview();

    expect(screen.getByRole('heading', { level: 1, name: 'An article for review' })).toBeVisible();
    expect(screen.getByTestId('review-status')).toHaveTextContent('Pending review');
    expect(screen.getByTestId('review-category')).toHaveTextContent('Science');
    expect(screen.getByTestId('review-submission-version')).toHaveTextContent('2');
    expect(screen.getByText(/Oct 22, 2026/)).toBeVisible();
    expect(screen.getByRole('link', { name: 'Ann Author' })).toHaveAttribute(
      'href',
      '/users/u-ann',
    );
    expect(screen.getByTestId('review-author-email')).toHaveTextContent('ann@demo.local');
    expect(screen.getByTestId('review-author-status')).toHaveTextContent('Active');
  });

  it('shows the summary and body as plain text, with line breaks kept', async () => {
    current = { ...PENDING, summary: `Summary ${SCRIPT}`, body: `Body ${IMG}\n<b>bold?</b>` };
    await openReview();

    expect(screen.getByTestId('review-summary').textContent).toBe(`Summary ${SCRIPT}`);
    expect(screen.getByTestId('review-body').textContent).toBe(`Body ${IMG}\n<b>bold?</b>`);
    expect(document.querySelector('script:not([src])')).toBeNull();
    expect(document.querySelector('img')).toBeNull();
    expect(document.querySelector('b')).toBeNull();
    expect(getComputedStyle(screen.getByTestId('review-body')).whiteSpace).toBeDefined();
  });

  it('has no control that edits the article', async () => {
    await openReview();

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /edit|save/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /edit/i })).not.toBeInTheDocument();
  });

  it('lists earlier decisions with the reviewer, date and reason', async () => {
    await openReview();

    const history = screen.getByTestId('review-history');
    expect(within(history).getByText('Submission 1: Rejected')).toBeVisible();
    expect(within(history).getByText(/by Alice Admin, Oct 20, 2026/)).toBeVisible();
    expect(within(history).getByText('First round: add sources.')).toBeVisible();
  });

  it('says so when there are no decisions yet', async () => {
    current = { ...PENDING, reviewHistory: [] };
    await openReview();

    expect(screen.getByText('No decisions yet.')).toBeVisible();
  });

  it('highlights a suspended author', async () => {
    current = {
      ...PENDING,
      author: { ...PENDING.author, status: 'Suspended' },
      canReview: false,
      reviewBlockedReason: 'Author account is suspended.',
    };
    await openReview();

    expect(screen.getByTestId('review-author-status')).toHaveTextContent('Suspended');
    expect(screen.getByTestId('review-author-status')).toHaveAttribute('data-status', 'Suspended');
  });

  it('shows "Article not found" for an unknown or private article', async () => {
    getResponse = () => problem(404, 'Resource not found.');
    renderAt('/reviews/a1');

    expect(await screen.findByRole('heading', { level: 1, name: NOT_FOUND_TITLE })).toBeVisible();
    expect(screen.getByRole('link', { name: /Back to the review queue/ })).toBeVisible();
  });

  it('shows a retryable error for other failures', async () => {
    getResponse = () => problem(500, 'An unexpected error occurred.');
    renderAt('/reviews/a1');

    expect(await screen.findByText('The article could not be loaded')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });
});

describe('who may decide', () => {
  it('offers Approve and Reject for an eligible article', async () => {
    await openReview();

    expect(screen.getByTestId('review-approve')).toBeEnabled();
    expect(screen.getByTestId('review-reject')).toBeEnabled();
    expect(screen.queryByTestId('review-blocked')).not.toBeInTheDocument();
  });

  it('disables the actions and says why when the author is suspended', async () => {
    current = {
      ...PENDING,
      author: { ...PENDING.author, status: 'Suspended' },
      canReview: false,
      reviewBlockedReason: 'Author account is suspended.',
    };
    await openReview();

    expect(screen.getByTestId('review-blocked')).toHaveTextContent('Author account is suspended.');
    expect(screen.getByTestId('review-approve')).toBeDisabled();
    expect(screen.getByTestId('review-reject')).toBeDisabled();
  });

  it('offers no actions for an article that is already decided, and says why', async () => {
    current = {
      ...PENDING,
      status: 'Published',
      canReview: false,
      reviewBlockedReason: 'Article is not pending review (status Published).',
    };
    await openReview();

    expect(screen.getByTestId('review-blocked')).toHaveTextContent('not pending review');
    expect(screen.queryByTestId('review-approve')).not.toBeInTheDocument();
    expect(screen.queryByTestId('review-reject')).not.toBeInTheDocument();
  });
});

describe('approving', () => {
  it('asks first and does nothing when cancelled', async () => {
    await openReview();
    await userEvent.click(screen.getByTestId('review-approve'));

    const dialog = await screen.findByRole('dialog', { name: 'Approve article' });
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(posts('/approve')).toHaveLength(0);
  });

  it('sends the version and submission version, then returns to the queue with a message', async () => {
    const router = await openReview();
    await userEvent.click(screen.getByTestId('review-approve'));

    await userEvent.click(await screen.findByTestId('review-approve-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/reviews'));
    expect(bodyOf(posts('/approve')[0]!)).toEqual({ version: 500, submissionVersion: 2 });
    expect(await screen.findByTestId('review-flash')).toHaveTextContent(
      'Approved “An article for review”. It is now published.',
    );
  });

  it('returns to the queue with the filters it was opened from', async () => {
    const router = renderAt('/reviews?categoryId=c-sci&direction=desc');
    await userEvent.click(await screen.findByRole('link', { name: 'An article for review' }));
    await screen.findByTestId('review-title');

    await userEvent.click(screen.getByTestId('review-approve'));
    await userEvent.click(await screen.findByTestId('review-approve-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/reviews'));
    expect(router.state.location.search).toBe('?categoryId=c-sci&direction=desc');
  });

  it('shows the message once: a reload of the queue does not repeat it', async () => {
    const router = await openReview();
    await userEvent.click(screen.getByTestId('review-approve'));
    await userEvent.click(await screen.findByTestId('review-approve-confirm'));
    await screen.findByText(/Approved “An article for review”/);

    await waitFor(() => expect(router.state.location.state).toBeNull());
  });

  it('keeps the dialog open with the message for other failures', async () => {
    approveResponse = () => problem(500, 'An unexpected error occurred.');
    await openReview();
    await userEvent.click(screen.getByTestId('review-approve'));

    await userEvent.click(await screen.findByTestId('review-approve-confirm'));

    const dialog = await screen.findByRole('dialog');
    expect(await within(dialog).findByRole('alert')).toBeVisible();
    expect(within(dialog).getByTestId('review-approve-confirm')).toBeEnabled();
  });
});

describe('rejecting', () => {
  async function openRejectDialog() {
    await openReview();
    await userEvent.click(screen.getByTestId('review-reject'));
    return screen.findByRole('dialog', { name: 'Reject article' });
  }

  it('needs a reason before anything is sent', async () => {
    const dialog = await openRejectDialog();

    await userEvent.click(within(dialog).getByTestId('review-reject-confirm'));

    expect(await within(dialog).findByText('A reason is required.')).toBeVisible();
    expect(posts('/reject')).toHaveLength(0);
  });

  it('refuses a reason that is too short and one that is too long', async () => {
    const dialog = await openRejectDialog();
    const reason = within(dialog).getByLabelText(/Reason/);

    await userEvent.type(reason, 'too short');
    await userEvent.click(within(dialog).getByTestId('review-reject-confirm'));
    expect(
      await within(dialog).findByText('The reason must be between 10 and 1000 characters.'),
    ).toBeVisible();

    await userEvent.clear(reason);
    await userEvent.click(reason);
    await userEvent.paste('r'.repeat(1001));
    await userEvent.click(within(dialog).getByTestId('review-reject-confirm'));
    expect(
      await within(dialog).findByText('The reason must be between 10 and 1000 characters.'),
    ).toBeVisible();
    expect(posts('/reject')).toHaveLength(0);
  });

  it('counts the characters', async () => {
    const dialog = await openRejectDialog();

    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'Needs work');

    expect(within(dialog).getByText('10 / 1000 characters (at least 10)')).toBeVisible();
  });

  it('sends the trimmed reason with the versions and returns to the queue with a message', async () => {
    const dialog = await openRejectDialog();
    await userEvent.type(within(dialog).getByLabelText(/Reason/), '  Please add examples.  ');

    await userEvent.click(within(dialog).getByTestId('review-reject-confirm'));

    expect(await screen.findByTestId('review-flash')).toHaveTextContent(
      'Rejected “An article for review”. The author can see your reason.',
    );
    expect(bodyOf(posts('/reject')[0]!)).toEqual({
      version: 500,
      submissionVersion: 2,
      reason: 'Please add examples.',
    });
  });

  it('shows the reason error the API reports', async () => {
    rejectResponse = () =>
      json(
        { title: 'Validation', status: 400, errors: { reason: ['Reason is required.'] } },
        400,
        'application/problem+json',
      );
    const dialog = await openRejectDialog();
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'A long enough reason.');

    await userEvent.click(within(dialog).getByTestId('review-reject-confirm'));

    expect(await within(dialog).findByText('Reason is required.')).toBeVisible();
  });
});

describe('a conflict', () => {
  it.each([
    ['approve', 'review-approve', 'review-approve-confirm'],
    ['reject', 'review-reject', 'review-reject-confirm'],
  ])(
    'explains it in a modal when %s gets a 409, and refreshes on request',
    async (kind, open, confirm) => {
      approveResponse = () => conflict('Published');
      rejectResponse = () => conflict('Published');
      await openReview();
      await userEvent.click(screen.getByTestId(open));
      if (kind === 'reject') {
        await userEvent.type(await screen.findByLabelText(/Reason/), 'A long enough reason.');
      }

      await userEvent.click(await screen.findByTestId(confirm));

      const modal = await screen.findByRole('dialog', { name: 'The article has changed' });
      expect(within(modal).getByTestId('review-conflict-message')).toHaveTextContent(
        conflictMessage('Published'),
      );
      expect(within(modal).getByTestId('review-conflict-message')).toHaveTextContent(
        'current status: Published',
      );
      current = {
        ...PENDING,
        status: 'Published',
        canReview: false,
        reviewBlockedReason: 'Article is not pending review (status Published).',
        version: 600,
      };
      const before = fetchMock.mock.calls.filter(([url]) =>
        String(url).endsWith('/api/admin/articles/a1'),
      ).length;

      await userEvent.click(within(modal).getByTestId('review-refresh'));

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      await waitFor(() =>
        expect(screen.getByTestId('review-status')).toHaveTextContent('Published'),
      );
      expect(
        fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/api/admin/articles/a1'))
          .length,
      ).toBe(before + 1);
      expect(screen.queryByTestId('review-approve')).not.toBeInTheDocument();
    },
  );
});
