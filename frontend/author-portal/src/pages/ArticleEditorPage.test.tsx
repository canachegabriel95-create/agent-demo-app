import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Article } from '../api/articles';
import { AUTHOR, json, problem, renderAt, storeSession, stubApi } from '../test/authHelpers';
import { CONFLICT_DIALOG_MESSAGE, LEAVE_MESSAGE } from './ArticleEditorPage';

const CATEGORIES = [
  { id: 'c1', name: 'Technology', slug: 'technology' },
  { id: 'c2', name: 'Science', slug: 'science' },
];

const DRAFT: Article = {
  id: 'a1',
  title: 'Stored title',
  summary: 'Stored summary',
  body: 'Stored body',
  category: { id: 'c2', name: 'Science' },
  status: 'Draft',
  submissionVersion: 0,
  createdAt: '2026-10-27T10:00:00Z',
  updatedAt: '2026-10-27T10:00:00Z',
  submittedAt: null,
  publishedAt: null,
  latestReview: null,
  allowedActions: ['edit', 'delete', 'submit'],
  version: 100,
  publishedVersion: 0,
  openRevision: null,
};

let current: Article;
let putResponse: () => Response;
let submitResponse: () => Response;
let fetchMock: ReturnType<typeof stubApi>;

function calls(method: string) {
  return fetchMock.mock.calls.filter(
    ([input, init]) =>
      new URL(String(input)).pathname.startsWith('/api/articles') && init?.method === method,
  );
}

function bodyOf(call: Parameters<typeof fetch>) {
  return JSON.parse(String(call[1]?.body)) as Record<string, unknown>;
}

beforeEach(() => {
  storeSession();
  current = DRAFT;
  putResponse = () => json({ ...current, version: current.version + 1 });
  submitResponse = () =>
    json({ ...current, status: 'PendingReview', allowedActions: ['withdraw'], version: 200 });
  fetchMock = stubApi({
    'GET /api/auth/me': () => json(AUTHOR),
    'GET /api/categories': () => json(CATEGORIES),
    'GET /api/articles/a1': () => json(current),
    'POST /api/articles': (init) => {
      const body = JSON.parse(String(init?.body)) as Record<string, string | null>;
      current = {
        ...DRAFT,
        title: String(body.title),
        summary: String(body.summary),
        body: String(body.body),
        category: CATEGORIES.find((c) => c.id === body.categoryId) ?? null,
      };
      return json(current, 201);
    },
    'PUT /api/articles/a1': () => putResponse(),
    'POST /api/articles/a1/submit': () => submitResponse(),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('new article', () => {
  it('shows the fields, the counters, the categories and the submission rules', async () => {
    renderAt('/articles/new');

    expect(await screen.findByRole('heading', { level: 1, name: 'New article' })).toBeVisible();
    expect(screen.getByTestId('article-title')).toBeVisible();
    expect(screen.getByTestId('article-summary')).toBeVisible();
    expect(screen.getByTestId('article-body')).toBeVisible();
    expect(screen.getByText('0 / 150 characters')).toBeVisible();
    expect(screen.getByText('0 / 500 characters')).toBeVisible();
    expect(screen.getByText('0 / 20000 characters')).toBeVisible();
    expect(screen.getByTestId('article-rules')).toHaveTextContent('summary');
    const select = screen.getByTestId('article-category');
    await waitFor(() => expect(select).toBeEnabled());
    expect(
      within(select)
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual(['— Select —', 'Technology', 'Science']);
  });

  it('counts characters as the author types', async () => {
    renderAt('/articles/new');
    await userEvent.type(await screen.findByTestId('article-title'), 'Hello');

    expect(screen.getByText('5 / 150 characters')).toBeVisible();
  });

  it('blocks the save and names the field when the title is missing', async () => {
    renderAt('/articles/new');
    await userEvent.type(await screen.findByTestId('article-summary'), 'Only a summary');

    await userEvent.click(screen.getByTestId('article-save'));

    expect(await screen.findByText('Title is required.')).toBeVisible();
    expect(screen.getByTestId('article-title')).toHaveAttribute('aria-invalid', 'true');
    expect(calls('POST')).toHaveLength(0);
  });

  it('refuses text over the limits without calling the API', async () => {
    renderAt('/articles/new');
    await userEvent.type(await screen.findByTestId('article-title'), 'T');
    await userEvent.click(screen.getByTestId('article-summary'));
    await userEvent.paste('s'.repeat(501));

    await userEvent.click(screen.getByTestId('article-save'));

    expect(await screen.findByText('Summary must be at most 500 characters.')).toBeVisible();
    expect(calls('POST')).toHaveLength(0);
  });

  it('creates the draft from a title alone and switches to the edit route', async () => {
    const router = renderAt('/articles/new');
    await userEvent.type(await screen.findByTestId('article-title'), 'My first draft');

    await userEvent.click(screen.getByTestId('article-save'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/articles/a1/edit'));
    expect(bodyOf(calls('POST')[0]!)).toEqual({
      title: 'My first draft',
      summary: '',
      body: '',
      categoryId: null,
    });
    expect(await screen.findByRole('heading', { level: 1, name: 'Edit article' })).toBeVisible();
    expect(screen.getByTestId('article-status')).toHaveTextContent(/Draft saved at \d\d:\d\d/);
    // No second request for the article that was just created.
    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/api/articles/a1'))).toBe(
      false,
    );
  });

  it('sends the chosen category', async () => {
    renderAt('/articles/new');
    await userEvent.type(await screen.findByTestId('article-title'), 'With category');
    const select = screen.getByTestId('article-category');
    await waitFor(() => expect(select).toBeEnabled());
    await userEvent.selectOptions(select, 'Technology');

    await userEvent.click(screen.getByTestId('article-save'));

    await waitFor(() => expect(calls('POST')).toHaveLength(1));
    expect(bodyOf(calls('POST')[0]!).categoryId).toBe('c1');
  });

  it('shows the API field errors when the server refuses the draft', async () => {
    fetchMock.mockImplementationOnce(async () => json({}));
    renderAt('/articles/new');
    await userEvent.type(await screen.findByTestId('article-title'), 'Title');
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        if (init?.method === 'POST') {
          return json(
            { title: 'Bad', status: 400, errors: { categoryId: ['Category does not exist.'] } },
            400,
            'application/problem+json',
          );
        }
        return fetchMock(input, init);
      }),
    );

    await userEvent.click(screen.getByTestId('article-save'));

    expect(await screen.findByText('Category does not exist.')).toBeVisible();
  });
});

describe('unsaved changes', () => {
  it('asks before leaving the page with unsaved text and stays when declined', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const router = renderAt('/articles/new');
    await userEvent.type(await screen.findByTestId('article-title'), 'Typed');

    await router.navigate('/profile');

    await waitFor(() => expect(confirm).toHaveBeenCalledWith(LEAVE_MESSAGE));
    expect(router.state.location.pathname).toBe('/articles/new');
  });

  it('leaves when the author confirms', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const router = renderAt('/articles/new');
    await userEvent.type(await screen.findByTestId('article-title'), 'Typed');

    await router.navigate('/profile');

    await waitFor(() => expect(router.state.location.pathname).toBe('/profile'));
  });

  it('does not ask when nothing changed', async () => {
    const confirm = vi.spyOn(window, 'confirm');
    const router = renderAt('/articles/new');
    await screen.findByTestId('article-title');

    await router.navigate('/profile');

    expect(confirm).not.toHaveBeenCalled();
    await waitFor(() => expect(router.state.location.pathname).toBe('/profile'));
  });
});

describe('editing an existing article', () => {
  it('loads the saved text and the category', async () => {
    renderAt('/articles/a1/edit');

    expect(await screen.findByTestId('article-title')).toHaveValue('Stored title');
    expect(screen.getByTestId('article-summary')).toHaveValue('Stored summary');
    expect(screen.getByTestId('article-body')).toHaveValue('Stored body');
    await waitFor(() => expect(screen.getByTestId('article-category')).toHaveValue('c2'));
  });

  it('saves with the version it loaded and shows when it was saved', async () => {
    renderAt('/articles/a1/edit');
    const title = await screen.findByTestId('article-title');
    await userEvent.clear(title);
    await userEvent.type(title, 'Edited');

    await userEvent.click(screen.getByTestId('article-save'));

    await waitFor(() => expect(calls('PUT')).toHaveLength(1));
    expect(bodyOf(calls('PUT')[0]!)).toMatchObject({ title: 'Edited', version: 100 });
    expect(await screen.findByText(/Draft saved at \d\d:\d\d/)).toBeVisible();
  });

  it('uses the new version for the next save', async () => {
    renderAt('/articles/a1/edit');
    const title = await screen.findByTestId('article-title');
    await userEvent.type(title, '!');
    await userEvent.click(screen.getByTestId('article-save'));
    await screen.findByText(/Draft saved at/);
    await userEvent.type(title, '?');

    await userEvent.click(screen.getByTestId('article-save'));

    await waitFor(() => expect(calls('PUT')).toHaveLength(2));
    expect(bodyOf(calls('PUT')[1]!).version).toBe(101);
  });

  it('can be reached for a rejected article', async () => {
    current = { ...DRAFT, status: 'Rejected', allowedActions: ['edit', 'submit'] };
    renderAt('/articles/a1/edit');

    expect(await screen.findByTestId('article-title')).toHaveValue('Stored title');
  });

  it.each([
    ['PendingReview', ['withdraw']],
    ['Published', []],
  ] as const)(
    'goes to the article page when a %s article cannot be edited',
    async (status, actions) => {
      current = { ...DRAFT, status, allowedActions: [...actions] };
      const router = renderAt('/articles/a1/edit');

      await waitFor(() => expect(router.state.location.pathname).toBe('/articles/a1'));
      expect(screen.queryByTestId('article-title')).not.toBeInTheDocument();
    },
  );

  it('explains a missing article and offers another try', async () => {
    fetchMock.mockRestore();
    stubApi({
      'GET /api/auth/me': () => json(AUTHOR),
      'GET /api/categories': () => json(CATEGORIES),
      'GET /api/articles/a1': () => problem(404, 'Resource not found.'),
    });
    renderAt('/articles/a1/edit');

    expect(await screen.findByText('The article could not be loaded')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });
});

describe('a conflicting save', () => {
  beforeEach(() => {
    putResponse = () =>
      json(
        {
          title: 'Conflict',
          status: 409,
          detail: 'The article was changed by someone else. Reload it and try again.',
          currentVersion: 105,
        },
        409,
        'application/problem+json',
      );
  });

  it('opens a dialog that keeps the unsaved text visible and copyable', async () => {
    renderAt('/articles/a1/edit');
    const title = await screen.findByTestId('article-title');
    await userEvent.clear(title);
    await userEvent.type(title, 'My unsaved title');

    await userEvent.click(screen.getByTestId('article-save'));

    const dialog = await screen.findByRole('dialog', { name: 'Article changed elsewhere' });
    expect(within(dialog).getByText(CONFLICT_DIALOG_MESSAGE)).toBeVisible();
    expect(within(dialog).getByTestId('article-conflict-text')).toHaveValue(
      'My unsaved title\n\nStored summary\n\nStored body',
    );
    // The form behind the dialog still holds the author's text and cannot be saved again.
    expect(title).toHaveValue('My unsaved title');
    expect(screen.getByTestId('article-save')).toBeDisabled();
  });

  it('reloads the latest version when asked', async () => {
    renderAt('/articles/a1/edit');
    const title = await screen.findByTestId('article-title');
    await userEvent.type(title, ' mine');
    await userEvent.click(screen.getByTestId('article-save'));
    await screen.findByRole('dialog');
    current = { ...DRAFT, title: 'Newer from elsewhere', version: 105 };

    await userEvent.click(screen.getByTestId('article-reload'));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(await screen.findByDisplayValue('Newer from elsewhere')).toBeVisible();
    expect(screen.getByTestId('article-save')).toBeEnabled();
  });

  it('sends the reloaded version on the next save', async () => {
    renderAt('/articles/a1/edit');
    await userEvent.type(await screen.findByTestId('article-title'), 'x');
    await userEvent.click(screen.getByTestId('article-save'));
    await screen.findByRole('dialog');
    current = { ...DRAFT, version: 105 };
    putResponse = () => json({ ...current, version: 106 });
    await userEvent.click(screen.getByTestId('article-reload'));
    await waitFor(() => expect(screen.getByTestId('article-save')).toBeEnabled());
    await userEvent.type(screen.getByTestId('article-title'), 'y');

    await userEvent.click(screen.getByTestId('article-save'));

    await waitFor(() => expect(calls('PUT')).toHaveLength(2));
    expect(bodyOf(calls('PUT')[1]!).version).toBe(105);
  });
});

describe('submitting from the editor', () => {
  it('has no submit button for an article that does not exist yet', async () => {
    renderAt('/articles/new');
    await screen.findByTestId('article-title');

    expect(screen.queryByTestId('article-submit')).not.toBeInTheDocument();
  });

  it('offers "Resubmit for review" for a rejected article', async () => {
    current = { ...DRAFT, status: 'Rejected', allowedActions: ['edit', 'submit'] };
    renderAt('/articles/a1/edit');

    expect(await screen.findByTestId('article-submit')).toHaveTextContent('Resubmit for review');
  });

  it('asks first and goes to the article page once it is submitted', async () => {
    const router = renderAt('/articles/a1/edit');
    await userEvent.click(await screen.findByTestId('article-submit'));

    const dialog = await screen.findByRole('dialog', { name: 'Submit for review' });
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    expect(calls('POST')).toHaveLength(0);
    await userEvent.click(within(dialog).getByTestId('article-submit-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/articles/a1'));
    expect(calls('POST')).toHaveLength(1);
    expect(calls('PUT')).toHaveLength(0);
    expect(bodyOf(calls('POST')[0]!)).toEqual({ version: 100 });
  });

  it('saves unsaved changes first and submits the version that save returned', async () => {
    const router = renderAt('/articles/a1/edit');
    const title = await screen.findByTestId('article-title');
    await userEvent.type(title, ' changed');
    await userEvent.click(screen.getByTestId('article-submit'));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/Your unsaved changes are saved first/)).toBeVisible();

    await userEvent.click(within(dialog).getByTestId('article-submit-confirm'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/articles/a1'));
    expect(bodyOf(calls('PUT')[0]!)).toMatchObject({ title: 'Stored title changed', version: 100 });
    expect(bodyOf(calls('POST')[0]!)).toEqual({ version: 101 });
  });

  it('does not submit when the unsaved text breaks a draft rule', async () => {
    renderAt('/articles/a1/edit');
    const title = await screen.findByTestId('article-title');
    await userEvent.clear(title);
    await userEvent.click(screen.getByTestId('article-submit'));

    await userEvent.click(await screen.findByTestId('article-submit-confirm'));

    expect(await screen.findByText('Title is required.')).toBeVisible();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(calls('PUT')).toHaveLength(0);
    expect(calls('POST')).toHaveLength(0);
  });

  it('shows the submission rules the API reports on the fields', async () => {
    submitResponse = () =>
      json(
        {
          title: 'Validation',
          status: 400,
          errors: {
            summary: ['Summary must be between 20 and 500 characters.'],
            categoryId: ['Category is required.'],
          },
        },
        400,
        'application/problem+json',
      );
    const router = renderAt('/articles/a1/edit');
    await userEvent.click(await screen.findByTestId('article-submit'));

    await userEvent.click(await screen.findByTestId('article-submit-confirm'));

    expect(await screen.findByText('Summary must be between 20 and 500 characters.')).toBeVisible();
    expect(screen.getByText('Category is required.')).toBeVisible();
    expect(screen.getByTestId('article-summary')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/articles/a1/edit');
  });

  it('opens the conflict dialog when the article changed status meanwhile', async () => {
    submitResponse = () => problem(409, 'Conflict');
    renderAt('/articles/a1/edit');
    await userEvent.click(await screen.findByTestId('article-submit'));

    await userEvent.click(await screen.findByTestId('article-submit-confirm'));

    expect(await screen.findByRole('dialog', { name: 'Article changed elsewhere' })).toBeVisible();
  });
});
