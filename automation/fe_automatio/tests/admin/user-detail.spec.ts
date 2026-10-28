import { randomUUID } from 'node:crypto';
import { ok } from '../support/api';
import { apiLogin, apiMeStatus } from '../support/auth';
import { test, expect } from '../support/fixtures';

interface UserDetails {
  fullName: string;
  bio: string | null;
  status: string;
  version: number;
}

const dialog = (page: import('@playwright/test').Page) => page.getByRole('dialog');

test('editing the name and biography saves them; email and role are read-only', async ({ page, admin, newUser }) => {
  void admin;
  const author = await newUser({ role: 'Author' });
  await page.goto(`/users/${author.id}`);

  await page.getByTestId('user-edit-open').click();
  await expect(dialog(page).getByLabel('Email')).toHaveCount(0);
  await expect(dialog(page).getByLabel('Role')).toHaveCount(0);
  await dialog(page).getByLabel('Display name').fill('Renamed Author');
  await dialog(page).getByLabel('Biography').fill('A new biography.');
  await page.getByTestId('profile-save').click();

  await expect(page.getByTestId('user-full-name')).toHaveText('Renamed Author');
  await expect(page.getByTestId('user-bio')).toHaveText('A new biography.');
  await expect(page.getByTestId('user-email')).toHaveText(author.email);
});

test('the name is trimmed when saved', async ({ page, admin, newUser }) => {
  const author = await newUser({ role: 'Author' });
  await page.goto(`/users/${author.id}`);

  await page.getByTestId('user-edit-open').click();
  await dialog(page).getByLabel('Display name').fill('   Trimmed Name   ');
  await page.getByTestId('profile-save').click();

  await expect(dialog(page)).toBeHidden();
  const saved = await ok<UserDetails>(await admin.api.get(`/api/admin/users/${author.id}`));
  expect(saved.fullName).toBe('Trimmed Name');
});

for (const [label, field, value] of [
  ['a 1-character name', 'Display name', 'A'],
  ['a name over 100 characters', 'Display name', 'N'.repeat(101)],
  ['a biography over 1000 characters', 'Biography', 'b'.repeat(1001)],
] as const) {
  test(`${label} is rejected`, async ({ page, admin, newUser }) => {
    void admin;
    const author = await newUser({ role: 'Author' });
    await page.goto(`/users/${author.id}`);
    await page.getByTestId('user-edit-open').click();

    await dialog(page).getByLabel(field).fill(value);
    await page.getByTestId('profile-save').click();

    await expect(dialog(page).getByLabel(field)).toHaveAttribute('aria-invalid', 'true');
  });
}

test('suspending needs a reason of at least 10 characters and then marks the author Suspended', async ({
  page,
  admin,
  newUser,
}) => {
  void admin;
  const author = await newUser({ role: 'Author' });
  await page.goto(`/users/${author.id}`);
  await page.getByTestId('user-suspend-open').click();

  await dialog(page).getByLabel('Reason').fill('too short');
  await page.getByTestId('suspend-confirm').click();
  await expect(dialog(page).getByLabel('Reason')).toHaveAttribute('aria-invalid', 'true');

  await dialog(page).getByLabel('Reason').fill('Repeated policy violations.');
  await page.getByTestId('suspend-confirm').click();

  await expect(page.getByTestId('user-status')).toHaveText('Suspended');
  await expect(page.getByTestId('user-suspension-reason')).toHaveText('Repeated policy violations.');
});

test("a suspended author's existing session is rejected on the next request", async ({ page, request, admin, newUser }) => {
  void admin;
  const author = await newUser({ role: 'Author' });
  const token = await apiLogin(request, author, 'Author');
  expect(await apiMeStatus(request, token)).toBe(200);
  await page.goto(`/users/${author.id}`);

  await page.getByTestId('user-suspend-open').click();
  await dialog(page).getByLabel('Reason').fill('Repeated policy violations.');
  await page.getByTestId('suspend-confirm').click();
  await expect(page.getByTestId('user-status')).toHaveText('Suspended');

  expect(await apiMeStatus(request, token)).toBe(401);
});

test('reactivating restores the Active status', async ({ page, admin, newUser }) => {
  void admin;
  const author = await newUser({ role: 'Author', status: 'Suspended' });
  await page.goto(`/users/${author.id}`);
  await expect(page.getByTestId('user-status')).toHaveText('Suspended');

  await page.getByTestId('user-reactivate-open').click();
  await page.getByTestId('reactivate-confirm').click();

  await expect(page.getByTestId('user-status')).toHaveText('Active');
});

test('saving over a newer version shows the conflict message and keeps the other change', async ({
  page,
  admin,
  newUser,
}) => {
  const author = await newUser({ role: 'Author' });
  await page.goto(`/users/${author.id}`);
  await page.getByTestId('user-edit-open').click();
  const current = await ok<UserDetails>(await admin.api.get(`/api/admin/users/${author.id}`));
  await ok(
    await admin.api.put(`/api/admin/users/${author.id}/profile`, {
      fullName: 'Changed Elsewhere',
      bio: '',
      version: current.version,
    }),
  );

  await dialog(page).getByLabel('Display name').fill('My Late Edit');
  await page.getByTestId('profile-save').click();

  await expect(page.getByTestId('profile-conflict')).toBeVisible();
  const saved = await ok<UserDetails>(await admin.api.get(`/api/admin/users/${author.id}`));
  expect(saved.fullName).toBe('Changed Elsewhere');
});

test('an unknown author id shows the not-found state', async ({ page, admin }) => {
  void admin;

  await page.goto(`/users/${randomUUID()}`);

  await expect(page.getByRole('heading', { name: 'Author not found' })).toBeVisible();
});
