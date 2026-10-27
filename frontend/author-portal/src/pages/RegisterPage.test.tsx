import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  AUTHOR,
  healthy,
  json,
  renderAt,
  storeSession,
  stubApi,
  type Handler,
} from '../test/authHelpers';
import {
  FIX_FIELDS_MESSAGE,
  GENERIC_FAILURE_MESSAGE,
  TOO_MANY_ATTEMPTS_MESSAGE,
} from './RegisterPage';
import { REGISTERED_MESSAGE } from './LoginPage';

const PASSWORD = 'Str0ng!Passw0rd#9';

let registration: Handler;
let fetchMock: ReturnType<typeof stubApi>;

const problemJson = (status: number, body: Record<string, unknown>) =>
  json({ status, ...body }, status, 'application/problem+json');

beforeEach(() => {
  registration = () => json({ id: 'u1', email: 'jane@demo.local', fullName: 'Jane Doe' }, 201);
  fetchMock = stubApi({
    'POST /api/registration': (init, url) => registration(init, url),
    'GET /api/auth/me': () => json({ ...AUTHOR, status: 'Active' }),
    'GET /health': () => healthy(),
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function registrationCalls() {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST');
}

async function fillValid(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByTestId('register-invitation-code'), 'abcd-efgh-jkmn');
  await user.type(screen.getByTestId('register-full-name'), '  Jane Doe ');
  await user.type(screen.getByTestId('register-email'), ' jane@demo.local ');
  await user.type(screen.getByTestId('register-password'), PASSWORD);
  await user.type(screen.getByTestId('register-confirm-password'), PASSWORD);
}

describe('the form', () => {
  it('shows labelled fields with the right autocomplete hints and test ids', () => {
    renderAt('/register');

    expect(
      screen.getByRole('heading', { level: 1, name: 'Create an account' }),
    ).toBeInTheDocument();
    const expectations: [string, string, string | null][] = [
      ['Invitation code', 'register-invitation-code', 'off'],
      ['Full name', 'register-full-name', 'name'],
      ['Email', 'register-email', 'email'],
      ['Password', 'register-password', 'new-password'],
      ['Confirm password', 'register-confirm-password', 'new-password'],
    ];
    for (const [label, testId, autocomplete] of expectations) {
      const input = screen.getByLabelText(new RegExp(`^${label}`));
      expect(input).toBe(screen.getByTestId(testId));
      expect(input).toHaveAttribute('autocomplete', autocomplete);
      expect(input).toBeRequired();
    }
    expect(screen.getByLabelText(/^Password/)).toHaveAttribute('type', 'password');
    expect(screen.getByLabelText(/^Confirm password/)).toHaveAttribute('type', 'password');
    expect(screen.getByRole('button', { name: 'Create account' })).toBe(
      screen.getByTestId('register-submit'),
    );
  });

  it('prefills the invitation code from ?code=', () => {
    renderAt('/register?code=ABCD-EFGH-JKMN');

    expect(screen.getByTestId('register-invitation-code')).toHaveValue('ABCD-EFGH-JKMN');
  });

  it('starts with an empty code when there is no ?code=', () => {
    renderAt('/register');

    expect(screen.getByTestId('register-invitation-code')).toHaveValue('');
  });

  it('shows the live password checklist, linked to the password field', async () => {
    const user = userEvent.setup();
    renderAt('/register');
    const list = screen.getByTestId('password-requirements');
    expect(within(list).getAllByText('Not met.', { exact: false })).toHaveLength(5);
    expect(screen.getByTestId('register-password')).toHaveAttribute(
      'aria-describedby',
      expect.stringContaining('register-password-requirements'),
    );

    await user.type(screen.getByTestId('register-password'), 'abc');

    expect(list.querySelector('[data-requirement="lowercase"]')).toHaveAttribute(
      'data-met',
      'true',
    );
    expect(list.querySelector('[data-requirement="uppercase"]')).toHaveAttribute(
      'data-met',
      'false',
    );
  });

  it('links back to the sign-in page', async () => {
    const user = userEvent.setup();
    const router = renderAt('/register');

    await user.click(screen.getByTestId('register-login-link'));

    expect(router.state.location.pathname).toBe('/login');
  });

  it('sends a signed-in author to the home page', async () => {
    storeSession();
    const router = renderAt('/register');

    await waitFor(() => expect(router.state.location.pathname).toBe('/'));
  });
});

describe('client-side validation', () => {
  it('checks a field when it loses focus and shows the API sentences', async () => {
    const user = userEvent.setup();
    renderAt('/register');

    await user.click(screen.getByTestId('register-email'));
    await user.tab();
    expect(screen.getByText('Email is required.')).toBeInTheDocument();
    expect(screen.getByTestId('register-email')).toBeInvalid();

    await user.type(screen.getByTestId('register-email'), 'nope');
    await user.tab();
    expect(
      screen.getByText('Enter a valid email address of at most 254 characters.'),
    ).toBeInTheDocument();

    await user.type(screen.getByTestId('register-full-name'), 'J');
    await user.tab();
    expect(screen.getByText('Full name must be between 2 and 100 characters.')).toBeInTheDocument();
  });

  it('clears a message as soon as the field is corrected', async () => {
    const user = userEvent.setup();
    renderAt('/register');
    await user.click(screen.getByTestId('register-full-name'));
    await user.tab();
    expect(screen.getByText('Full name is required.')).toBeInTheDocument();

    await user.type(screen.getByTestId('register-full-name'), 'Jane');

    expect(screen.queryByText('Full name is required.')).not.toBeInTheDocument();
    expect(screen.getByTestId('register-full-name')).not.toBeInvalid();
  });

  it('flags a weak password and a confirmation that does not match', async () => {
    const user = userEvent.setup();
    renderAt('/register');

    await user.type(screen.getByTestId('register-password'), 'weak');
    await user.tab();
    expect(
      screen.getByText('The password does not meet the requirements below.'),
    ).toBeInTheDocument();

    await user.clear(screen.getByTestId('register-password'));
    await user.type(screen.getByTestId('register-password'), PASSWORD);
    await user.type(screen.getByTestId('register-confirm-password'), 'different');
    await user.tab();
    expect(screen.getByText('Passwords do not match.')).toBeInTheDocument();

    await user.clear(screen.getByTestId('register-confirm-password'));
    await user.type(screen.getByTestId('register-confirm-password'), PASSWORD);
    expect(screen.queryByText('Passwords do not match.')).not.toBeInTheDocument();
  });

  it('re-checks the confirmation when the password changes afterwards', async () => {
    const user = userEvent.setup();
    renderAt('/register');
    await user.type(screen.getByTestId('register-password'), PASSWORD);
    await user.type(screen.getByTestId('register-confirm-password'), PASSWORD);
    await user.tab();
    expect(screen.queryByText('Passwords do not match.')).not.toBeInTheDocument();

    await user.type(screen.getByTestId('register-password'), 'x');
    await user.tab();

    expect(screen.getByText('Passwords do not match.')).toBeInTheDocument();
  });

  it('does not send an invalid form; it lists the problems and moves focus to the summary', async () => {
    const user = userEvent.setup();
    renderAt('/register');

    await user.click(screen.getByTestId('register-submit'));

    const summary = screen.getByTestId('register-error-summary');
    expect(summary).toHaveAttribute('role', 'alert');
    expect(summary).toHaveTextContent(FIX_FIELDS_MESSAGE);
    expect(summary).toHaveFocus();
    expect(screen.getByText('Invitation code is required.')).toBeInTheDocument();
    expect(screen.getByText('Full name is required.')).toBeInTheDocument();
    expect(screen.getByText('Email is required.')).toBeInTheDocument();
    expect(screen.getByText('Password is required.')).toBeInTheDocument();
    expect(screen.getByText('Confirm your password.')).toBeInTheDocument();
    expect(registrationCalls()).toHaveLength(0);
  });
});

describe('submitting', () => {
  it('sends trimmed values and goes to the sign-in page with the success banner', async () => {
    const user = userEvent.setup();
    let body: unknown;
    registration = (init) => {
      body = JSON.parse(String(init?.body));
      return json({ id: 'u1', email: 'jane@demo.local', fullName: 'Jane Doe' }, 201);
    };
    const router = renderAt('/register');
    await fillValid(user);

    await user.click(screen.getByTestId('register-submit'));

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'));
    expect(router.state.location.search).toBe('?registered=1');
    expect(body).toEqual({
      invitationCode: 'abcd-efgh-jkmn',
      fullName: 'Jane Doe',
      email: 'jane@demo.local',
      password: PASSWORD,
      confirmPassword: PASSWORD,
    });
    expect(await screen.findByTestId('login-registered')).toHaveTextContent(REGISTERED_MESSAGE);
  });

  it('is disabled while the request runs and cannot be sent twice', async () => {
    const user = userEvent.setup();
    let release!: () => void;
    registration = () =>
      new Promise((resolve) => {
        release = () =>
          resolve(json({ id: 'u1', email: 'jane@demo.local', fullName: 'Jane Doe' }, 201));
      });
    renderAt('/register');
    await fillValid(user);
    const submit = screen.getByTestId('register-submit');

    await user.click(submit);
    await user.click(submit);
    await user.keyboard('{Enter}');

    expect(submit).toBeDisabled();
    expect(submit).toHaveTextContent('Creating account…');
    expect(screen.getByTestId('register-email')).toBeDisabled();
    expect(registrationCalls()).toHaveLength(1);
    release();
  });
});

describe('errors from the API', () => {
  async function submitWith(error: Response) {
    registration = () => error;
    const user = userEvent.setup();
    renderAt('/register');
    await fillValid(user);
    await user.click(screen.getByTestId('register-submit'));
    return user;
  }

  it.each([
    [
      'invitationCode',
      'register-invitation-code',
      'This invitation code is not valid. Check the code or ask an administrator for a new one.',
    ],
    [
      'invitationCode',
      'register-invitation-code',
      'This invitation has expired. Ask an administrator for a new one.',
    ],
    [
      'invitationCode',
      'register-invitation-code',
      'This invitation has been revoked. Ask an administrator for a new one.',
    ],
    ['invitationCode', 'register-invitation-code', 'This invitation has already been used.'],
    ['email', 'register-email', 'The email address does not match this invitation.'],
  ])('shows an invitation problem on the %s field: %s', async (field, testId, message) => {
    await submitWith(
      problemJson(400, {
        title: 'One or more validation errors occurred.',
        errors: { [field]: [message] },
      }),
    );

    const input = await screen.findByTestId(testId);
    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(input).toBeInvalid();
    expect(input).toHaveAccessibleDescription(expect.stringContaining(message));
    expect(screen.getByTestId('register-error-summary')).toHaveTextContent(FIX_FIELDS_MESSAGE);
    expect(screen.getByTestId('register-error-summary')).toHaveFocus();
  });

  it('shows several field errors at once and joins the password sentences', async () => {
    await submitWith(
      problemJson(400, {
        title: 'One or more validation errors occurred.',
        errors: {
          fullName: ['Full name is required.'],
          password: ['Must contain at least one digit.', 'Must contain at least one symbol.'],
        },
      }),
    );

    expect(await screen.findByText('Full name is required.')).toBeInTheDocument();
    expect(
      screen.getByText('Must contain at least one digit. Must contain at least one symbol.'),
    ).toBeInTheDocument();
  });

  it('puts messages for fields it does not know in the summary', async () => {
    await submitWith(
      problemJson(400, {
        title: 'One or more validation errors occurred.',
        errors: { surprise: ['Something else is wrong.'] },
      }),
    );

    expect(await screen.findByTestId('register-error-summary')).toHaveTextContent(
      'Something else is wrong.',
    );
  });

  it('shows "an account with this email already exists" at the email field for a 409', async () => {
    await submitWith(
      problemJson(409, {
        title: 'The request conflicts with the current state of the resource.',
        detail: 'An account with this email already exists.',
      }),
    );

    expect(
      await screen.findByText('An account with this email already exists.'),
    ).toBeInTheDocument();
    expect(screen.getByTestId('register-email')).toBeInvalid();
  });

  it('explains rate limiting in the summary', async () => {
    await submitWith(problemJson(429, { title: 'Too many attempts. Try again later.' }));

    expect(await screen.findByTestId('register-error-summary')).toHaveTextContent(
      TOO_MANY_ATTEMPTS_MESSAGE,
    );
  });

  it('shows a generic message and the reference for an unexpected failure', async () => {
    await submitWith(
      problemJson(500, { title: 'An unexpected error occurred.', correlationId: 'corr-42' }),
    );

    const summary = await screen.findByTestId('register-error-summary');
    expect(summary).toHaveTextContent(GENERIC_FAILURE_MESSAGE);
    expect(summary).toHaveTextContent('corr-42');
  });

  it('keeps what was typed and lets the person try again', async () => {
    const user = await submitWith(
      problemJson(400, {
        title: 'x',
        errors: { invitationCode: ['This invitation has already been used.'] },
      }),
    );
    await screen.findByText('This invitation has already been used.');

    expect(screen.getByTestId('register-full-name')).toHaveValue('  Jane Doe ');
    expect(screen.getByTestId('register-password')).toHaveValue(PASSWORD);
    expect(screen.getByTestId('register-submit')).toBeEnabled();

    registration = () => json({ id: 'u1', email: 'jane@demo.local', fullName: 'Jane Doe' }, 201);
    await user.click(screen.getByTestId('register-submit'));

    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument(),
    );
    expect(registrationCalls()).toHaveLength(2);
  });

  it('keeps a network failure out of the field errors', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    const user = userEvent.setup();
    renderAt('/register');
    await fillValid(user);

    await user.click(screen.getByTestId('register-submit'));

    expect(await screen.findByTestId('register-error-summary')).toHaveTextContent(
      GENERIC_FAILURE_MESSAGE,
    );
  });
});
