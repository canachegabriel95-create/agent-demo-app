import { randomUUID } from 'node:crypto';
import { test, expect } from '../fixtures';
import { expectProblem } from '../contract';

test.describe('health', () => {
  test('/health is Healthy and includes the database check', async ({ request }) => {
    const body = await (await request.get('/health')).json();
    expect(body.status).toBe('Healthy');
    expect(body.checks).toContainEqual({ name: 'database', status: 'Healthy' });
  });
});

test.describe('X-Correlation-Id', () => {
  test('is generated when the client sends none', async ({ request }) => {
    const response = await request.get('/health');
    expect(response.headers()['x-correlation-id']).toBeTruthy();
  });

  test('a valid client id is echoed', async ({ request }) => {
    const id = `e2e-${randomUUID()}`;
    const response = await request.get('/health', { headers: { 'X-Correlation-Id': id } });
    expect(response.headers()['x-correlation-id']).toBe(id);
  });

  test('an invalid client id is replaced', async ({ request }) => {
    const response = await request.get('/health', { headers: { 'X-Correlation-Id': 'not valid!' } });
    expect(response.headers()['x-correlation-id']).not.toBe('not valid!');
  });
});

test.describe('ProblemDetails', () => {
  test('400 on invalid input', async ({ adminApi }) => {
    const body = await expectProblem(await adminApi.post('/api/admin/invitations', { data: { email: 'not-an-email' } }), 400);
    expect(body.errors.email).toBeDefined();
  });

  test('401 without a token', async ({ request }) => {
    await expectProblem(await request.get('/api/admin/invitations'), 401);
  });

  test('403 for the wrong role', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await author.api.get('/api/admin/invitations'), 403);
  });

  test('404 for a missing resource', async ({ adminApi }) => {
    await expectProblem(await adminApi.get(`/api/admin/invitations/${randomUUID()}`), 404);
  });

  test('409 on a conflict', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await adminApi.post('/api/admin/invitations', { data: { email: author.email } }), 409);
  });

  test('a signed-in request to an unknown route gets a 404 problem', async ({ adminApi }) => {
    await expectProblem(await adminApi.get(`/api/${randomUUID()}`), 404);
  });
});

test.describe('JSON format', () => {
  test('property names are camelCase and enums are strings', async ({ adminApi }) => {
    const email = `invite-${randomUUID()}@example.test`;
    const response = await adminApi.post('/api/admin/invitations', { data: { email } });
    expect(response.status()).toBe(201);
    const body = await response.json();
    expect(Object.keys(body)).toEqual(expect.arrayContaining(['createdAt', 'expiresAt', 'createdBy']));
    expect(body.status).toBe('Available');
  });
});

test.describe('pagination bounds', () => {
  for (const query of ['page=0', 'pageSize=0', 'pageSize=101']) {
    test(`${query} returns 400`, async ({ adminApi }) => {
      await expectProblem(await adminApi.get(`/api/admin/invitations?${query}`), 400);
    });
  }
});

test.describe('Swagger', () => {
  test('lists every endpoint of the API', async ({ request }) => {
    const { paths } = await (await request.get('/swagger/v1/swagger.json')).json();
    const listed = Object.entries(paths).flatMap(([path, ops]) =>
      Object.keys(ops as object).map((method) => `${method.toUpperCase()} ${path}`),
    );
    expect(listed).toEqual(expect.arrayContaining(expectedEndpoints));
  });
});

const expectedEndpoints = [
  'GET /health',
  'POST /api/auth/login', 'POST /api/auth/logout', 'GET /api/auth/me',
  'POST /api/registration', 'POST /api/registration/validate-invitation',
  'GET /api/admin/invitations', 'POST /api/admin/invitations', 'GET /api/admin/invitations/{id}',
  'POST /api/admin/invitations/{id}/revoke',
  'GET /api/admin/users', 'GET /api/admin/users/{id}', 'PUT /api/admin/users/{id}/profile',
  'POST /api/admin/users/{id}/suspend', 'POST /api/admin/users/{id}/reactivate',
  'GET /api/me/profile', 'PUT /api/me/profile',
  'GET /api/categories',
  'POST /api/articles', 'GET /api/articles/{id}', 'PUT /api/articles/{id}', 'DELETE /api/articles/{id}',
  'POST /api/articles/{id}/submit', 'POST /api/articles/{id}/withdraw', 'GET /api/articles/{id}/reviews',
  'GET /api/admin/reviews/queue', 'GET /api/admin/articles/{id}',
  'POST /api/admin/articles/{id}/approve', 'POST /api/admin/articles/{id}/reject',
  'GET /api/feed', 'GET /api/feed/{id}',
  'GET /api/me/articles', 'GET /api/me/articles/stats',
  'GET /api/admin/dashboard',
  'GET /api/admin/audit-events', 'GET /api/admin/audit-events/actions',
];
