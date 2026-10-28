import { randomUUID } from 'node:crypto';
import type { APIRequestContext } from '@playwright/test';
import { test, expect } from '../fixtures';
import { expectPage, expectProblem } from '../contract';

const REASON = 'Suspended by an automated test.';

async function details(adminApi: APIRequestContext, id: string) {
  return (await adminApi.get(`/api/admin/users/${id}`)).json();
}

async function suspend(adminApi: APIRequestContext, id: string) {
  const { version } = await details(adminApi, id);
  return adminApi.post(`/api/admin/users/${id}/suspend`, { data: { reason: REASON, version } });
}

test.describe('GET /api/admin/users', () => {
  test('finds an author by email, without any password data', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const response = await adminApi.get(`/api/admin/users?search=${author.email}`);
    const body = await expectPage(response);
    expect(body.totalCount).toBe(1);
    expect(body.items[0]).toMatchObject({ id: author.id, email: author.email, status: 'Active' });
    expect(JSON.stringify(body).toLowerCase()).not.toContain('password');
  });

  test('search ignores case', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const body = await expectPage(await adminApi.get(`/api/admin/users?search=${author.email.toUpperCase()}`));
    expect(body.totalCount).toBe(1);
  });

  test('filters by status', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    await suspend(adminApi, author.id);

    const suspended = await expectPage(await adminApi.get(`/api/admin/users?search=${author.email}&status=Suspended`));
    const active = await expectPage(await adminApi.get(`/api/admin/users?search=${author.email}&status=Active`));
    expect(suspended.totalCount).toBe(1);
    expect(active.totalCount).toBe(0);
  });

  test('pages the results', async ({ adminApi, newAuthor }) => {
    await newAuthor();
    await newAuthor();
    const body = await expectPage(await adminApi.get('/api/admin/users?search=@example.test&pageSize=1&page=2'));
    expect(body).toMatchObject({ page: 2, pageSize: 1 });
    expect(body.items).toHaveLength(1);
    expect(body.totalCount).toBeGreaterThanOrEqual(2);
  });

  test('administrators are not listed', async ({ adminApi }) => {
    const body = await expectPage(await adminApi.get(`/api/admin/users?search=${process.env.API_ADMIN_EMAIL}`));
    expect(body.totalCount).toBe(0);
  });

  for (const query of ['status=Bogus', 'sort=bogus', 'page=0', 'pageSize=101']) {
    test(`${query} returns 400`, async ({ adminApi }) => {
      await expectProblem(await adminApi.get(`/api/admin/users?${query}`), 400);
    });
  }
});

test.describe('GET /api/admin/users/{id}', () => {
  test('returns the author with zero article counts', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const body = await details(adminApi, author.id);
    expect(body).toMatchObject({
      id: author.id,
      email: author.email,
      role: 'Author',
      status: 'Active',
      articleCounts: { draft: 0, pendingReview: 0, published: 0, rejected: 0 },
    });
  });

  test('an unknown id returns 404', async ({ adminApi }) => {
    await expectProblem(await adminApi.get(`/api/admin/users/${randomUUID()}`), 404);
  });

  test('an administrator id returns 404', async ({ adminApi }) => {
    const admin = await (await adminApi.get('/api/auth/me')).json();
    await expectProblem(await adminApi.get(`/api/admin/users/${admin.id}`), 404);
  });
});

test.describe('PUT /api/admin/users/{id}/profile', () => {
  test('updates the name and biography and moves the version', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const before = await details(adminApi, author.id);

    const response = await adminApi.put(`/api/admin/users/${author.id}/profile`, {
      data: { fullName: '  New Name  ', bio: 'New biography', version: before.version },
    });
    expect(response.status()).toBe(200);
    const after = await response.json();
    expect(after).toMatchObject({ fullName: 'New Name', bio: 'New biography' });
    expect(after.version).not.toBe(before.version);
  });

  test('email, role and status in the body are ignored', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const { version } = await details(adminApi, author.id);

    const response = await adminApi.put(`/api/admin/users/${author.id}/profile`, {
      data: { fullName: 'Same Person', version, email: 'other@example.test', role: 'Administrator', status: 'Suspended' },
    });

    expect(await response.json()).toMatchObject({ email: author.email, role: 'Author', status: 'Active' });
  });

  const invalid: [string, Record<string, string>, string][] = [
    ['name of 1 character', { fullName: 'A' }, 'fullName'],
    ['name of 101 characters', { fullName: 'A'.repeat(101) }, 'fullName'],
    ['biography of 1001 characters', { fullName: 'Valid Name', bio: 'b'.repeat(1001) }, 'bio'],
  ];
  for (const [name, change, field] of invalid) {
    test(`${name} returns 400 on ${field}`, async ({ adminApi, newAuthor }) => {
      const author = await newAuthor();
      const { version } = await details(adminApi, author.id);
      const body = await expectProblem(
        await adminApi.put(`/api/admin/users/${author.id}/profile`, { data: { ...change, version } }),
        400,
      );
      expect(body.errors[field]).toBeDefined();
    });
  }

  test('a stale version returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const { version } = await details(adminApi, author.id);
    await adminApi.put(`/api/admin/users/${author.id}/profile`, { data: { fullName: 'First Edit', version } });

    await expectProblem(
      await adminApi.put(`/api/admin/users/${author.id}/profile`, { data: { fullName: 'Second Edit', version } }),
      409,
    );
  });

  test('a missing version returns 400', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await adminApi.put(`/api/admin/users/${author.id}/profile`, { data: { fullName: 'No Version' } }), 400);
  });
});

test.describe('POST /api/admin/users/{id}/suspend', () => {
  test('suspends the author and signs them out immediately', async ({ request, adminApi, newAuthor }) => {
    const author = await newAuthor();
    const response = await suspend(adminApi, author.id);
    expect(response.status()).toBe(200);
    expect(await response.json()).toMatchObject({ status: 'Suspended', suspensionReason: REASON });

    await expectProblem(await author.api.get('/api/auth/me'), 401);
    const login = await request.post('/api/auth/login', {
      data: { email: author.email, password: author.password, portal: 'Author' },
    });
    expect(login.status()).toBe(401);
  });

  for (const [name, reason] of [['missing', undefined], ['9 characters', 'too short'], ['501 characters', 'r'.repeat(501)]] as const) {
    test(`a ${name} reason returns 400`, async ({ adminApi, newAuthor }) => {
      const author = await newAuthor();
      const { version } = await details(adminApi, author.id);
      const body = await expectProblem(
        await adminApi.post(`/api/admin/users/${author.id}/suspend`, { data: { reason, version } }),
        400,
      );
      expect(body.errors.reason).toBeDefined();
    });
  }

  test('suspending twice returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    await suspend(adminApi, author.id);
    await expectProblem(await suspend(adminApi, author.id), 409);
  });

  test('a stale version returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const { version } = await details(adminApi, author.id);
    await expectProblem(
      await adminApi.post(`/api/admin/users/${author.id}/suspend`, { data: { reason: REASON, version: version + 1 } }),
      409,
    );
  });

  test('an unknown id returns 404', async ({ adminApi }) => {
    await expectProblem(
      await adminApi.post(`/api/admin/users/${randomUUID()}/suspend`, { data: { reason: REASON, version: 1 } }),
      404,
    );
  });
});

test.describe('POST /api/admin/users/{id}/reactivate', () => {
  test('makes the author Active again and allows signing in', async ({ request, adminApi, newAuthor }) => {
    const author = await newAuthor();
    await suspend(adminApi, author.id);
    const { version } = await details(adminApi, author.id);

    const response = await adminApi.post(`/api/admin/users/${author.id}/reactivate`, { data: { version } });
    expect(response.status()).toBe(200);
    expect(await response.json()).toMatchObject({ status: 'Active', suspensionReason: null });

    const login = await request.post('/api/auth/login', {
      data: { email: author.email, password: author.password, portal: 'Author' },
    });
    expect(login.status()).toBe(200);
  });

  test('reactivating an Active author returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const { version } = await details(adminApi, author.id);
    await expectProblem(await adminApi.post(`/api/admin/users/${author.id}/reactivate`, { data: { version } }), 409);
  });

  test('a stale version returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    await suspend(adminApi, author.id);
    const { version } = await details(adminApi, author.id);
    await expectProblem(await adminApi.post(`/api/admin/users/${author.id}/reactivate`, { data: { version: version + 1 } }), 409);
  });
});

test.describe('access', () => {
  test('without a token every route returns 401', async ({ request }) => {
    const id = randomUUID();
    await expectProblem(await request.get('/api/admin/users'), 401);
    await expectProblem(await request.get(`/api/admin/users/${id}`), 401);
    await expectProblem(await request.put(`/api/admin/users/${id}/profile`, { data: { fullName: 'Nobody', version: 1 } }), 401);
    await expectProblem(await request.post(`/api/admin/users/${id}/suspend`, { data: { reason: REASON, version: 1 } }), 401);
    await expectProblem(await request.post(`/api/admin/users/${id}/reactivate`, { data: { version: 1 } }), 401);
  });

  test('an Author token returns 403 on every route', async ({ newAuthor }) => {
    const author = await newAuthor();
    const { api, id } = author;
    await expectProblem(await api.get('/api/admin/users'), 403);
    await expectProblem(await api.get(`/api/admin/users/${id}`), 403);
    await expectProblem(await api.put(`/api/admin/users/${id}/profile`, { data: { fullName: 'Nobody', version: 1 } }), 403);
    await expectProblem(await api.post(`/api/admin/users/${id}/suspend`, { data: { reason: REASON, version: 1 } }), 403);
    await expectProblem(await api.post(`/api/admin/users/${id}/reactivate`, { data: { version: 1 } }), 403);
  });
});

test.describe('audit', () => {
  test('profile edit, suspension and reactivation are recorded', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    let { version } = await details(adminApi, author.id);
    version = (await (await adminApi.put(`/api/admin/users/${author.id}/profile`, { data: { fullName: 'Audited Name', version } })).json()).version;
    version = (await (await adminApi.post(`/api/admin/users/${author.id}/suspend`, { data: { reason: REASON, version } })).json()).version;
    await adminApi.post(`/api/admin/users/${author.id}/reactivate`, { data: { version } });

    const events = (await (await adminApi.get('/api/admin/audit-events?pageSize=100')).json()).items;
    const actions = events.filter((e: { entityId: string }) => e.entityId === author.id).map((e: { action: string }) => e.action);
    expect(actions).toEqual(expect.arrayContaining(['AdminProfileEdited', 'UserSuspended', 'UserReactivated']));
  });
});
