import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Profile } from '../api/profile';
import {
  AUTHOR,
  json,
  problem,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';
import { CONFLICT_MESSAGE, LEAVE_MESSAGE, SAVED_MESSAGE } from './ProfilePage';

const PROFILE: Profile = {
  id: AUTHOR.id,
  fullName: 'Jane Author',
  email: 'author@demo.local',
  bio: 'Writes about testing.',
  role: 'Author',
  registeredAt: '2026-10-20T09:00:00Z',
  version: 100,
};

let current: Profile;
let getProfile: Handler;
let putProfile: Handler;
let fetchMock: ReturnType<typeof stubApi>;

function bodyOf(init: RequestInit | undefined) {
  return JSON.parse(String(init?.body)) as Record<string, unknown>;
}

function puts() {
  return fetchMock.mock.calls.filter(
    ([input, init]) =>
      new URL(String(input)).pathname === '/api/me/profile' && init?.method === 'PUT',
  );
}

beforeEach(() => {
  storeSession();
  current = PROFILE;
  getProfile = () => json(current);
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
  fetchMock = stubApi({
    'GET /api/auth/me': () => json(AUTHOR),
    'GET /api/me/profile': (init, url) => getProfile(init, url),
    'PUT /api/me/profile': (init, url) => putProfile(init, url),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function openPage() {
  const router = renderAt('/profile');
  await screen.findByTestId('profile-fullname');
  return router;
}

const nameField = () => screen.getByTestId('profile-fullname');
const bioField = () => screen.getByTestId('profile-bio');

async function replace(field: HTMLElement, text: string) {
  const user = userEvent.setup();
  await user.clear(field);
  if (text) {
    await user.click(field);
    await user.paste(text);
  }
}

describe('showing the profile', () => {
  it('shows email, role and registration date read-only and name and biography in the form', async () => {
    await openPage();

    expect(screen.getByTestId('profile-email')).toHaveTextContent('author@demo.local');
    expect(screen.getByTestId('profile-role')).toHaveTextContent('Author');
    expect(screen.getByTestId('profile-registered')).toHaveTextContent('October 20, 2026');
    expect(nameField()).toHaveValue('Jane Author');
    expect(bioField()).toHaveValue('Writes about testing.');
    // Only the two editable fields are inputs: email and role have no control at all.
    expect(screen.getAllByRole('textbox').map((box) => box.getAttribute('name'))).toEqual([
      'fullName',
      'bio',
    ]);
    expect(screen.queryByDisplayValue('author@demo.local')).not.toBeInTheDocument();
  });

  it('sends the session token and has a Profile link marked as the current page', async () => {
    await openPage();

    const call = fetchMock.mock.calls.find(([input]) => String(input).endsWith('/api/me/profile'))!;
    expect(new Headers(call[1]!.headers).get('Authorization')).toBe('Bearer valid-token');
    expect(screen.getByRole('link', { name: 'Profile' })).toHaveAttribute('aria-current', 'page');
  });

  it('shows an empty biography as an empty field', async () => {
    current = { ...PROFILE, bio: null };
    await openPage();

    expect(bioField()).toHaveValue('');
  });

  it('shows live character counters', async () => {
    await openPage();

    expect(screen.getByText('11 / 100 characters (at least 2)')).toBeInTheDocument();
    expect(screen.getByText('21 / 1000 characters')).toBeInTheDocument();

    await userEvent.setup().type(bioField(), '!!');

    expect(screen.getByText('23 / 1000 characters')).toBeInTheDocument();
  });

  it('shows a loading state, then the form', async () => {
    let release!: () => void;
    getProfile = () =>
      new Promise((resolve) => {
        release = () => resolve(json(current));
      });
    renderAt('/profile');

    expect(await screen.findByText('Loading your profile…')).toBeInTheDocument();
    release();

    expect(await screen.findByTestId('profile-fullname')).toBeInTheDocument();
  });

  it('shows an error with a retry', async () => {
    getProfile = () => problem(500, 'Server error');
    renderAt('/profile');
    expect(await screen.findByRole('alert')).toHaveTextContent('Your profile could not be loaded');

    getProfile = () => json(current);
    await userEvent.setup().click(screen.getByRole('button', { name: /try again/i }));

    expect(await screen.findByTestId('profile-fullname')).toBeInTheDocument();
  });
});

describe('validation', () => {
  it.each([
    ['', 'Full name is required.'],
    ['   ', 'Full name is required.'],
    ['A', 'Full name must be between 2 and 100 characters.'],
    ['x'.repeat(101), 'Full name must be between 2 and 100 characters.'],
  ])('refuses the name %j without saving', async (name, message) => {
    await openPage();
    await replace(nameField(), name);

    await userEvent.setup().click(screen.getByTestId('profile-save'));

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(nameField()).toHaveAttribute('aria-invalid', 'true');
    expect(puts()).toHaveLength(0);
    expect(screen.queryByText(SAVED_MESSAGE)).not.toBeInTheDocument();
  });

  it('refuses a biography over 1000 characters but accepts exactly 1000', async () => {
    await openPage();
    const user = userEvent.setup();
    await replace(bioField(), 'b'.repeat(1001));

    await user.click(screen.getByTestId('profile-save'));

    expect(
      await screen.findByText('Biography must be at most 1000 characters.'),
    ).toBeInTheDocument();
    expect(puts()).toHaveLength(0);

    await replace(bioField(), 'b'.repeat(1000));
    await user.click(screen.getByTestId('profile-save'));

    expect(await screen.findByText(SAVED_MESSAGE)).toBeInTheDocument();
    expect(puts()).toHaveLength(1);
  });

  it('clears a field message as soon as that field is edited', async () => {
    await openPage();
    await replace(nameField(), '');
    await userEvent.setup().click(screen.getByTestId('profile-save'));
    expect(await screen.findByText('Full name is required.')).toBeInTheDocument();

    await userEvent.setup().type(nameField(), 'Jane');

    expect(screen.queryByText('Full name is required.')).not.toBeInTheDocument();
  });

  it('shows a message from the API next to its field', async () => {
    putProfile = () =>
      json(
        { title: 'Invalid', status: 400, errors: { fullName: ['Full name is not allowed.'] } },
        400,
        'application/problem+json',
      );
    await openPage();
    await replace(nameField(), 'Janet Author');

    await userEvent.setup().click(screen.getByTestId('profile-save'));

    expect(await screen.findByText('Full name is not allowed.')).toBeInTheDocument();
    expect(nameField()).toHaveAttribute('aria-invalid', 'true');
  });
});

describe('saving', () => {
  it('sends the trimmed values and the loaded version, then announces "Profile saved"', async () => {
    await openPage();
    await replace(nameField(), '  Jane B. Author ');
    await replace(bioField(), '  New bio ');

    await userEvent.setup().click(screen.getByTestId('profile-save'));

    expect(await screen.findByText(SAVED_MESSAGE)).toBeInTheDocument();
    expect(bodyOf(puts()[0]![1])).toEqual({
      fullName: 'Jane B. Author',
      bio: 'New bio',
      version: 100,
    });
    expect(screen.getByTestId('profile-status')).toHaveAttribute('role', 'status');
    expect(
      within(screen.getByTestId('profile-status')).getByText(SAVED_MESSAGE),
    ).toBeInTheDocument();
    expect(nameField()).toHaveValue('Jane B. Author');
    expect(bioField()).toHaveValue('New bio');
  });

  it('updates the name in the header without reloading the page', async () => {
    await openPage();
    expect(screen.getByTestId('current-user')).toHaveTextContent('Jane Author');
    await replace(nameField(), 'Jane B. Author');

    await userEvent.setup().click(screen.getByTestId('profile-save'));

    await waitFor(() =>
      expect(screen.getByTestId('current-user')).toHaveTextContent('Jane B. Author'),
    );
    expect(
      fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/api/auth/me')),
    ).toHaveLength(1);
  });

  it('uses the new version for the next save', async () => {
    await openPage();
    const user = userEvent.setup();
    for (const name of ['Jane One', 'Jane Two']) {
      await replace(nameField(), name);
      await user.click(screen.getByTestId('profile-save'));
      await waitFor(() => expect(screen.getByTestId('current-user')).toHaveTextContent(name));
    }

    expect(puts().map(([, init]) => bodyOf(init).version)).toEqual([100, 101]);
  });

  it('removes the message when editing again', async () => {
    await openPage();
    await replace(nameField(), 'Jane Two');
    await userEvent.setup().click(screen.getByTestId('profile-save'));
    expect(await screen.findByText(SAVED_MESSAGE)).toBeInTheDocument();

    await userEvent.setup().type(nameField(), '!');

    expect(screen.queryByText(SAVED_MESSAGE)).not.toBeInTheDocument();
  });

  it('shows an unexpected failure with its reference and keeps what was typed', async () => {
    putProfile = () =>
      json(
        { title: 'Server error', status: 500, correlationId: 'abc-123' },
        500,
        'application/problem+json',
      );
    await openPage();
    await replace(nameField(), 'Jane Two');

    await userEvent.setup().click(screen.getByTestId('profile-save'));

    expect(await screen.findByRole('alert')).toHaveTextContent('abc-123');
    expect(nameField()).toHaveValue('Jane Two');
    expect(screen.getByTestId('profile-save')).toBeEnabled();
    expect(screen.getByTestId('current-user')).toHaveTextContent('Jane Author');
  });

  it('on a conflict asks to reload, disables the form and loads the current profile on request', async () => {
    putProfile = () => problem(409, 'Conflict');
    await openPage();
    await replace(nameField(), 'Jane Two');
    const user = userEvent.setup();
    await user.click(screen.getByTestId('profile-save'));

    expect(await screen.findByTestId('profile-conflict')).toHaveTextContent(CONFLICT_MESSAGE);
    expect(nameField()).toBeDisabled();
    expect(screen.getByTestId('profile-save')).toBeDisabled();

    current = { ...PROFILE, fullName: 'Jane Changed Elsewhere', version: 150 };
    await user.click(screen.getByTestId('profile-reload'));

    await waitFor(() => expect(nameField()).toHaveValue('Jane Changed Elsewhere'));
    expect(nameField()).toBeEnabled();
    expect(screen.queryByTestId('profile-conflict')).not.toBeInTheDocument();
    expect(screen.getByTestId('current-user')).toHaveTextContent('Jane Author'); // header changes only on a save
  });
});

describe('unsaved changes', () => {
  it('asks before leaving through a link and stays when the author says no', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const router = await openPage();
    await replace(nameField(), 'Jane Two');

    await userEvent.setup().click(screen.getByRole('link', { name: 'Feed' }));

    await waitFor(() => expect(confirm).toHaveBeenCalledWith(LEAVE_MESSAGE));
    expect(router.state.location.pathname).toBe('/profile');
    expect(nameField()).toHaveValue('Jane Two');
  });

  it('leaves when the author confirms', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const router = await openPage();
    await replace(bioField(), 'Something else');

    await userEvent.setup().click(screen.getByRole('link', { name: 'Feed' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/feed'));
  });

  it('does not ask when nothing changed', async () => {
    const confirm = vi.spyOn(window, 'confirm');
    const router = await openPage();

    await userEvent.setup().click(screen.getByRole('link', { name: 'Feed' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/feed'));
    expect(confirm).not.toHaveBeenCalled();
  });

  it('does not ask after a successful save', async () => {
    const confirm = vi.spyOn(window, 'confirm');
    const router = await openPage();
    await replace(nameField(), 'Jane Two');
    await userEvent.setup().click(screen.getByTestId('profile-save'));
    await screen.findByText(SAVED_MESSAGE);

    await userEvent.setup().click(screen.getByRole('link', { name: 'Feed' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/feed'));
    expect(confirm).not.toHaveBeenCalled();
  });

  it('does not ask when the text is changed and then changed back', async () => {
    const confirm = vi.spyOn(window, 'confirm');
    const router = await openPage();
    await replace(nameField(), 'Jane Two');
    await replace(nameField(), 'Jane Author');

    await userEvent.setup().click(screen.getByRole('link', { name: 'Feed' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/feed'));
    expect(confirm).not.toHaveBeenCalled();
  });

  it('warns on closing the tab only while there are unsaved changes', async () => {
    await openPage();
    const event = () => {
      const e = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(e);
      return e.defaultPrevented;
    };
    expect(event()).toBe(false);

    await replace(nameField(), 'Jane Two');

    expect(event()).toBe(true);
  });
});
