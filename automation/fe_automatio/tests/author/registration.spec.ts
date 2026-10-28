import { randomBytes, randomUUID } from 'node:crypto';
import type { APIRequestContext, Page } from '@playwright/test';
import { apiAs, apiUrl, createInvitation, ok } from '../support/api';
import { apiLogin } from '../support/auth';
import { setInvitationState, userExists } from '../support/db';
import { test, expect } from '../support/fixtures';

const email = () => `e2e-${randomUUID()}@example.test`;
const validPassword = () => `Aa1!${randomBytes(10).toString('hex')}`;

interface Form {
  code: string;
  fullName?: string;
  email: string;
  password?: string;
  confirm?: string;
}

async function fillAndSubmit(page: Page, form: Form) {
  const password = form.password ?? validPassword();
  await page.getByLabel('Invitation code').fill(form.code);
  await page.getByLabel('Full name').fill(form.fullName ?? 'New Author');
  await page.getByLabel('Email').fill(form.email);
  await page.getByLabel(/^Password/).fill(password);
  await page.getByLabel(/^Confirm password/).fill(form.confirm ?? password);
  await page.getByTestId('register-submit').click();
}

/** An administrator (no portal session needed) that creates invitations through the API. */
async function adminApi(request: APIRequestContext, newUser: (u: { role: 'Administrator' }) => ReturnType<typeof import('../support/db').createUser>) {
  const admin = await newUser({ role: 'Administrator' });
  return apiAs(request, await apiLogin(request, admin, 'Admin'));
}

const registerViaApi = (request: APIRequestContext, code: string, to: string, password = validPassword()) =>
  request.post(`${apiUrl}/api/registration`, {
    data: { invitationCode: code, fullName: 'API Author', email: to, password, confirmPassword: password },
  });

test.beforeEach(async ({ page }) => {
  await page.goto('/register');
});

test('a valid registration creates the account, uses the invitation and allows signing in', async ({
  page,
  request,
  newUser,
  trackEmail,
}) => {
  const api = await adminApi(request, newUser);
  const to = email();
  trackEmail(to);
  const invitation = await createInvitation(api, to);
  const password = validPassword();

  await fillAndSubmit(page, { code: invitation.code, email: to, password });
  await expect(page).toHaveURL(/\/login\?registered=1/);
  await page.getByLabel('Email').fill(to);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page).toHaveURL('/');
  const used = await ok<{ items: { status: string }[] }>(await api.get(`/api/admin/invitations?email=${to}`));
  expect(used.items[0].status).toBe('Used');
});

type Api = ReturnType<typeof apiAs>;
type Prepare = (api: Api, to: string, newUser: (u: { role: 'Author' }) => Promise<{ id: string }>) => Promise<string>;

/** Each rejected case: how to get a code that must not work for `to`. */
const badCodes: [string, Prepare][] = [
  ['unknown', async () => 'ZZZZ-ZZZZ-ZZZZ'],
  [
    'expired',
    async (api, to) => {
      const invitation = await createInvitation(api, to);
      await setInvitationState(invitation.id, { expired: true });
      return invitation.code;
    },
  ],
  [
    'revoked',
    async (api, to) => {
      const invitation = await createInvitation(api, to);
      await api.post(`/api/admin/invitations/${invitation.id}/revoke`, { version: invitation.version });
      return invitation.code;
    },
  ],
  [
    'used',
    async (api, to, newUser) => {
      const invitation = await createInvitation(api, to);
      await setInvitationState(invitation.id, { usedBy: (await newUser({ role: 'Author' })).id });
      return invitation.code;
    },
  ],
  ['issued for another email', async (api) => (await createInvitation(api, email())).code],
];

for (const [kind, prepare] of badCodes) {
  test(`a ${kind} invitation code is rejected and creates no account`, async ({ page, request, newUser, trackEmail }) => {
    const api = await adminApi(request, newUser);
    const to = email();
    trackEmail(to);
    const code = await prepare(api, to, newUser);

    await fillAndSubmit(page, { code, email: to });

    await expect(page.getByTestId('register-error-summary')).toBeVisible();
    await expect(page).toHaveURL(/\/register/);
    expect(await userExists(to)).toBe(false);
  });
}

const fieldRules: [string, Partial<Form>, string][] = [
  ['a 1-character name', { fullName: 'A' }, 'Full name must be between 2 and 100 characters.'],
  ['an email over 254 characters', { email: `e2e-${'a'.repeat(250)}@example.test` }, 'Enter a valid email address of at most 254 characters.'],
  ['a password under 12 characters', { password: 'Aa1!aaaaaaa' }, 'The password does not meet the requirements below.'],
  ['a password over 128 characters', { password: `Aa1!${'a'.repeat(125)}` }, 'The password does not meet the requirements below.'],
  ['a password without an upper-case letter', { password: 'aa1!aaaaaaaaaa' }, 'The password does not meet the requirements below.'],
  ['a password without a lower-case letter', { password: 'AA1!AAAAAAAAAA' }, 'The password does not meet the requirements below.'],
  ['a password without a digit', { password: 'Aaa!aaaaaaaaaa' }, 'The password does not meet the requirements below.'],
  ['a password without a symbol', { password: 'Aaa1aaaaaaaaaa' }, 'The password does not meet the requirements below.'],
  ['a different confirmation', { password: validPassword(), confirm: 'Different-1234!' }, 'Passwords do not match.'],
];

for (const [label, change, message] of fieldRules) {
  test(`${label} is rejected with a message`, async ({ page }) => {
    await fillAndSubmit(page, { code: 'ANY-CODE', email: email(), ...change });

    await expect(page.getByText(message)).toBeVisible();
    await expect(page).toHaveURL(/\/register/);
  });
}

test('the full name field does not accept more than 100 characters', async ({ page }) => {
  await page.getByLabel('Full name').fill('N'.repeat(101));

  await expect(page.getByLabel('Full name')).toHaveValue('N'.repeat(100));
});

test('the same email in a different case is rejected as a duplicate', async ({ page, request, newUser, trackEmail }) => {
  const api = await adminApi(request, newUser);
  const to = email();
  trackEmail(to);
  const first = await createInvitation(api, to);
  const second = await createInvitation(api, to);
  expect((await registerViaApi(request, first.code, to)).ok()).toBe(true);

  await fillAndSubmit(page, { code: second.code, email: `  ${to.toUpperCase()}  ` });

  await expect(page.getByTestId('register-error-summary')).toBeVisible();
  await expect(page).toHaveURL(/\/register/);
});

test('two registrations with one code at the same time: exactly one succeeds', async ({ request, newUser, trackEmail }) => {
  const api = await adminApi(request, newUser);
  const to = email();
  trackEmail(to);
  const invitation = await createInvitation(api, to);

  const results = await Promise.all([
    registerViaApi(request, invitation.code, to),
    registerViaApi(request, invitation.code, to),
  ]);

  expect(results.filter((r) => r.ok())).toHaveLength(1);
});
