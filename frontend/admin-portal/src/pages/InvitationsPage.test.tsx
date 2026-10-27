import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Invitation } from '../api/invitations';
import { ADMIN, json, renderAt, storeSession, stubApi, type Handler } from '../test/authHelpers';
import { CONFLICT_MESSAGE, NO_MATCHES_MESSAGE } from './InvitationsPage';

function invitation(overrides: Partial<Invitation> & Pick<Invitation, 'id'>): Invitation {
  return {
    code: 'ABCD-EFGH-JKMN',
    email: 'ann@example.com',
    status: 'Available',
    createdAt: '2026-10-20T09:00:00Z',
    createdBy: { id: ADMIN.id, fullName: 'Platform Admin' },
    expiresAt: '2026-10-27T09:00:00Z',
    revokedAt: null,
    revokedBy: null,
    usedAt: null,
    usedBy: null,
    version: 100,
    ...overrides,
  };
}

const AVAILABLE = invitation({ id: 'inv-available', email: 'ann@example.com' });
const USED = invitation({
  id: 'inv-used',
  email: 'bob@example.com',
  code: 'USED-USED-USED',
  status: 'Used',
  usedAt: '2026-10-21T10:00:00Z',
  usedBy: { id: 'u1', fullName: 'Bob Author' },
});
const EXPIRED = invitation({
  id: 'inv-expired',
  email: 'cy@example.com',
  code: 'EXPI-REDX-0000',
  status: 'Expired',
});
const REVOKED = invitation({
  id: 'inv-revoked',
  email: 'di@example.com',
  code: 'REVO-KEDX-1111',
  status: 'Revoked',
  revokedAt: '2026-10-22T11:00:00Z',
  revokedBy: { id: ADMIN.id, fullName: 'Platform Admin' },
});

function pageOf(
  items: Invitation[],
  extra: Partial<{ page: number; pageSize: number; totalCount: number }> = {},
) {
  return { items, page: 1, pageSize: 20, totalCount: items.length, ...extra };
}

let list: Handler;
let create: Handler;
let revoke: Handler;
let fetchMock: ReturnType<typeof stubApi>;

function listCalls(): URL[] {
  return fetchMock.mock.calls
    .filter(
      ([input, init]) =>
        new URL(String(input)).pathname === '/api/admin/invitations' &&
        (init?.method ?? 'GET') === 'GET',
    )
    .map(([input]) => new URL(String(input)));
}

beforeEach(() => {
  storeSession();
  list = () => json(pageOf([AVAILABLE, USED, EXPIRED, REVOKED]));
  create = () => {
    throw new Error('unexpected create');
  };
  revoke = () => {
    throw new Error('unexpected revoke');
  };
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /api/admin/invitations': (init, url) => list(init, url),
    'POST /api/admin/invitations': (init, url) => create(init, url),
    'POST /api/admin/invitations/inv-available/revoke': (init, url) => revoke(init, url),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openPage(path = '/invitations') {
  const router = renderAt(path);
  await screen.findByRole('heading', { level: 1, name: 'Invitations' });
  return router;
}

describe('listing', () => {
  it('shows a table with code, email, status, creator, expiry and usage or revocation', async () => {
    await openPage();

    const table = await screen.findByRole('table');
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((header) => header.textContent);
    expect(headers).toEqual([
      'Code',
      'Email',
      'Status',
      'Created',
      'Expires',
      'Used / Revoked',
      'Actions',
    ]);

    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(4);
    const available = within(rows[0]!);
    expect(available.getByText('ABCD-EFGH-JKMN')).toBeInTheDocument();
    expect(available.getByText('ann@example.com')).toBeInTheDocument();
    expect(available.getByText('Available')).toBeInTheDocument();
    expect(available.getByText(/by Platform Admin/)).toBeInTheDocument();
    expect(within(rows[1]!).getByText('Used')).toBeInTheDocument();
    expect(within(rows[1]!).getByText(/by Bob Author/)).toBeInTheDocument();
    expect(within(rows[2]!).getByText('Expired')).toBeInTheDocument();
    expect(within(rows[3]!).getByText('Revoked')).toBeInTheDocument();
    expect(within(rows[3]!).getAllByText(/by Platform Admin/)).toHaveLength(2); // created by and revoked by
  });

  it('asks for the unfiltered first page with the session token', async () => {
    await openPage();
    await screen.findByRole('table');

    expect(listCalls()[0]!.search).toBe('');
    const headers = new Headers(
      fetchMock.mock.calls.find(([input]) => String(input).includes('/api/admin/invitations'))![1]!
        .headers,
    );
    expect(headers.get('Authorization')).toBe('Bearer valid-token');
  });

  it('shows a loading state, then the table', async () => {
    let release!: () => void;
    list = () =>
      new Promise((resolve) => {
        release = () => resolve(json(pageOf([AVAILABLE])));
      });
    await openPage();

    expect(await screen.findByText('Loading invitations…')).toBeInTheDocument();
    release();

    expect(await screen.findByRole('table')).toBeInTheDocument();
    expect(screen.queryByText('Loading invitations…')).not.toBeInTheDocument();
  });

  it('shows an empty state when nothing matches', async () => {
    list = () => json(pageOf([]));
    await openPage('/invitations?status=Used');

    expect(await screen.findByText(NO_MATCHES_MESSAGE)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('shows the error with its reference and retries on request', async () => {
    const user = userEvent.setup();
    let calls = 0;
    list = () => {
      calls += 1;
      return calls === 1
        ? json(
            { title: 'An unexpected error occurred.', status: 500, correlationId: 'corr-123' },
            500,
            'application/problem+json',
          )
        : json(pageOf([AVAILABLE]));
    };
    await openPage();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('An unexpected error occurred.');
    expect(alert).toHaveTextContent('corr-123');

    await user.click(within(alert).getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('table')).toBeInTheDocument();
    expect(calls).toBe(2);
  });

  it('pages through the results and keeps the page in the URL', async () => {
    const user = userEvent.setup();
    list = (_init, url) => {
      const page = Number(new URL(url).searchParams.get('page') ?? '1');
      return json(pageOf([page === 1 ? AVAILABLE : USED], { page, totalCount: 40 }));
    };
    const router = await openPage();
    await screen.findByText('ann@example.com');

    await user.click(screen.getByRole('button', { name: 'Next page' }));

    expect(await screen.findByText('bob@example.com')).toBeInTheDocument();
    expect(router.state.location.search).toBe('?page=2');
    expect(listCalls().at(-1)!.searchParams.get('page')).toBe('2');
  });
});

describe('filters and deep links', () => {
  it('pre-applies status, email and page from the URL', async () => {
    await openPage('/invitations?status=Used&email=ann&page=2');
    await screen.findByRole('table');

    expect(screen.getByLabelText('Status')).toHaveValue('Used');
    expect(screen.getByLabelText('Email')).toHaveValue('ann');
    const url = listCalls()[0]!;
    expect(url.searchParams.get('status')).toBe('Used');
    expect(url.searchParams.get('email')).toBe('ann');
    expect(url.searchParams.get('page')).toBe('2');
  });

  it('ignores a status it does not know', async () => {
    await openPage('/invitations?status=Bogus');
    await screen.findByRole('table');

    expect(screen.getByLabelText('Status')).toHaveValue('');
    expect(listCalls()[0]!.searchParams.has('status')).toBe(false);
  });

  it('offers All and the four statuses', async () => {
    await openPage();

    const options = within(screen.getByLabelText('Status'))
      .getAllByRole('option')
      .map((o) => o.textContent);
    expect(options).toEqual(['All', 'Available', 'Used', 'Expired', 'Revoked']);
  });

  it('changing the status updates the URL, resets the page and requests the new list', async () => {
    const user = userEvent.setup();
    const router = await openPage('/invitations?page=3');
    await screen.findByRole('table');

    await user.selectOptions(screen.getByLabelText('Status'), 'Available');

    await waitFor(() => expect(listCalls().at(-1)!.searchParams.get('status')).toBe('Available'));
    expect(router.state.location.search).toBe('?status=Available');
    expect(listCalls().at(-1)!.searchParams.has('page')).toBe(false);
  });

  it('waits for typing to pause before requesting the email filter', async () => {
    const user = userEvent.setup();
    const router = await openPage();
    await screen.findByRole('table');
    const before = listCalls().length;

    await user.type(screen.getByLabelText('Email'), 'ann');

    // Nothing yet: the debounce has not elapsed.
    expect(listCalls()).toHaveLength(before);
    await waitFor(() => expect(listCalls().at(-1)!.searchParams.get('email')).toBe('ann'), {
      timeout: 2000,
    });
    expect(listCalls()).toHaveLength(before + 1); // one request for the whole word, not one per letter
    expect(router.state.location.search).toBe('?email=ann');
  });

  it('clearing the email filter removes it from the URL', async () => {
    const user = userEvent.setup();
    const router = await openPage('/invitations?email=ann');
    await screen.findByRole('table');

    await user.clear(screen.getByLabelText('Email'));

    await waitFor(() => expect(router.state.location.search).toBe(''), { timeout: 2000 });
    await waitFor(() => expect(listCalls().at(-1)!.searchParams.has('email')).toBe(false));
  });

  it('follows the address bar when it changes', async () => {
    const router = await openPage('/invitations?email=ann');
    await screen.findByRole('table');

    await act(async () => {
      await router.navigate('/invitations?email=zed&status=Revoked');
    });

    await waitFor(() => expect(screen.getByLabelText('Email')).toHaveValue('zed'));
    expect(screen.getByLabelText('Status')).toHaveValue('Revoked');
  });
});

describe('navigation', () => {
  it('has an Invitations link in the main navigation', async () => {
    const user = userEvent.setup();
    const router = renderAt('/');
    const link = await screen.findByRole('link', { name: 'Invitations' });

    await user.click(link);

    expect(router.state.location.pathname).toBe('/invitations');
    expect(
      await screen.findByRole('heading', { level: 1, name: 'Invitations' }),
    ).toBeInTheDocument();
  });
});

describe('copy', () => {
  it('copies the code of a row and announces it', async () => {
    const user = userEvent.setup();
    await openPage();
    await screen.findByRole('table');

    await user.click(screen.getByTestId('invitation-copy-inv-available'));

    expect(await navigator.clipboard.readText()).toBe('ABCD-EFGH-JKMN');
    expect(await screen.findByText('Copied')).toBeInTheDocument();
    expect(screen.getByText('Copied')).toHaveAttribute('aria-live', 'polite');
  });

  it('says so when the clipboard is not available', async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    await openPage();
    await screen.findByRole('table');

    await user.click(screen.getByTestId('invitation-copy-inv-available'));

    expect(await screen.findByText(/Copy failed/)).toBeInTheDocument();
    expect(screen.queryByText('Copied')).not.toBeInTheDocument();
  });
});

describe('revoking', () => {
  it('offers Revoke only for Available invitations', async () => {
    await openPage();
    await screen.findByRole('table');

    const buttons = screen.getAllByRole('button', { name: /^Revoke invitation for/ });
    expect(buttons).toHaveLength(1);
    expect(buttons[0]).toHaveAccessibleName('Revoke invitation for ann@example.com');
  });

  it('asks for confirmation, and Cancel changes nothing', async () => {
    const user = userEvent.setup();
    await openPage();
    await screen.findByRole('table');

    await user.click(screen.getByTestId('invitation-revoke-inv-available'));
    const dialog = screen.getByRole('dialog', { name: 'Revoke invitation' });
    expect(dialog).toHaveTextContent('ann@example.com');
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
  });

  it('revokes with the loaded version, then reloads and confirms', async () => {
    const user = userEvent.setup();
    let body: unknown;
    revoke = (init) => {
      body = JSON.parse(String(init?.body));
      return json({ ...AVAILABLE, status: 'Revoked', version: 101 });
    };
    await openPage();
    await screen.findByRole('table');
    const loads = listCalls().length;

    await user.click(screen.getByTestId('invitation-revoke-inv-available'));
    await user.click(screen.getByTestId('invitation-revoke-confirm'));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(body).toEqual({ version: 100 });
    expect(await screen.findByTestId('invitations-notice')).toHaveTextContent(
      'The invitation for ann@example.com was revoked.',
    );
    await waitFor(() => expect(listCalls()).toHaveLength(loads + 1));
  });

  it('reports a conflict, reloads and keeps the page usable', async () => {
    const user = userEvent.setup();
    revoke = () => json({ title: 'conflict', status: 409 }, 409, 'application/problem+json');
    await openPage();
    await screen.findByRole('table');
    const loads = listCalls().length;

    await user.click(screen.getByTestId('invitation-revoke-inv-available'));
    await user.click(screen.getByTestId('invitation-revoke-confirm'));

    expect(await screen.findByRole('alert')).toHaveTextContent(CONFLICT_MESSAGE);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await waitFor(() => expect(listCalls()).toHaveLength(loads + 1));
  });

  it('keeps the dialog open and explains any other failure', async () => {
    const user = userEvent.setup();
    revoke = () =>
      json(
        { title: 'An unexpected error occurred.', status: 500, correlationId: 'c-9' },
        500,
        'application/problem+json',
      );
    await openPage();
    await screen.findByRole('table');

    await user.click(screen.getByTestId('invitation-revoke-inv-available'));
    await user.click(screen.getByTestId('invitation-revoke-confirm'));

    const dialog = screen.getByRole('dialog');
    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'An unexpected error occurred.',
    );
    expect(dialog).toHaveTextContent('c-9');
    expect(within(dialog).getByTestId('invitation-revoke-confirm')).toBeEnabled();
  });
});

describe('creating', () => {
  async function openCreateDialog() {
    const user = userEvent.setup();
    await openPage();
    await screen.findByRole('table');
    await user.click(screen.getByTestId('invitation-create-open'));
    return { user, dialog: screen.getByRole('dialog', { name: 'New invitation' }) };
  }

  it('opens a labelled modal dialog with the focus on the email field and 7 days preset', async () => {
    const { dialog } = await openCreateDialog();

    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(within(dialog).getByLabelText(/Email/)).toHaveFocus();
    expect(within(dialog).getByLabelText(/Valid for \(days\)/)).toHaveValue(7);
  });

  it('keeps Tab inside the dialog and closes it with Escape, returning the focus', async () => {
    const { user, dialog } = await openCreateDialog();
    const open = screen.getByTestId('invitation-create-open');
    const submit = within(dialog).getByTestId('invitation-create-submit');

    submit.focus();
    await user.tab();
    expect(within(dialog).getByLabelText(/Email/)).toHaveFocus();
    await user.tab({ shift: true });
    expect(submit).toHaveFocus();

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(open).toHaveFocus();
  });

  it('validates before sending', async () => {
    const { user, dialog } = await openCreateDialog();

    await user.click(within(dialog).getByTestId('invitation-create-submit'));
    expect(within(dialog).getByText('Email is required.')).toBeInTheDocument();

    await user.type(within(dialog).getByLabelText(/Email/), 'not-an-email');
    await user.clear(within(dialog).getByLabelText(/Valid for/));
    await user.type(within(dialog).getByLabelText(/Valid for/), '45');
    await user.click(within(dialog).getByTestId('invitation-create-submit'));

    expect(within(dialog).getByText('Enter a valid email address.')).toBeInTheDocument();
    expect(
      within(dialog).getByText('Enter a whole number of days between 1 and 30.'),
    ).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/Email/)).toBeInvalid();
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
  });

  it('shows the server field errors next to the fields', async () => {
    create = () =>
      json(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: {
            email: ['Enter a valid email address of at most 254 characters.'],
            validForDays: ['Validity must be between 1 and 30 days.'],
          },
        },
        400,
        'application/problem+json',
      );
    const { user, dialog } = await openCreateDialog();

    await user.type(within(dialog).getByLabelText(/Email/), 'ann@example.com');
    await user.click(within(dialog).getByTestId('invitation-create-submit'));

    expect(
      await within(dialog).findByText('Enter a valid email address of at most 254 characters.'),
    ).toBeInTheDocument();
    expect(within(dialog).getByText('Validity must be between 1 and 30 days.')).toBeInTheDocument();
  });

  it('shows "a user with this email already exists" at the email field', async () => {
    create = () =>
      json(
        {
          title: 'The request conflicts with the current state of the resource.',
          detail: 'A user with this email already exists.',
          status: 409,
        },
        409,
        'application/problem+json',
      );
    const { user, dialog } = await openCreateDialog();

    await user.type(within(dialog).getByLabelText(/Email/), 'ann@example.com');
    await user.click(within(dialog).getByTestId('invitation-create-submit'));

    expect(
      await within(dialog).findByText('A user with this email already exists.'),
    ).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/Email/)).toBeInvalid();
  });

  it('shows the new code prominently with a Copy button, reloads the list, and Done closes', async () => {
    let body: unknown;
    create = (init) => {
      body = JSON.parse(String(init?.body));
      return json(
        invitation({ id: 'inv-new', email: 'new@example.com', code: 'NEW0-CODE-2222' }),
        201,
      );
    };
    const { user, dialog } = await openCreateDialog();
    const loads = listCalls().length;

    await user.type(within(dialog).getByLabelText(/Email/), '  new@example.com ');
    await user.clear(within(dialog).getByLabelText(/Valid for/));
    await user.type(within(dialog).getByLabelText(/Valid for/), '14');
    await user.click(within(dialog).getByTestId('invitation-create-submit'));

    expect(await within(dialog).findByTestId('invitation-created-code')).toHaveTextContent(
      'NEW0-CODE-2222',
    );
    expect(body).toEqual({ email: 'new@example.com', validForDays: 14 });
    const copy = within(dialog).getByTestId('invitation-created-copy');
    expect(copy).toHaveFocus();
    await waitFor(() => expect(listCalls()).toHaveLength(loads + 1));

    await user.click(copy);
    expect(await navigator.clipboard.readText()).toBe('NEW0-CODE-2222');
    expect(await within(dialog).findByText('Copied')).toBeInTheDocument();

    await user.click(within(dialog).getByTestId('invitation-create-done'));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('explains an unexpected failure without losing what was typed', async () => {
    create = () =>
      json(
        { title: 'An unexpected error occurred.', status: 500, correlationId: 'c-1' },
        500,
        'application/problem+json',
      );
    const { user, dialog } = await openCreateDialog();

    await user.type(within(dialog).getByLabelText(/Email/), 'ann@example.com');
    await user.click(within(dialog).getByTestId('invitation-create-submit'));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'An unexpected error occurred.',
    );
    expect(within(dialog).getByLabelText(/Email/)).toHaveValue('ann@example.com');
    expect(within(dialog).getByTestId('invitation-create-submit')).toBeEnabled();
  });
});
