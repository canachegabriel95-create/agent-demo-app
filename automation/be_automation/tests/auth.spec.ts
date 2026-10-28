import { request as playwrightRequest } from '@playwright/test';
import { test, expect, requiredEnv } from '../fixtures';
import { expectProblem } from '../contract';
import { expireSessionsOf } from '../db';

const adminLogin = () => ({
  email: requiredEnv('API_ADMIN_EMAIL'),
  password: requiredEnv('API_ADMIN_PASSWORD'),
});

test.describe('POST /api/auth/login', () => {
  test('admin signs in on the Admin portal and the token works', async ({ request }) => {
    const response = await request.post('/api/auth/login', { data: { ...adminLogin(), portal: 'Admin' } });
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(body.user.role).toBe('Administrator');
    const me = await request.get('/api/auth/me', { headers: { Authorization: `Bearer ${body.token}` } });
    expect(me.status()).toBe(200);
  });

  test('author signs in on the Author portal and the token works', async ({ request, newAuthor }) => {
    const author = await newAuthor();
    const response = await request.post('/api/auth/login', {
      data: { email: author.email, password: author.password, portal: 'Author' },
    });
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(body.user.role).toBe('Author');
    expect(body.user.email).toBe(author.email);
    const me = await request.get('/api/auth/me', { headers: { Authorization: `Bearer ${body.token}` } });
    expect(me.status()).toBe(200);
  });

  test('wrong password, unknown email and portal mismatch return the same 401', async ({ request, newAuthor }) => {
    const author = await newAuthor();
    const attempts = [
      { email: author.email, password: 'Wrong-password-1!', portal: 'Author' },
      { email: `nobody-${Date.now()}@example.test`, password: author.password, portal: 'Author' },
      { email: author.email, password: author.password, portal: 'Admin' },
      { ...adminLogin(), portal: 'Author' },
    ];
    const bodies = [];
    for (const data of attempts) {
      const body = await expectProblem(await request.post('/api/auth/login', { data }), 401);
      bodies.push({ title: body.title, detail: body.detail, type: body.type });
    }
    for (const body of bodies) expect(body).toEqual(bodies[0]);
  });

  test('missing fields return 400 with camelCase field names', async ({ request }) => {
    const body = await expectProblem(await request.post('/api/auth/login', { data: {} }), 400);
    expect(Object.keys(body.errors).sort()).toEqual(['email', 'password', 'portal']);
  });

  test('an unknown portal value is rejected with 400', async ({ request }) => {
    const body = await expectProblem(
      await request.post('/api/auth/login', { data: { ...adminLogin(), portal: 'Reviewer' } }),
      400,
    );
    expect(body.errors.portal).toBeDefined();
  });

  test('a suspended author cannot sign in', async ({ request, adminApi, newAuthor }) => {
    const author = await newAuthor();
    const { id } = await (await author.api.get('/api/auth/me')).json();
    const { version } = await (await adminApi.get(`/api/admin/users/${id}`)).json();
    const suspended = await adminApi.post(`/api/admin/users/${id}/suspend`, {
      data: { reason: 'Suspended by an automated test.', version },
    });
    expect(suspended.status()).toBe(200);

    await expectProblem(
      await request.post('/api/auth/login', { data: { email: author.email, password: author.password, portal: 'Author' } }),
      401,
    );
  });
});

test.describe('POST /api/auth/logout', () => {
  test('revokes the session on the server', async ({ newAuthor }) => {
    const author = await newAuthor();
    expect((await author.api.post('/api/auth/logout')).status()).toBe(204);
    await expectProblem(await author.api.get('/api/auth/me'), 401);
  });

  test('only ends the session whose token was sent', async ({ request, newAuthor }) => {
    const first = await newAuthor();
    const second = await request.post('/api/auth/login', {
      data: { email: first.email, password: first.password, portal: 'Author' },
    });
    const secondToken = (await second.json()).token;

    await first.api.post('/api/auth/logout');

    const stillValid = await request.get('/api/auth/me', { headers: { Authorization: `Bearer ${secondToken}` } });
    expect(stillValid.status()).toBe(200);
  });

  test('without a token returns 401', async ({ request }) => {
    await expectProblem(await request.post('/api/auth/logout'), 401);
  });
});

test.describe('GET /api/auth/me', () => {
  test('returns the signed-in user', async ({ newAuthor }) => {
    const author = await newAuthor();
    const body = await (await author.api.get('/api/auth/me')).json();
    expect(body).toMatchObject({ email: author.email, role: 'Author', status: 'Active' });
  });
});

test.describe('invalid tokens', () => {
  test('a tampered token returns 401', async ({ baseURL, newAuthor }) => {
    const author = await newAuthor();
    const tampered = author.token.slice(0, -1) + (author.token.endsWith('A') ? 'B' : 'A');
    const api = await playwrightRequest.newContext({ baseURL, extraHTTPHeaders: { Authorization: `Bearer ${tampered}` } });
    await expectProblem(await api.get('/api/auth/me'), 401);
    await api.dispose();
  });

  test('an expired session returns 401', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expireSessionsOf(author.email);
    await expectProblem(await author.api.get('/api/auth/me'), 401);
  });

  test('a session of a user suspended later returns 401 immediately', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const { id } = await (await author.api.get('/api/auth/me')).json();
    const { version } = await (await adminApi.get(`/api/admin/users/${id}`)).json();
    await adminApi.post(`/api/admin/users/${id}/suspend`, {
      data: { reason: 'Suspended by an automated test.', version },
    });
    await expectProblem(await author.api.get('/api/auth/me'), 401);
  });
});
