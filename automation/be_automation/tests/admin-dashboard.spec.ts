import type { APIRequestContext } from '@playwright/test';
import { test, expect, createInvitation } from '../fixtures';
import { expectProblem } from '../contract';
import { approveArticle, submittedArticle } from '../articles';

const dashboard = async (api: APIRequestContext) => (await api.get('/api/admin/dashboard')).json();

test.describe('GET /api/admin/dashboard', () => {
  test('returns the five counters as numbers', async ({ adminApi }) => {
    const response = await adminApi.get('/api/admin/dashboard');
    expect(response.status()).toBe(200);
    const body = await response.json();
    for (const key of ['activeAuthors', 'suspendedAuthors', 'availableInvitations', 'pendingReviews', 'publishedArticles']) {
      expect(typeof body[key]).toBe('number');
    }
  });

  test('a new author adds one active author', async ({ adminApi, newAuthor }) => {
    const before = await dashboard(adminApi);
    await newAuthor();
    const after = await dashboard(adminApi);
    expect(after.activeAuthors).toBe(before.activeAuthors + 1);
  });

  test('available invitations follow create and revoke', async ({ adminApi }) => {
    const before = await dashboard(adminApi);
    const invitation = await createInvitation(adminApi, `dash-${Date.now()}@example.test`);
    expect((await dashboard(adminApi)).availableInvitations).toBe(before.availableInvitations + 1);

    await adminApi.post(`/api/admin/invitations/${invitation.id}/revoke`, { data: { version: invitation.version } });
    expect((await dashboard(adminApi)).availableInvitations).toBe(before.availableInvitations);
  });

  test('a used invitation is no longer available', async ({ adminApi, newAuthor }) => {
    const before = await dashboard(adminApi);
    await newAuthor();
    expect((await dashboard(adminApi)).availableInvitations).toBe(before.availableInvitations);
  });

  test('pending reviews and published articles follow submit and approve', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const before = await dashboard(adminApi);

    const pending = await submittedArticle(author.api);
    const afterSubmit = await dashboard(adminApi);
    expect(afterSubmit.pendingReviews).toBe(before.pendingReviews + 1);

    await approveArticle(adminApi, pending);
    const afterApprove = await dashboard(adminApi);
    expect(afterApprove.pendingReviews).toBe(before.pendingReviews);
    expect(afterApprove.publishedArticles).toBe(before.publishedArticles + 1);
  });

  test('suspending and reactivating an author moves the author counters', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const before = await dashboard(adminApi);

    const { version } = await (await adminApi.get(`/api/admin/users/${author.id}`)).json();
    const suspended = await (await adminApi.post(`/api/admin/users/${author.id}/suspend`, {
      data: { reason: 'Suspended by an automated test.', version },
    })).json();
    const afterSuspend = await dashboard(adminApi);
    expect(afterSuspend).toMatchObject({
      activeAuthors: before.activeAuthors - 1,
      suspendedAuthors: before.suspendedAuthors + 1,
    });

    await adminApi.post(`/api/admin/users/${author.id}/reactivate`, { data: { version: suspended.version } });
    const afterReactivate = await dashboard(adminApi);
    expect(afterReactivate).toMatchObject({
      activeAuthors: before.activeAuthors,
      suspendedAuthors: before.suspendedAuthors,
    });
  });

  test('without a token returns 401, an Author token 403', async ({ request, newAuthor }) => {
    await expectProblem(await request.get('/api/admin/dashboard'), 401);
    const author = await newAuthor();
    await expectProblem(await author.api.get('/api/admin/dashboard'), 403);
  });
});
