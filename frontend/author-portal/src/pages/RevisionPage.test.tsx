import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Revision } from '../api/revisions';
import { AUTHOR, json, problem, renderAt, storeSession, stubApi } from '../test/authHelpers';
import {
  APPROVED_NOTICE,
  CONFLICT_MESSAGE,
  PENDING_NOTICE,
  PUBLISHED_STAYS_NOTICE,
} from './RevisionPage';

const SCRIPT = '<script>alert(1)</script>';
const LONG_SUMMARY = 'A summary that is long enough to be submitted.';
const LONG_BODY = 'b'.repeat(120);
const CATEGORIES = [
  { id: 'c1', name: 'Technology' },
  { id: 'c2', name: 'Science' },
];

const DRAFT: Revision = {
  id: 'r1',
  articleId: 'a1',
  title: 'Revised title',
  summary: LONG_SUMMARY,
  body: LONG_BODY,
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

let current: Revision;
let getResponse: () => Response;
let putResponse: (body: Record<string, unknown>) => Response;
let submitResponse: () => Response;
let withdrawResponse: () => Response;
let deleteResponse: () => Response;
let fetchMock: ReturnType<typeof stubApi>;

function calls(method: string, suffix = '') {
  return fetchMock.mock.calls.filter(
    ([url, init]) =>
      init?.method === method && new URL(String(url)).pathname === `/api/revisions/r1${suffix}`,
  );
}

function sentBody(method: string, suffix = '') {
  const [, init] = calls(method, suffix).at(-1)!;
  return JSON.parse(String(init?.body)) as Record<string, unknown>;
}

beforeEach(() => {
  storeSession();
  current = DRAFT;
  getResponse = () => json(current);
  putResponse = (body) => json({ ...current, ...body, version: current.version + 1 });
  submitResponse = () =>
    json({
      ...current,
      status: 'PendingReview',
      submissionVersion: 1,
      submittedAt: '2026-10-28T11:00:00Z',
      allowedActions: ['withdraw'],
      version: current.version + 1,
    });
  withdrawResponse = () =>
    json({ ...current, status: 'Draft', allowedActions: ['edit', 'delete', 'submit'], version: 9 });
  deleteResponse = () => new Response(null, { status: 204 });
  fetchMock = stubApi({
    'GET /api/auth/me': () => json(AUTHOR),
    'GET /api/categories': () => json(CATEGORIES),
    'GET /api/revisions/r1': () => getResponse(),
    'PUT /api/revisions/r1': (init) => putResponse(JSON.parse(String(init?.body))),
    'POST /api/revisions/r1/submit': () => submitResponse(),
    'POST /api/revisions/r1/withdraw': () => withdrawResponse(),
    'DELETE /api/revisions/r1': () => deleteResponse(),
    'GET /api/articles/a1': () => problem(404, 'not needed'),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('a draft revision', () => {
  it('shows the editor with the revision content and says the published version stays visible', async () => {
    renderAt('/revisions/r1');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Revision of “Revised title”' }),
    ).toBeVisible();
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent('Revision draft');
    expect(screen.getByTestId('revision-published-notice')).toHaveTextContent(
      PUBLISHED_STAYS_NOTICE,
    );
    expect(screen.getByTestId('revision-published-link')).toHaveAttribute('href', '/articles/a1');
    expect(screen.getByTestId('revision-title')).toHaveValue('Revised title');
    expect(screen.getByTestId('revision-summary')).toHaveValue(LONG_SUMMARY);
    expect(screen.getByTestId('revision-body')).toHaveValue(LONG_BODY);
    await waitFor(() => expect(screen.getByTestId('revision-category')).toHaveValue('c2'));
    expect(screen.getByTestId('revision-submit')).toHaveTextContent('Submit for review');
    expect(screen.getByTestId('revision-discard')).toBeVisible();
  });

  it('saves the changes with the current version and confirms it', async () => {
    renderAt('/revisions/r1');
    const title = await screen.findByTestId('revision-title');

    await userEvent.clear(title);
    await userEvent.type(title, 'A better title');
    await userEvent.click(screen.getByTestId('revision-save'));

    await waitFor(() => expect(calls('PUT')).toHaveLength(1));
    expect(sentBody('PUT')).toMatchObject({
      title: 'A better title',
      version: 7,
      categoryId: 'c2',
    });
    expect(await screen.findByTestId('revision-flash')).toHaveTextContent(/Revision saved at/);
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Revision of “A better title”',
    );
  });

  it('asks for a title before it saves anything', async () => {
    renderAt('/revisions/r1');
    const title = await screen.findByTestId('revision-title');

    await userEvent.clear(title);
    await userEvent.click(screen.getByTestId('revision-save'));

    expect(await screen.findByText('Title is required.')).toBeVisible();
    expect(calls('PUT')).toHaveLength(0);
  });

  it('puts the server field messages on the fields', async () => {
    putResponse = () =>
      json(
        {
          title: 'Invalid',
          status: 400,
          errors: { summary: ['Summary must be at most 500 characters.'] },
        },
        400,
        'application/problem+json',
      );
    renderAt('/revisions/r1');
    await userEvent.type(await screen.findByTestId('revision-title'), ' more');

    await userEvent.click(screen.getByTestId('revision-save'));

    expect(await screen.findByText('Summary must be at most 500 characters.')).toBeVisible();
  });

  it('saves unsaved changes first, then submits after confirmation, and becomes read-only', async () => {
    renderAt('/revisions/r1');
    await userEvent.type(await screen.findByTestId('revision-title'), ' v2');

    await userEvent.click(screen.getByTestId('revision-submit'));
    await userEvent.click(await screen.findByTestId('revision-submit-confirm'));

    await waitFor(() => expect(calls('POST', '/submit')).toHaveLength(1));
    expect(calls('PUT')).toHaveLength(1);
    expect(sentBody('POST', '/submit')).toEqual({ version: 8 }); // the version the save returned
    expect(await screen.findByTestId('revision-readonly-notice')).toHaveTextContent(PENDING_NOTICE);
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent(
      'Revision pending review',
    );
    expect(screen.queryByTestId('revision-title')).not.toBeInTheDocument();
    expect(screen.getByTestId('revision-flash')).toHaveTextContent(
      /published version stays visible/,
    );
  });

  it('submits without saving when nothing changed', async () => {
    renderAt('/revisions/r1');

    await userEvent.click(await screen.findByTestId('revision-submit'));
    await userEvent.click(await screen.findByTestId('revision-submit-confirm'));

    await waitFor(() => expect(calls('POST', '/submit')).toHaveLength(1));
    expect(calls('PUT')).toHaveLength(0);
    expect(sentBody('POST', '/submit')).toEqual({ version: 7 });
  });

  it('shows the submission rules the API reports and stays a draft', async () => {
    submitResponse = () =>
      json(
        {
          title: 'Invalid',
          status: 400,
          errors: { summary: ['Summary is required.'], categoryId: ['Category is required.'] },
        },
        400,
        'application/problem+json',
      );
    renderAt('/revisions/r1');

    await userEvent.click(await screen.findByTestId('revision-submit'));
    await userEvent.click(await screen.findByTestId('revision-submit-confirm'));

    expect(await screen.findByText('Summary is required.')).toBeVisible();
    expect(screen.getByText('Category is required.')).toBeVisible();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent('Revision draft');
  });

  it('tells the author when the revision changed elsewhere and reloads on request', async () => {
    putResponse = () => problem(409, 'Conflict');
    renderAt('/revisions/r1');
    await userEvent.type(await screen.findByTestId('revision-title'), ' more');
    await userEvent.click(screen.getByTestId('revision-save'));

    expect(await screen.findByTestId('revision-conflict')).toHaveTextContent(CONFLICT_MESSAGE);
    expect(screen.getByTestId('revision-save')).toBeDisabled();

    current = { ...DRAFT, title: 'Changed elsewhere', version: 9 };
    await userEvent.click(screen.getByTestId('revision-refresh'));

    await waitFor(() =>
      expect(screen.getByTestId('revision-title')).toHaveValue('Changed elsewhere'),
    );
    expect(screen.queryByTestId('revision-conflict')).not.toBeInTheDocument();
  });

  it('discards the revision after confirmation and returns to the published article', async () => {
    const router = renderAt('/revisions/r1');

    await userEvent.click(await screen.findByTestId('revision-discard'));
    await userEvent.click(await screen.findByTestId('revision-discard-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/articles/a1'));
    expect(calls('DELETE')).toHaveLength(1);
    expect(String(calls('DELETE')[0]![0])).toContain('version=7');
  });

  it('keeps the revision when discarding conflicts', async () => {
    deleteResponse = () => problem(409, 'Conflict');
    const router = renderAt('/revisions/r1');

    await userEvent.click(await screen.findByTestId('revision-discard'));
    await userEvent.click(await screen.findByTestId('revision-discard-confirm'));

    expect(await screen.findByTestId('revision-conflict')).toBeVisible();
    expect(router.state.location.pathname).toBe('/revisions/r1');
  });
});

describe('a revision under review', () => {
  beforeEach(() => {
    current = {
      ...DRAFT,
      status: 'PendingReview',
      submissionVersion: 1,
      submittedAt: '2026-10-28T11:00:00Z',
      allowedActions: ['withdraw'],
    };
  });

  it('is read-only with a withdraw action', async () => {
    renderAt('/revisions/r1');

    expect(await screen.findByTestId('revision-readonly-notice')).toHaveTextContent(PENDING_NOTICE);
    expect(screen.queryByTestId('revision-title')).not.toBeInTheDocument();
    expect(screen.queryByTestId('revision-save')).not.toBeInTheDocument();
    expect(screen.queryByTestId('revision-submit')).not.toBeInTheDocument();
    expect(screen.getByTestId('revision-view-title')).toHaveTextContent('Revised title');
    expect(screen.getByTestId('revision-view-category')).toHaveTextContent('Science');
    expect(screen.getByTestId('revision-withdraw')).toBeVisible();
  });

  it('withdraws after confirmation and becomes an editable draft again', async () => {
    renderAt('/revisions/r1');

    await userEvent.click(await screen.findByTestId('revision-withdraw'));
    await userEvent.click(await screen.findByTestId('revision-withdraw-confirm'));

    expect(await screen.findByTestId('revision-title')).toBeVisible();
    expect(sentBody('POST', '/withdraw')).toEqual({ version: 7 });
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent('Revision draft');
  });

  it('explains a conflict when an administrator decided first', async () => {
    withdrawResponse = () => problem(409, 'Conflict');
    renderAt('/revisions/r1');

    await userEvent.click(await screen.findByTestId('revision-withdraw'));
    await userEvent.click(await screen.findByTestId('revision-withdraw-confirm'));

    expect(await screen.findByTestId('revision-conflict')).toHaveTextContent(CONFLICT_MESSAGE);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('a rejected revision', () => {
  beforeEach(() => {
    current = {
      ...DRAFT,
      status: 'Rejected',
      submissionVersion: 1,
      allowedActions: ['edit', 'delete', 'submit'],
      latestReview: {
        decision: 'Rejected',
        reason: 'Needs more sources.',
        decidedAt: '2026-10-28T12:00:00Z',
      },
      reviews: [
        {
          submissionVersion: 1,
          decision: 'Rejected',
          reason: 'Needs more sources.',
          decidedAt: '2026-10-28T12:00:00Z',
        },
      ],
    };
  });

  it('shows the reason, stays editable and offers to resubmit', async () => {
    renderAt('/revisions/r1');

    expect(await screen.findByTestId('revision-review-reason')).toHaveTextContent(
      'Needs more sources.',
    );
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent('Revision rejected');
    expect(screen.getByTestId('revision-title')).toBeEnabled();
    expect(screen.getByTestId('revision-submit')).toHaveTextContent('Resubmit for review');
  });

  it('lists every decision in the history', async () => {
    renderAt('/revisions/r1');

    const history = await screen.findByTestId('revision-history');
    expect(within(history).getByText('Submission 1: Rejected')).toBeVisible();
    expect(within(history).getByText('Needs more sources.')).toBeVisible();
  });
});

describe('an approved revision', () => {
  it('is final and read-only', async () => {
    current = { ...DRAFT, status: 'Approved', submissionVersion: 1, allowedActions: [] };
    renderAt('/revisions/r1');

    expect(await screen.findByTestId('revision-readonly-notice')).toHaveTextContent(
      APPROVED_NOTICE,
    );
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent('Revision approved');
    expect(screen.queryByTestId('revision-withdraw')).not.toBeInTheDocument();
    expect(screen.queryByTestId('revision-title')).not.toBeInTheDocument();
  });
});

describe('loading', () => {
  it('shows a loading state until the revision arrives', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    fetchMock.mockRestore();
    fetchMock = stubApi({
      'GET /api/auth/me': () => json(AUTHOR),
      'GET /api/categories': () => json(CATEGORIES),
      'GET /api/revisions/r1': async () => {
        await gate;
        return json(current);
      },
    });
    renderAt('/revisions/r1');

    expect(await screen.findByText('Loading the revision…')).toBeVisible();
    release();
    expect(await screen.findByTestId('revision-title')).toBeVisible();
  });

  it('explains a missing revision, which is also what somebody else’s revision looks like', async () => {
    getResponse = () => problem(404, 'Resource not found.');
    renderAt('/revisions/r1');

    expect(await screen.findByRole('heading', { name: 'Revision not found' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Go to My Articles' })).toBeVisible();
  });

  it('offers another try after a failure', async () => {
    getResponse = () => problem(500, 'Server error');
    renderAt('/revisions/r1');

    expect(await screen.findByText('The revision could not be loaded')).toBeVisible();
    getResponse = () => json(current);
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByTestId('revision-title')).toBeVisible();
  });
});

describe('safe rendering', () => {
  it('shows HTML in a read-only revision as literal text', async () => {
    current = {
      ...DRAFT,
      status: 'PendingReview',
      allowedActions: ['withdraw'],
      title: `Title ${SCRIPT}`,
      summary: `Summary ${SCRIPT}`,
      body: `Body ${SCRIPT}\n<b>bold?</b>`,
    };
    renderAt('/revisions/r1');

    expect((await screen.findByTestId('revision-view-body')).textContent).toBe(
      `Body ${SCRIPT}\n<b>bold?</b>`,
    );
    expect(screen.getByTestId('revision-view-summary').textContent).toBe(`Summary ${SCRIPT}`);
    expect(screen.getByTestId('revision-view-title').textContent).toBe(`Title ${SCRIPT}`);
    expect(document.querySelector('script')).toBeNull();
  });

  it('shows HTML in a rejection reason as literal text', async () => {
    current = {
      ...DRAFT,
      status: 'Rejected',
      latestReview: {
        decision: 'Rejected',
        reason: `Fix ${SCRIPT}`,
        decidedAt: '2026-10-28T12:00:00Z',
      },
    };
    renderAt('/revisions/r1');

    expect((await screen.findByTestId('revision-review-reason')).textContent).toBe(`Fix ${SCRIPT}`);
    expect(document.querySelector('script')).toBeNull();
  });
});
