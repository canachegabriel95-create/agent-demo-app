import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { apiAs, approve, createInvitation, createSubmittedArticle, ok } from '../support/api';
import { apiLogin } from '../support/auth';
import { test, expect } from '../support/fixtures';

const rows = (page: Page) => page.getByTestId('audit-row');
const AUDIT_LIST = /\/api\/admin\/audit-events(\?.*)?$/;

/** One invitation, one suspension and one approval, all made by the admin through the API. */
async function makeEvents(
  request: Parameters<typeof apiLogin>[0],
  admin: { api: ReturnType<typeof apiAs> },
  author: Parameters<typeof apiLogin>[1],
) {
  const invitation = await createInvitation(admin.api, `e2e-${randomUUID()}@example.test`);
  const authorApi = apiAs(request, await apiLogin(request, author, 'Author'));
  await approve(admin.api, await createSubmittedArticle(authorApi));
  const details = await ok<{ version: number }>(await admin.api.get(`/api/admin/users/${author.id}`));
  await ok(await admin.api.post(`/api/admin/users/${author.id}/suspend`, { reason: 'Audit test suspension.', version: details.version }));
  return invitation;
}

test('shows events for an invitation created, a user suspended and an article approved', async ({
  page,
  request,
  admin,
  newUser,
}) => {
  await makeEvents(request, admin, await newUser({ role: 'Author' }));

  await page.goto('/audit');

  for (const action of ['InvitationCreated', 'UserSuspended', 'ArticleApproved']) {
    await expect(rows(page).filter({ hasText: action })).toHaveCount(1);
  }
});

test('filters by event type', async ({ page, request, admin, newUser }) => {
  await makeEvents(request, admin, await newUser({ role: 'Author' }));
  await page.goto('/audit');
  await expect(rows(page).filter({ hasText: 'InvitationCreated' })).toHaveCount(1);

  await page.getByRole('checkbox', { name: 'UserSuspended' }).click();

  await expect(rows(page).filter({ hasText: 'UserSuspended' })).toHaveCount(1);
  await expect(rows(page).filter({ hasText: 'InvitationCreated' })).toHaveCount(0);
});

test('filters by date', async ({ page, request, admin, newUser }) => {
  await makeEvents(request, admin, await newUser({ role: 'Author' }));
  const day = (offset: number) => new Date(Date.now() + offset * 86_400_000).toISOString().slice(0, 10);
  await page.goto('/audit');
  await expect(rows(page).first()).toBeVisible();

  await page.getByLabel('From').fill(day(2));
  await expect(page.getByText('No audit events match the filters')).toBeVisible();

  await page.getByLabel('From').fill(day(-1));
  await expect(rows(page).first()).toBeVisible();
});

test('paginates 20 events per page', async ({ page, admin }) => {
  for (let i = 0; i < 21; i++) await createInvitation(admin.api, `e2e-${randomUUID()}@example.test`);

  await page.goto('/audit');
  await expect(rows(page)).toHaveCount(20);

  await page.getByRole('button', { name: 'Next page' }).click();
  await expect(rows(page).first()).toBeVisible();
  await expect(page.getByText('Page 2 of')).toBeVisible();
});

test('never shows a password, a token or a full invitation code', async ({ page, request, admin, newUser }) => {
  const invitation = await makeEvents(request, admin, await newUser({ role: 'Author' }));
  const response = page.waitForResponse(AUDIT_LIST);

  await page.goto('/audit');

  const body = await (await response).text();
  expect(body).not.toContain(invitation.code);
  expect(body).not.toMatch(/password|token/i);
  await expect(page.locator('body')).not.toContainText(invitation.code);
});

test('shows the empty state when there are no events', async ({ page, admin }) => {
  void admin;
  await page.route(AUDIT_LIST, (route) =>
    route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }),
  );

  await page.goto('/audit');

  await expect(page.getByText('No audit events yet')).toBeVisible();
});

test('shows a loading state until the events arrive', async ({ page, admin }) => {
  void admin;
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(AUDIT_LIST, async (route) => {
    await gate;
    await route.continue();
  });

  await page.goto('/audit');

  await expect(page.getByText('Loading the audit history…')).toBeVisible();
  release();
  await expect(page.getByText('Loading the audit history…')).toBeHidden();
});

test('shows an error state when the events cannot be loaded', async ({ page, admin }) => {
  void admin;
  await page.route(AUDIT_LIST, (route) => route.fulfill({ status: 500, json: { title: 'Server error' } }));

  await page.goto('/audit');

  await expect(page.getByRole('alert')).toContainText('The audit history could not be loaded');
});
