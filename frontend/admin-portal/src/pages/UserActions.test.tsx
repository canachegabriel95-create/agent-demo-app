import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { UserDetails } from '../api/users';
import {
  ADMIN,
  json,
  problem,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';
import { PROFILE_CONFLICT_MESSAGE } from './EditProfileDialog';
import { USER_CONFLICT_MESSAGE } from './SuspendUserDialog';

const ACTIVE: UserDetails = {
  id: 'u-ann',
  fullName: 'Ann Author',
  email: 'ann@example.com',
  bio: 'Writes about testing.',
  role: 'Author',
  status: 'Active',
  registeredAt: '2026-10-20T09:00:00Z',
  suspendedAt: null,
  suspensionReason: null,
  articleCounts: { draft: 3, pendingReview: 1, published: 2, rejected: 4 },
  version: 100,
};

const SUSPENDED: UserDetails = {
  ...ACTIVE,
  status: 'Suspended',
  suspendedAt: '2026-10-22T11:30:00Z',
  suspensionReason: 'Posted spam links repeatedly.',
  version: 200,
};

let current: UserDetails;
let putProfile: Handler;
let suspend: Handler;
let reactivate: Handler;
let fetchMock: ReturnType<typeof stubApi>;

function bodyOf(init: RequestInit | undefined) {
  return JSON.parse(String(init?.body)) as Record<string, unknown>;
}

function callsTo(method: string, path: string) {
  return fetchMock.mock.calls.filter(
    ([input, init]) =>
      new URL(String(input)).pathname === path && (init?.method ?? 'GET') === method,
  );
}

beforeEach(() => {
  storeSession();
  current = ACTIVE;
  putProfile = (init) => {
    const body = bodyOf(init);
    current = {
      ...current,
      fullName: String(body.fullName),
      bio: String(body.bio) || null,
      version: current.version + 1,
    };
    return json(current);
  };
  suspend = (init) => {
    current = {
      ...SUSPENDED,
      suspensionReason: String(bodyOf(init).reason),
      version: current.version + 1,
    };
    return json(current);
  };
  reactivate = () => {
    current = { ...ACTIVE, version: current.version + 1 };
    return json(current);
  };
  fetchMock = stubApi({
    'GET /api/auth/me': () => json({ ...ADMIN, status: 'Active' }),
    'GET /api/admin/users/u-ann': () => json(current),
    'PUT /api/admin/users/u-ann/profile': (init, url) => putProfile(init, url),
    'POST /api/admin/users/u-ann/suspend': (init, url) => suspend(init, url),
    'POST /api/admin/users/u-ann/reactivate': (init, url) => reactivate(init, url),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openPage() {
  renderAt('/users/u-ann');
  await screen.findByRole('heading', { level: 1, name: current.fullName });
}

describe('which actions are offered', () => {
  it('offers Edit profile and Suspend, but not Reactivate, for an Active author', async () => {
    await openPage();

    expect(screen.getByRole('button', { name: 'Edit profile' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Suspend' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reactivate' })).not.toBeInTheDocument();
  });

  it('offers Edit profile and Reactivate, but not Suspend, for a Suspended author', async () => {
    current = SUSPENDED;
    await openPage();

    expect(screen.getByRole('button', { name: 'Edit profile' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reactivate' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Suspend' })).not.toBeInTheDocument();
  });

  it('shows email, role and password as read-only text with no way to edit them', async () => {
    await openPage();
    const user = userEvent.setup();

    expect(screen.getByTestId('user-email')).toHaveTextContent('ann@example.com');
    expect(screen.getByText(/cannot be changed here/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit profile' }));

    const dialog = screen.getByRole('dialog');
    expect(
      within(dialog)
        .getAllByRole('textbox')
        .map((box) => box.getAttribute('name')),
    ).toEqual(['fullName', 'bio']);
    expect(dialog.querySelector('input[type=email], input[type=password]')).toBeNull();
  });
});

describe('edit profile', () => {
  it('is a labelled dialog with the current values and live character counters', async () => {
    await openPage();
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: 'Edit profile' }));

    const dialog = screen.getByRole('dialog', { name: 'Edit profile' });
    expect(within(dialog).getByRole('textbox', { name: /Display name/ })).toHaveValue('Ann Author');
    expect(within(dialog).getByRole('textbox', { name: 'Biography' })).toHaveValue(
      'Writes about testing.',
    );
    expect(within(dialog).getByText('10 / 100 characters (at least 2)')).toBeInTheDocument();
    expect(within(dialog).getByText('21 / 1000 characters')).toBeInTheDocument();

    await user.type(within(dialog).getByRole('textbox', { name: 'Biography' }), '!!');
    expect(within(dialog).getByText('23 / 1000 characters')).toBeInTheDocument();
  });

  it('saves the trimmed values with the loaded version, shows the result and a success message', async () => {
    await openPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Edit profile' }));
    const dialog = screen.getByRole('dialog');

    await user.clear(within(dialog).getByRole('textbox', { name: /Display name/ }));
    await user.type(
      within(dialog).getByRole('textbox', { name: /Display name/ }),
      '  Ann B. Author ',
    );
    await user.clear(within(dialog).getByRole('textbox', { name: 'Biography' }));
    await user.type(within(dialog).getByRole('textbox', { name: 'Biography' }), 'New bio');
    await user.click(screen.getByTestId('profile-save'));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Ann B. Author' }),
    ).toBeInTheDocument();
    expect(bodyOf(callsTo('PUT', '/api/admin/users/u-ann/profile')[0]![1])).toEqual({
      fullName: 'Ann B. Author',
      bio: 'New bio',
      version: 100,
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByTestId('user-bio')).toHaveTextContent('New bio');
    expect(screen.getByRole('status')).toHaveTextContent('The profile was saved.');
    // Focus goes back to the button that opened the dialog.
    expect(screen.getByRole('button', { name: 'Edit profile' })).toHaveFocus();
  });

  it('uses the new version for a second edit', async () => {
    await openPage();
    const user = userEvent.setup();
    for (const name of ['Ann One', 'Ann Two']) {
      await user.click(screen.getByRole('button', { name: 'Edit profile' }));
      const field = screen.getByRole('textbox', { name: /Display name/ });
      await user.clear(field);
      await user.type(field, name);
      await user.click(screen.getByTestId('profile-save'));
      await screen.findByRole('heading', { level: 1, name });
    }

    const versions = callsTo('PUT', '/api/admin/users/u-ann/profile').map(
      ([, init]) => bodyOf(init).version,
    );
    expect(versions).toEqual([100, 101]);
  });

  it.each([
    ['', 'Full name is required.'],
    ['   ', 'Full name is required.'],
    ['A', 'Full name must be between 2 and 100 characters.'],
    ['x'.repeat(101), 'Full name must be between 2 and 100 characters.'],
  ])('rejects the name %j without sending anything', async (name, message) => {
    await openPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Edit profile' }));
    const field = screen.getByRole('textbox', { name: /Display name/ });
    await user.clear(field);
    if (name) {
      await user.type(field, name, { delay: null });
    }

    await user.click(screen.getByTestId('profile-save'));

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(field).toHaveAttribute('aria-invalid', 'true');
    expect(callsTo('PUT', '/api/admin/users/u-ann/profile')).toHaveLength(0);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('clears a name error as soon as the name is edited', async () => {
    await openPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Edit profile' }));
    const field = screen.getByRole('textbox', { name: /Display name/ });
    await user.clear(field);
    await user.click(screen.getByTestId('profile-save'));
    expect(await screen.findByText('Full name is required.')).toBeInTheDocument();

    await user.type(field, 'Ann');

    expect(screen.queryByText('Full name is required.')).not.toBeInTheDocument();
  });

  it('rejects a biography over 1000 characters but accepts exactly 1000', async () => {
    await openPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Edit profile' }));
    const bio = screen.getByRole('textbox', { name: 'Biography' });
    await user.clear(bio);
    await user.click(bio);
    await user.paste('b'.repeat(1001));

    await user.click(screen.getByTestId('profile-save'));

    expect(
      await screen.findByText('Biography must be at most 1000 characters.'),
    ).toBeInTheDocument();
    expect(callsTo('PUT', '/api/admin/users/u-ann/profile')).toHaveLength(0);

    await user.clear(bio);
    await user.click(bio);
    await user.paste('b'.repeat(1000));
    await user.click(screen.getByTestId('profile-save'));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(callsTo('PUT', '/api/admin/users/u-ann/profile')).toHaveLength(1);
  });

  it('shows a server validation message next to the field', async () => {
    putProfile = () =>
      json(
        { title: 'Invalid', status: 400, errors: { fullName: ['Full name is not allowed.'] } },
        400,
        'application/problem+json',
      );
    await openPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Edit profile' }));

    await user.click(screen.getByTestId('profile-save'));

    expect(await screen.findByText('Full name is not allowed.')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('on a conflict asks to reload, keeps the typed text out of the save and reloads on request', async () => {
    putProfile = () => problem(409, 'Conflict');
    await openPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Edit profile' }));
    await user.click(screen.getByTestId('profile-save'));

    expect(await screen.findByTestId('profile-conflict')).toHaveTextContent(
      PROFILE_CONFLICT_MESSAGE,
    );
    expect(screen.queryByTestId('profile-save')).not.toBeInTheDocument();

    current = { ...ACTIVE, fullName: 'Ann Changed Elsewhere', version: 150 };
    await user.click(screen.getByTestId('profile-reload'));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Ann Changed Elsewhere' }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(callsTo('PUT', '/api/admin/users/u-ann/profile')).toHaveLength(1);
  });

  it('Cancel and Escape close the dialog without saving and return focus to the button', async () => {
    await openPage();
    const user = userEvent.setup();
    const opener = screen.getByRole('button', { name: 'Edit profile' });

    await user.click(opener);
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();

    await user.click(opener);
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
    expect(callsTo('PUT', '/api/admin/users/u-ann/profile')).toHaveLength(0);
  });
});

describe('suspend', () => {
  async function openSuspend() {
    await openPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Suspend' }));
    return { user, dialog: screen.getByRole('dialog', { name: 'Suspend Ann Author' }) };
  }

  it('is a labelled dialog that explains the effect and starts with the reason field focused', async () => {
    const { dialog } = await openSuspend();

    expect(within(dialog).getByText(/signed out everywhere/)).toBeInTheDocument();
    expect(within(dialog).getByRole('textbox', { name: /Reason/ })).toHaveFocus();
    expect(within(dialog).getByTestId('suspend-confirm')).toBeInTheDocument();
  });

  it.each([
    ['', 'A reason is required.'],
    ['         ', 'A reason is required.'],
    ['too short', 'The reason must be between 10 and 500 characters.'],
    ['r'.repeat(501), 'The reason must be between 10 and 500 characters.'],
  ])('requires a reason of 10 to 500 characters: %j', async (reason, message) => {
    const { user } = await openSuspend();
    if (reason) {
      await user.click(screen.getByRole('textbox', { name: /Reason/ }));
      await user.paste(reason);
    }

    await user.click(screen.getByTestId('suspend-confirm'));

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(callsTo('POST', '/api/admin/users/u-ann/suspend')).toHaveLength(0);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('clears the reason error as soon as the reason is edited', async () => {
    const { user } = await openSuspend();
    await user.click(screen.getByTestId('suspend-confirm'));
    expect(await screen.findByText('A reason is required.')).toBeInTheDocument();

    await user.type(screen.getByRole('textbox', { name: /Reason/ }), 'Posted spam links.');

    expect(screen.queryByText('A reason is required.')).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: /Reason/ })).not.toHaveAttribute('aria-invalid');
  });

  it.each([10, 500])('accepts a reason of exactly %i characters', async (length) => {
    const { user } = await openSuspend();
    await user.click(screen.getByRole('textbox', { name: /Reason/ }));
    await user.paste('r'.repeat(length));

    await user.click(screen.getByTestId('suspend-confirm'));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(callsTo('POST', '/api/admin/users/u-ann/suspend')).toHaveLength(1);
  });

  it('sends the trimmed reason and version, then shows Suspended, the reason and a success message', async () => {
    const { user } = await openSuspend();
    await user.click(screen.getByRole('textbox', { name: /Reason/ }));
    await user.paste('  Posted spam links repeatedly.  ');

    await user.click(screen.getByTestId('suspend-confirm'));

    expect(await screen.findByTestId('user-suspension')).toBeInTheDocument();
    expect(bodyOf(callsTo('POST', '/api/admin/users/u-ann/suspend')[0]![1])).toEqual({
      reason: 'Posted spam links repeatedly.',
      version: 100,
    });
    expect(screen.getByTestId('user-status')).toHaveTextContent('Suspended');
    expect(screen.getByTestId('user-suspension-reason')).toHaveTextContent(
      'Posted spam links repeatedly.',
    );
    expect(screen.getByRole('status')).toHaveTextContent('Ann Author was suspended');
    expect(screen.queryByRole('button', { name: 'Suspend' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reactivate' })).toBeInTheDocument();
  });

  it('on a conflict says so, offers Reload and shows the current author after reloading', async () => {
    suspend = () => problem(409, 'Conflict');
    const { user } = await openSuspend();
    await user.click(screen.getByRole('textbox', { name: /Reason/ }));
    await user.paste('Posted spam links repeatedly.');
    await user.click(screen.getByTestId('suspend-confirm'));

    expect(await screen.findByTestId('suspend-conflict')).toHaveTextContent(USER_CONFLICT_MESSAGE);
    expect(screen.queryByTestId('suspend-confirm')).not.toBeInTheDocument();

    current = SUSPENDED; // someone else suspended the author meanwhile
    await user.click(screen.getByTestId('suspend-reload'));

    expect(await screen.findByTestId('user-suspension')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Suspend' })).not.toBeInTheDocument();
  });

  it('shows an unexpected failure with its reference and keeps the dialog open', async () => {
    suspend = () =>
      json(
        { title: 'Server error', status: 500, correlationId: 'abc-123' },
        500,
        'application/problem+json',
      );
    const { user } = await openSuspend();
    await user.click(screen.getByRole('textbox', { name: /Reason/ }));
    await user.paste('Posted spam links repeatedly.');

    await user.click(screen.getByTestId('suspend-confirm'));

    expect(await screen.findByRole('alert')).toHaveTextContent('abc-123');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByTestId('suspend-confirm')).toBeEnabled();
  });

  it('Escape closes the dialog without suspending and focus returns to the Suspend button', async () => {
    const { user } = await openSuspend();

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Suspend' })).toHaveFocus();
    expect(callsTo('POST', '/api/admin/users/u-ann/suspend')).toHaveLength(0);
  });
});

describe('reactivate', () => {
  beforeEach(() => {
    current = SUSPENDED;
  });

  async function openReactivate() {
    await openPage();
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Reactivate' }));
    return { user, dialog: screen.getByRole('dialog', { name: 'Reactivate Ann Author' }) };
  }

  it('asks for confirmation and explains that the author must sign in again', async () => {
    const { dialog } = await openReactivate();

    expect(within(dialog).getByText(/must sign in again/)).toBeInTheDocument();
    expect(callsTo('POST', '/api/admin/users/u-ann/reactivate')).toHaveLength(0);
  });

  it('sends the version, then shows Active, no suspension box and a success message', async () => {
    const { user } = await openReactivate();

    await user.click(screen.getByTestId('reactivate-confirm'));

    await waitFor(() => expect(screen.getByTestId('user-status')).toHaveTextContent('Active'));
    expect(bodyOf(callsTo('POST', '/api/admin/users/u-ann/reactivate')[0]![1])).toEqual({
      version: 200,
    });
    expect(screen.queryByTestId('user-suspension')).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Ann Author was reactivated');
    expect(screen.getByRole('button', { name: 'Suspend' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reactivate' })).not.toBeInTheDocument();
  });

  it('on a conflict says so and reloads on request', async () => {
    reactivate = () => problem(409, 'Conflict');
    const { user } = await openReactivate();
    await user.click(screen.getByTestId('reactivate-confirm'));

    expect(await screen.findByTestId('reactivate-conflict')).toHaveTextContent(
      USER_CONFLICT_MESSAGE,
    );

    current = ACTIVE;
    await user.click(screen.getByTestId('reactivate-reload'));

    await waitFor(() => expect(screen.getByTestId('user-status')).toHaveTextContent('Active'));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('Cancel closes the dialog without reactivating', async () => {
    const { user } = await openReactivate();

    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reactivate' })).toHaveFocus();
    expect(callsTo('POST', '/api/admin/users/u-ann/reactivate')).toHaveLength(0);
  });
});
