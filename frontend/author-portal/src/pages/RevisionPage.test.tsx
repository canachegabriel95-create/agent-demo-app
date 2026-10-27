import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Article } from '../api/articles';
import type { Revision, RevisionReviewEntry } from '../api/revisions';
import { AUTHOR, json, problem, renderAt, storeSession, stubApi } from '../test/authHelpers';
import { REVISION_CONFLICT_MESSAGE, REVISION_INTRO, REVISION_PENDING_NOTICE } from './RevisionPage';
import { REVISE_CONFLICT_MESSAGE } from './ArticlePage';

const SCRIPT = '<script>alert(1)</script>';

const CATEGORIES = [
  { id: 'c1', name: 'Technology', slug: 'technology' },
  { id: 'c2', name: 'Science', slug: 'science' },
];

const PUBLISHED: Article = {
  id: 'a1',
  title: 'Published title',
  summary: 'The published summary of the article.',
  body: 'The published body',
  category: { id: 'c2', name: 'Science' },
  status: 'Published',
  submissionVersion: 1,
  createdAt: '2026-10-20T10:00:00Z',
  updatedAt: '2026-10-21T11:00:00Z',
  submittedAt: '2026-10-20T10:30:00Z',
  publishedAt: '2026-10-21T11:00:00Z',
  latestReview: { decision: 'Approved', reason: null, decidedAt: '2026-10-21T11:00:00Z' },
  allowedActions: ['revise'],
  version: 100,
  publishedVersion: 1,
  openRevision: null,
};

const DRAFT_REVISION: Revision = {
  id: 'r1',
  articleId: 'a1',
  number: 1,
  title: 'Published title',
  summary: 'The published summary of the article.',
  body: 'The published body',
  category: { id: 'c2', name: 'Science' },
  status: 'Draft',
  submissionVersion: 0,
  createdAt: '2026-10-22T10:00:00Z',
  updatedAt: '2026-10-22T10:00:00Z',
  submittedAt: null,
  latestReview: null,
  allowedActions: ['edit', 'submit'],
  version: 500,
};

let article: Article;
let revision: Revision;
let getRevision: () => Response;
let putRevision: (body: Record<string, unknown>) => Response;
let submitRevision: () => Response;
let withdrawRevision: () => Response;
let createRevision: () => Response;
let history: RevisionReviewEntry[];
let fetchMock: ReturnType<typeof stubApi>;

function calls(method: string, suffix = '') {
  return fetchMock.mock.calls.filter(([input, init]) => {
    const path = new URL(String(input)).pathname;
    return init?.method === method && path.startsWith('/api/articles/a1/revision' + suffix);
  });
}

function bodyOf(call: Parameters<typeof fetch>) {
  return JSON.parse(String(call[1]?.body)) as Record<string, unknown>;
}

beforeEach(() => {
  storeSession();
  article = PUBLISHED;
  revision = DRAFT_REVISION;
  history = [];
  getRevision = () => json(revision);
  putRevision = (body) =>
    json({
      ...revision,
      title: String(body.title),
      summary: String(body.summary),
      body: String(body.body),
      version: revision.version + 1,
    });
  submitRevision = () =>
    json({
      ...revision,
      status: 'PendingReview',
      submissionVersion: 1,
      submittedAt: '2026-10-22T12:00:00Z',
      allowedActions: ['withdraw'],
      version: revision.version + 1,
    });
  withdrawRevision = () =>
    json({ ...revision, status: 'Draft', allowedActions: ['edit', 'submit'], version: 700 });
  createRevision = () => json(DRAFT_REVISION, 201);
  fetchMock = stubApi({
    'GET /api/auth/me': () => json(AUTHOR),
    'GET /api/categories': () => json(CATEGORIES),
    'GET /api/articles/a1': () => json(article),
    'GET /api/articles/a1/reviews': () => json([]),
    'GET /api/articles/a1/revision': () => getRevision(),
    'GET /api/articles/a1/revision/reviews': () => json(history),
    'POST /api/articles/a1/revision': () => createRevision(),
    'PUT /api/articles/a1/revision': (init) =>
      putRevision(JSON.parse(String(init?.body)) as Record<string, unknown>),
    'POST /api/articles/a1/revision/submit': () => submitRevision(),
    'POST /api/articles/a1/revision/withdraw': () => withdrawRevision(),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('starting a revision from the published article', () => {
  it('offers "Edit published article" and opens the revision after creating it', async () => {
    const router = renderAt('/articles/a1');

    await userEvent.click(await screen.findByTestId('article-revise'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/articles/a1/revision'));
    expect(calls('POST')).toHaveLength(1);
    expect(await screen.findByTestId('revision-title')).toHaveTextContent('Revision 1');
  });

  it('does not offer it while a revision is open and links to the revision instead', async () => {
    article = {
      ...PUBLISHED,
      allowedActions: [],
      openRevision: {
        id: 'r1',
        number: 1,
        status: 'PendingReview',
        updatedAt: '2026-10-22T10:00:00Z',
      },
    };
    renderAt('/articles/a1');

    expect(await screen.findByTestId('article-revision-status')).toHaveTextContent(
      'Revision 1: Revision pending review',
    );
    expect(screen.queryByTestId('article-revise')).not.toBeInTheDocument();
    expect(screen.getByTestId('article-open-revision')).toHaveAttribute(
      'href',
      '/articles/a1/revision',
    );
  });

  it('does not offer it for a draft article', async () => {
    article = { ...PUBLISHED, status: 'Draft', allowedActions: ['edit', 'delete', 'submit'] };
    renderAt('/articles/a1');

    await screen.findByTestId('article-status-badge');
    expect(screen.queryByTestId('article-revise')).not.toBeInTheDocument();
  });

  it('explains a 409 and reloads the article', async () => {
    createRevision = () => problem(409, 'Conflict');
    renderAt('/articles/a1');

    await userEvent.click(await screen.findByTestId('article-revise'));

    expect(await screen.findByTestId('article-notice')).toHaveTextContent(REVISE_CONFLICT_MESSAGE);
  });
});

describe('editing a revision', () => {
  it('shows the revision content, the intro that readers keep the published version, and the fields', async () => {
    renderAt('/articles/a1/revision');

    expect(await screen.findByTestId('revision-title')).toHaveTextContent('Revision 1');
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent('Draft revision');
    expect(screen.getByTestId('revision-intro')).toHaveTextContent(REVISION_INTRO);
    expect(screen.getByTestId('revision-title-input')).toHaveValue('Published title');
    expect(screen.getByTestId('revision-body-input')).toHaveValue('The published body');
    await waitFor(() => expect(screen.getByTestId('revision-category-input')).toHaveValue('c2'));
    expect(screen.getByTestId('revision-back')).toHaveAttribute('href', '/articles/a1');
  });

  it('saves with the version and shows that it was saved', async () => {
    renderAt('/articles/a1/revision');
    const input = await screen.findByTestId('revision-title-input');
    await userEvent.clear(input);
    await userEvent.type(input, 'A new title');

    await userEvent.click(screen.getByTestId('revision-save'));

    await waitFor(() => expect(calls('PUT')).toHaveLength(1));
    expect(bodyOf(calls('PUT')[0]!)).toMatchObject({ title: 'A new title', version: 500 });
    expect(await screen.findByTestId('revision-flash')).toHaveTextContent('Revision saved.');
  });

  it('blocks the save and names the field when the title is empty', async () => {
    renderAt('/articles/a1/revision');
    await userEvent.clear(await screen.findByTestId('revision-title-input'));

    await userEvent.click(screen.getByTestId('revision-save'));

    expect(await screen.findByText('Title is required.')).toBeVisible();
    expect(calls('PUT')).toHaveLength(0);
  });

  it('shows the API field errors from a submission', async () => {
    submitRevision = () =>
      json(
        {
          title: 'Validation',
          status: 400,
          errors: { summary: ['Summary must be between 20 and 500 characters.'] },
        },
        400,
        'application/problem+json',
      );
    renderAt('/articles/a1/revision');
    await screen.findByTestId('revision-title-input');

    await userEvent.click(screen.getByTestId('revision-submit'));
    await userEvent.click(await screen.findByTestId('revision-submit-confirm'));

    expect(await screen.findByText('Summary must be between 20 and 500 characters.')).toBeVisible();
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent('Draft revision');
  });

  it('shows a conflict dialog with the unsaved text when the revision changed elsewhere', async () => {
    putRevision = () => problem(409, 'Conflict');
    renderAt('/articles/a1/revision');
    const input = await screen.findByTestId('revision-title-input');
    await userEvent.type(input, ' edited');

    await userEvent.click(screen.getByTestId('revision-save'));

    expect(await screen.findByTestId('revision-conflict')).toHaveTextContent(
      REVISION_CONFLICT_MESSAGE,
    );
    expect((screen.getByTestId('revision-conflict-text') as HTMLTextAreaElement).value).toContain(
      'Published title edited',
    );
    expect(screen.getByTestId('revision-save')).toBeDisabled();

    await userEvent.click(screen.getByTestId('revision-reload'));
    await waitFor(() => expect(screen.queryByTestId('revision-conflict')).not.toBeInTheDocument());
  });

  it('shows the user text as text, never as markup', async () => {
    revision = {
      ...DRAFT_REVISION,
      status: 'PendingReview',
      title: SCRIPT,
      summary: SCRIPT,
      body: SCRIPT,
      allowedActions: ['withdraw'],
    };
    const { container } = { container: document.body };
    renderAt('/articles/a1/revision');

    expect(await screen.findByTestId('revision-view-body')).toHaveTextContent(SCRIPT);
    expect(container.querySelector('script')).toBeNull();
  });
});

describe('submitting and withdrawing', () => {
  it('saves unsaved changes first, then submits and shows the pending state', async () => {
    renderAt('/articles/a1/revision');
    const input = await screen.findByTestId('revision-title-input');
    await userEvent.type(input, ' v2');

    await userEvent.click(screen.getByTestId('revision-submit'));
    await userEvent.click(await screen.findByTestId('revision-submit-confirm'));

    await waitFor(() => expect(calls('POST', '/submit')).toHaveLength(1));
    expect(calls('PUT')).toHaveLength(1);
    expect(bodyOf(calls('POST', '/submit')[0]!)).toEqual({ version: 501 });
    expect(await screen.findByTestId('revision-readonly-notice')).toHaveTextContent(
      REVISION_PENDING_NOTICE,
    );
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent(
      'Revision pending review',
    );
    expect(screen.queryByTestId('revision-save')).not.toBeInTheDocument();
  });

  it('withdraws a pending revision back to a draft', async () => {
    revision = {
      ...DRAFT_REVISION,
      status: 'PendingReview',
      submissionVersion: 1,
      allowedActions: ['withdraw'],
    };
    renderAt('/articles/a1/revision');

    await userEvent.click(await screen.findByTestId('revision-withdraw'));
    await userEvent.click(await screen.findByTestId('revision-withdraw-confirm'));

    expect(await screen.findByTestId('revision-save')).toBeVisible();
    expect(bodyOf(calls('POST', '/withdraw')[0]!)).toEqual({ version: 500 });
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent('Draft revision');
  });

  it('tells the author when an administrator decided first', async () => {
    revision = {
      ...DRAFT_REVISION,
      status: 'PendingReview',
      submissionVersion: 1,
      allowedActions: ['withdraw'],
    };
    withdrawRevision = () => problem(409, 'Conflict');
    renderAt('/articles/a1/revision');

    await userEvent.click(await screen.findByTestId('revision-withdraw'));
    await userEvent.click(await screen.findByTestId('revision-withdraw-confirm'));

    expect(await screen.findByTestId('revision-conflict')).toBeVisible();
  });
});

describe('after a rejection', () => {
  const REJECTED: Revision = {
    ...DRAFT_REVISION,
    status: 'Rejected',
    submissionVersion: 1,
    latestReview: {
      decision: 'Rejected',
      reason: 'The new introduction is not accurate.',
      decidedAt: '2026-10-23T09:00:00Z',
    },
    allowedActions: ['edit', 'submit'],
  };

  it('shows the reason, keeps the revision editable and offers to resubmit', async () => {
    revision = REJECTED;
    history = [
      {
        revisionNumber: 1,
        submissionVersion: 1,
        decision: 'Rejected',
        reason: 'The new introduction is not accurate.',
        decidedAt: '2026-10-23T09:00:00Z',
      },
    ];
    renderAt('/articles/a1/revision');

    expect(await screen.findByTestId('revision-review-reason')).toHaveTextContent(
      'The new introduction is not accurate.',
    );
    expect(screen.getByTestId('revision-status-badge')).toHaveTextContent('Revision rejected');
    expect(screen.getByTestId('revision-submit')).toHaveTextContent('Resubmit for review');
    expect(screen.getByTestId('revision-title-input')).toBeEnabled();
    const entry = await within(screen.getByTestId('revision-history')).findByRole('listitem');
    expect(entry).toHaveTextContent('Revision 1, submission 1: Rejected');
  });
});

describe('loading, empty and error states', () => {
  it('shows a loading state first', async () => {
    fetchMock.mockImplementation(async (input) => {
      const path = new URL(String(input)).pathname;
      if (path === '/api/auth/me') {
        return json(AUTHOR);
      }

      return new Promise<Response>(() => undefined);
    });
    renderAt('/articles/a1/revision');

    expect(await screen.findByText('Loading the revision…')).toBeVisible();
  });

  it('explains that the article has no revision in progress', async () => {
    getRevision = () => problem(404, 'Resource not found.');
    renderAt('/articles/a1/revision');

    expect(await screen.findByText('This article has no revision in progress.')).toBeVisible();
    expect(screen.getByRole('link', { name: 'Back to the article' })).toHaveAttribute(
      'href',
      '/articles/a1',
    );
  });

  it('shows an error with a retry', async () => {
    getRevision = () => problem(500, 'An unexpected error occurred.');
    renderAt('/articles/a1/revision');

    expect(await screen.findByText('The revision could not be loaded')).toBeVisible();
    getRevision = () => json(revision);
    await userEvent.click(screen.getByRole('button', { name: /try again|retry/i }));
    expect(await screen.findByTestId('revision-title')).toBeVisible();
  });
});
