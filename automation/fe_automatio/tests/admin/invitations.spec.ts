import { randomUUID } from 'node:crypto';
import { createInvitation, ok } from '../support/api';
import { setInvitationState } from '../support/db';
import { test, expect } from '../support/fixtures';

const email = () => `e2e-${randomUUID()}@example.test`;
const row = (page: import('@playwright/test').Page, id: string) => page.getByTestId(`invitation-row-${id}`);

test('lists invitations with the status Available, Used, Expired and Revoked', async ({ page, admin, newUser }) => {
  const author = await newUser({ role: 'Author' });
  const available = await createInvitation(admin.api, email());
  const used = await createInvitation(admin.api, email());
  const expired = await createInvitation(admin.api, email());
  const revoked = await createInvitation(admin.api, email());
  await setInvitationState(used.id, { usedBy: author.id });
  await setInvitationState(expired.id, { expired: true });
  await admin.api.post(`/api/admin/invitations/${revoked.id}/revoke`, { version: revoked.version });

  await page.goto('/invitations');

  await expect(row(page, available.id)).toContainText('Available');
  await expect(row(page, used.id)).toContainText('Used');
  await expect(row(page, expired.id)).toContainText('Expired');
  await expect(row(page, revoked.id)).toContainText('Revoked');
});

test('filters by status and searches by email', async ({ page, admin }) => {
  const available = await createInvitation(admin.api, email());
  const revoked = await createInvitation(admin.api, email());
  await admin.api.post(`/api/admin/invitations/${revoked.id}/revoke`, { version: revoked.version });
  await page.goto('/invitations');

  await page.getByLabel('Status').selectOption('Revoked');
  await expect(row(page, revoked.id)).toBeVisible();
  await expect(row(page, available.id)).toBeHidden();

  await page.getByLabel('Status').selectOption('');
  await page.getByLabel('Email').fill(available.email);
  await expect(row(page, available.id)).toBeVisible();
  await expect(row(page, revoked.id)).toBeHidden();
});

test('paginates 20 invitations per page', async ({ page, admin }) => {
  for (let i = 0; i < 21; i++) await createInvitation(admin.api, email());

  await page.goto('/invitations');
  await expect(page.getByTestId(/^invitation-row-/)).toHaveCount(20);

  await page.getByRole('button', { name: 'Next page' }).click();
  await expect(page.getByTestId(/^invitation-row-/)).toHaveCount(1);
});

test('revoking asks for confirmation and then marks the invitation Revoked', async ({ page, admin }) => {
  const invitation = await createInvitation(admin.api, email());
  await page.goto('/invitations');

  await page.getByTestId(`invitation-revoke-${invitation.id}`).click();
  await page.getByRole('button', { name: 'Cancel' }).click();
  await expect(row(page, invitation.id)).toContainText('Available');

  await page.getByTestId(`invitation-revoke-${invitation.id}`).click();
  await page.getByTestId('invitation-revoke-confirm').click();

  await expect(row(page, invitation.id)).toContainText('Revoked');
});

test('only Available invitations can be revoked', async ({ page, admin }) => {
  const revoked = await createInvitation(admin.api, email());
  await admin.api.post(`/api/admin/invitations/${revoked.id}/revoke`, { version: revoked.version });
  await page.goto('/invitations');

  await expect(row(page, revoked.id)).toBeVisible();
  await expect(page.getByTestId(`invitation-revoke-${revoked.id}`)).toHaveCount(0);
});

test('revoking an invitation that changed meanwhile shows the conflict message', async ({ page, admin }) => {
  const invitation = await createInvitation(admin.api, email());
  await page.goto('/invitations');
  await expect(row(page, invitation.id)).toBeVisible();
  await ok(await admin.api.post(`/api/admin/invitations/${invitation.id}/revoke`, { version: invitation.version }));

  await page.getByTestId(`invitation-revoke-${invitation.id}`).click();
  await page.getByTestId('invitation-revoke-confirm').click();

  await expect(page.getByTestId('invitations-notice')).toHaveText(
    'This invitation changed. Refresh to see the latest status.',
  );
  await expect(row(page, invitation.id)).toContainText('Revoked');
});

test('shows the empty state when nothing matches', async ({ page, admin }) => {
  void admin;
  await page.goto('/invitations');

  await page.getByLabel('Email').fill(`nobody-${randomUUID()}@example.test`);

  await expect(page.getByText('No invitations match the filters')).toBeVisible();
});

test('shows a loading state until the invitations arrive', async ({ page, admin }) => {
  void admin;
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(/\/api\/admin\/invitations(\?.*)?$/, async (route) => {
    await gate;
    await route.continue();
  });

  await page.goto('/invitations');

  await expect(page.getByText('Loading invitations…')).toBeVisible();
  release();
  await expect(page.getByText('Loading invitations…')).toBeHidden();
});

test('shows an error state when the invitations cannot be loaded', async ({ page, admin }) => {
  void admin;
  await page.route(/\/api\/admin\/invitations(\?.*)?$/, (route) => route.fulfill({ status: 500, json: { title: 'Server error' } }));

  await page.goto('/invitations');

  await expect(page.getByRole('alert')).toContainText('The invitations could not be loaded');
});
