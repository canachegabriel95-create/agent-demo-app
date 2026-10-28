import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { createInvitation } from '../support/api';
import { test, expect } from '../support/fixtures';

const email = () => `e2e-${randomUUID()}@example.test`;

async function openDialog(page: Page) {
  await page.goto('/invitations');
  await page.getByTestId('invitation-create-open').click();
}

async function submit(page: Page, to: string, days?: string) {
  await page.getByRole('dialog').getByLabel('Email').fill(to);
  if (days !== undefined) await page.getByRole('dialog').getByLabel('Valid for (days)').fill(days);
  await page.getByTestId('invitation-create-submit').click();
}

test('creating with the default 7 days shows the code once and lists the invitation', async ({ page, admin }) => {
  void admin;
  const to = email();
  await openDialog(page);
  await expect(page.getByRole('dialog').getByLabel('Valid for (days)')).toHaveValue('7');

  await submit(page, to);

  await expect(page.getByTestId('invitation-created-code')).toBeVisible();
  await page.getByTestId('invitation-create-done').click();
  await expect(page.getByRole('row', { name: new RegExp(to) })).toBeVisible();
});

for (const days of ['1', '30']) {
  test(`${days} day(s) of validity is accepted`, async ({ page, admin }) => {
    void admin;
    await openDialog(page);

    await submit(page, email(), days);

    await expect(page.getByTestId('invitation-created-code')).toBeVisible();
  });
}

for (const days of ['0', '31']) {
  test(`${days} day(s) of validity is rejected`, async ({ page, admin }) => {
    void admin;
    await openDialog(page);

    await submit(page, email(), days);

    await expect(page.getByText('Enter a whole number of days between 1 and 30.')).toBeVisible();
    await expect(page.getByTestId('invitation-created-code')).toBeHidden();
  });
}

test('a missing email is rejected', async ({ page, admin }) => {
  void admin;
  await openDialog(page);

  await page.getByTestId('invitation-create-submit').click();

  await expect(page.getByText('Email is required.')).toBeVisible();
});

test('an invalid email is rejected', async ({ page, admin }) => {
  void admin;
  await openDialog(page);

  await submit(page, 'not-an-email');

  await expect(page.getByText('Enter a valid email address.')).toBeVisible();
});

test('an email longer than 254 characters is rejected', async ({ page, admin }) => {
  void admin;
  await openDialog(page);

  await submit(page, `e2e-${'a'.repeat(250)}@example.test`);

  await expect(page.getByRole('dialog').getByLabel('Email')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByTestId('invitation-created-code')).toBeHidden();
});

test('a second active invitation for the same email is rejected, ignoring case and spaces', async ({ page, admin }) => {
  // KNOWN DEFECT (F17.6 AC4): the API answers 201 for a second active invitation to the same email, so nothing is
  // rejected. Expected to fail until that is fixed; Playwright then reports it as unexpectedly passing.
  test.fail();
  const to = email();
  await createInvitation(admin.api, to);
  await openDialog(page);

  await submit(page, `  ${to.toUpperCase()}  `);

  await expect(page.getByRole('dialog').getByLabel('Email')).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByTestId('invitation-created-code')).toBeHidden();
});

test('cancel closes the dialog without creating an invitation', async ({ page, admin }) => {
  void admin;
  const to = email();
  await openDialog(page);
  await page.getByRole('dialog').getByLabel('Email').fill(to);

  await page.getByRole('button', { name: 'Cancel' }).click();

  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByRole('row', { name: new RegExp(to) })).toHaveCount(0);
});
