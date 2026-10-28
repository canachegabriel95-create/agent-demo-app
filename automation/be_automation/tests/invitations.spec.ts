import { randomUUID } from 'node:crypto';
import { test, expect, createInvitation } from '../fixtures';
import { expectPage, expectProblem } from '../contract';
import { expireInvitation } from '../db';

const newEmail = (tag = 'inv') => `${tag}-${randomUUID()}@example.test`;
const DAY_MS = 24 * 60 * 60 * 1000;

function validForDays(invitation: { createdAt: string; expiresAt: string }) {
  return Math.round((Date.parse(invitation.expiresAt) - Date.parse(invitation.createdAt)) / DAY_MS);
}

test.describe('POST /api/admin/invitations', () => {
  test('creates an Available invitation valid for 7 days by default', async ({ adminApi }) => {
    const email = newEmail();
    const response = await adminApi.post('/api/admin/invitations', { data: { email } });
    expect(response.status()).toBe(201);
    const body = await response.json();
    expect(body).toMatchObject({ email, status: 'Available' });
    expect(validForDays(body)).toBe(7);
    expect(response.headers()['location']).toContain(body.id);
  });

  for (const days of [1, 30]) {
    test(`accepts validForDays=${days}`, async ({ adminApi }) => {
      const response = await adminApi.post('/api/admin/invitations', { data: { email: newEmail(), validForDays: days } });
      expect(response.status()).toBe(201);
      expect(validForDays(await response.json())).toBe(days);
    });
  }

  for (const days of [0, 31]) {
    test(`validForDays=${days} returns 400`, async ({ adminApi }) => {
      const body = await expectProblem(
        await adminApi.post('/api/admin/invitations', { data: { email: newEmail(), validForDays: days } }),
        400,
      );
      expect(body.errors.validForDays).toBeDefined();
    });
  }

  test('an invalid email returns 400', async ({ adminApi }) => {
    const body = await expectProblem(await adminApi.post('/api/admin/invitations', { data: { email: 'nope' } }), 400);
    expect(body.errors.email).toBeDefined();
  });

  test('an email that already belongs to a user returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await adminApi.post('/api/admin/invitations', { data: { email: author.email } }), 409);
  });

  test('several Available invitations may exist for one email', async ({ adminApi }) => {
    const email = newEmail();
    await createInvitation(adminApi, email);
    const second = await adminApi.post('/api/admin/invitations', { data: { email: ` ${email.toUpperCase()} ` } });
    expect(second.status()).toBe(201);
  });
});

test.describe('GET /api/admin/invitations', () => {
  test('pages the results and reports the total', async ({ adminApi }) => {
    const tag = randomUUID();
    for (let i = 0; i < 3; i++) await createInvitation(adminApi, newEmail(tag));

    const first = await expectPage(await adminApi.get(`/api/admin/invitations?email=${tag}&pageSize=2`));
    expect(first).toMatchObject({ page: 1, pageSize: 2, totalCount: 3 });
    expect(first.items).toHaveLength(2);

    const second = await expectPage(await adminApi.get(`/api/admin/invitations?email=${tag}&pageSize=2&page=2`));
    expect(second.items).toHaveLength(1);
  });

  test('searches by part of the email, ignoring case', async ({ adminApi }) => {
    const tag = randomUUID();
    const email = newEmail(tag);
    await createInvitation(adminApi, email);

    const body = await expectPage(await adminApi.get(`/api/admin/invitations?email=${tag.toUpperCase()}`));
    expect(body.items.map((i: { email: string }) => i.email)).toEqual([email]);
  });

  test('filters by status', async ({ adminApi, newAuthor }) => {
    const tag = randomUUID();
    const available = await createInvitation(adminApi, newEmail(tag));
    const revoked = await createInvitation(adminApi, newEmail(tag));
    await adminApi.post(`/api/admin/invitations/${revoked.id}/revoke`, { data: { version: revoked.version } });
    const expired = await createInvitation(adminApi, newEmail(tag));
    await expireInvitation(expired.id);
    const author = await newAuthor();
    const used = await (await adminApi.get(`/api/admin/invitations?email=${author.email}`)).json();

    const idsWith = async (status: string, query: string = tag) =>
      (await (await adminApi.get(`/api/admin/invitations?email=${query}&status=${status}`)).json()).items.map(
        (i: { id: string }) => i.id,
      );

    expect(await idsWith('Available')).toEqual([available.id]);
    expect(await idsWith('Revoked')).toEqual([revoked.id]);
    expect(await idsWith('Expired')).toEqual([expired.id]);
    expect(await idsWith('Used', author.email)).toEqual([used.items[0].id]);
  });

  test('an unknown status returns 400', async ({ adminApi }) => {
    await expectProblem(await adminApi.get('/api/admin/invitations?status=Bogus'), 400);
  });
});

test.describe('GET /api/admin/invitations/{id}', () => {
  test('returns the invitation', async ({ adminApi }) => {
    const email = newEmail();
    const { id } = await createInvitation(adminApi, email);
    const body = await (await adminApi.get(`/api/admin/invitations/${id}`)).json();
    expect(body).toMatchObject({ id, email, status: 'Available' });
  });

  test('an unknown id returns 404', async ({ adminApi }) => {
    await expectProblem(await adminApi.get(`/api/admin/invitations/${randomUUID()}`), 404);
  });
});

test.describe('POST /api/admin/invitations/{id}/revoke', () => {
  test('revokes an Available invitation', async ({ adminApi }) => {
    const { id, version } = await createInvitation(adminApi, newEmail());
    const response = await adminApi.post(`/api/admin/invitations/${id}/revoke`, { data: { version } });
    expect(response.status()).toBe(200);
    expect((await response.json()).status).toBe('Revoked');
  });

  test('revoking twice returns 409', async ({ adminApi }) => {
    const { id, version } = await createInvitation(adminApi, newEmail());
    const revoked = await (await adminApi.post(`/api/admin/invitations/${id}/revoke`, { data: { version } })).json();
    await expectProblem(await adminApi.post(`/api/admin/invitations/${id}/revoke`, { data: { version: revoked.version } }), 409);
  });

  test('a Used invitation returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const used = (await (await adminApi.get(`/api/admin/invitations?email=${author.email}`)).json()).items[0];
    await expectProblem(await adminApi.post(`/api/admin/invitations/${used.id}/revoke`, { data: { version: used.version } }), 409);
  });

  test('a stale version returns 409 and leaves the invitation Available', async ({ adminApi }) => {
    const { id, version } = await createInvitation(adminApi, newEmail());
    await expectProblem(await adminApi.post(`/api/admin/invitations/${id}/revoke`, { data: { version: version + 1 } }), 409);
    expect((await (await adminApi.get(`/api/admin/invitations/${id}`)).json()).status).toBe('Available');
  });

  test('a missing version returns 400', async ({ adminApi }) => {
    const { id } = await createInvitation(adminApi, newEmail());
    await expectProblem(await adminApi.post(`/api/admin/invitations/${id}/revoke`, { data: {} }), 400);
  });

  test('an unknown id returns 404', async ({ adminApi }) => {
    await expectProblem(await adminApi.post(`/api/admin/invitations/${randomUUID()}/revoke`, { data: { version: 1 } }), 404);
  });
});

test.describe('access', () => {
  test('without a token every route returns 401', async ({ request }) => {
    await expectProblem(await request.get('/api/admin/invitations'), 401);
    await expectProblem(await request.post('/api/admin/invitations', { data: { email: newEmail() } }), 401);
  });

  test('an Author token returns 403', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await author.api.get('/api/admin/invitations'), 403);
    await expectProblem(await author.api.post('/api/admin/invitations', { data: { email: newEmail() } }), 403);
  });
});

test.describe('audit', () => {
  async function auditActionsFor(adminApi: import('@playwright/test').APIRequestContext, entityId: string) {
    const page = await (await adminApi.get('/api/admin/audit-events?pageSize=100')).json();
    return page.items.filter((e: { entityId: string }) => e.entityId === entityId).map((e: { action: string }) => e.action);
  }

  test('creating and revoking an invitation are recorded', async ({ adminApi }) => {
    const { id, version } = await createInvitation(adminApi, newEmail());
    await adminApi.post(`/api/admin/invitations/${id}/revoke`, { data: { version } });

    expect((await auditActionsFor(adminApi, id)).sort()).toEqual(['InvitationCreated', 'InvitationRevoked']);
  });
});
