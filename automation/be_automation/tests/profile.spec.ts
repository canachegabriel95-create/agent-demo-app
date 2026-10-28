import { test, expect } from '../fixtures';
import { expectProblem } from '../contract';

test.describe('GET /api/me/profile', () => {
  test('returns the signed-in author\'s own profile', async ({ newAuthor }) => {
    const author = await newAuthor();
    const response = await author.api.get('/api/me/profile');
    expect(response.status()).toBe(200);
    expect(await response.json()).toMatchObject({ email: author.email, fullName: 'Test Author' });
  });

  test('two authors each see only their own profile', async ({ newAuthor }) => {
    const first = await newAuthor();
    const second = await newAuthor();
    expect((await (await first.api.get('/api/me/profile')).json()).email).toBe(first.email);
    expect((await (await second.api.get('/api/me/profile')).json()).email).toBe(second.email);
  });

  test('without a token returns 401', async ({ request }) => {
    await expectProblem(await request.get('/api/me/profile'), 401);
  });

  test('an administrator token returns 403', async ({ adminApi }) => {
    await expectProblem(await adminApi.get('/api/me/profile'), 403);
  });
});

test.describe('PUT /api/me/profile', () => {
  test('updates the name (trimmed) and biography and moves the version', async ({ newAuthor }) => {
    const author = await newAuthor();
    const before = await (await author.api.get('/api/me/profile')).json();

    const response = await author.api.put('/api/me/profile', {
      data: { fullName: '  Updated Name  ', bio: 'My new biography', version: before.version },
    });
    expect(response.status()).toBe(200);
    const after = await response.json();
    expect(after).toMatchObject({ fullName: 'Updated Name', bio: 'My new biography' });
    expect(after.version).not.toBe(before.version);
    expect(await (await author.api.get('/api/me/profile')).json()).toMatchObject({ fullName: 'Updated Name' });
  });

  test('email, role and status in the body are ignored', async ({ newAuthor }) => {
    const author = await newAuthor();
    const { version } = await (await author.api.get('/api/me/profile')).json();

    const response = await author.api.put('/api/me/profile', {
      data: { fullName: 'Same Person', version, email: 'other@example.test', role: 'Administrator', status: 'Suspended' },
    });

    expect(await response.json()).toMatchObject({ email: author.email, role: 'Author' });
    expect((await (await author.api.get('/api/auth/me')).json()).status).toBe('Active');
  });

  const invalid: [string, Record<string, string>, string][] = [
    ['name of 1 character', { fullName: 'A' }, 'fullName'],
    ['name of 101 characters', { fullName: 'A'.repeat(101) }, 'fullName'],
    ['biography of 1001 characters', { fullName: 'Valid Name', bio: 'b'.repeat(1001) }, 'bio'],
  ];
  for (const [name, change, field] of invalid) {
    test(`${name} returns 400 on ${field}`, async ({ newAuthor }) => {
      const author = await newAuthor();
      const { version } = await (await author.api.get('/api/me/profile')).json();
      const body = await expectProblem(await author.api.put('/api/me/profile', { data: { ...change, version } }), 400);
      expect(body.errors[field]).toBeDefined();
    });
  }

  test('a stale version returns 409', async ({ newAuthor }) => {
    const author = await newAuthor();
    const { version } = await (await author.api.get('/api/me/profile')).json();
    await author.api.put('/api/me/profile', { data: { fullName: 'First Edit', version } });

    await expectProblem(await author.api.put('/api/me/profile', { data: { fullName: 'Second Edit', version } }), 409);
  });

  test('a missing version returns 400', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await author.api.put('/api/me/profile', { data: { fullName: 'No Version' } }), 400);
  });

  test('editing never changes another author', async ({ newAuthor }) => {
    const first = await newAuthor();
    const second = await newAuthor();
    const { version } = await (await first.api.get('/api/me/profile')).json();

    await first.api.put('/api/me/profile', { data: { fullName: 'Only Me', version } });

    expect((await (await second.api.get('/api/me/profile')).json()).fullName).toBe('Test Author');
  });

  test('without a token returns 401, an administrator token 403', async ({ request, adminApi }) => {
    const data = { fullName: 'Nobody', version: 1 };
    await expectProblem(await request.put('/api/me/profile', { data }), 401);
    await expectProblem(await adminApi.put('/api/me/profile', { data }), 403);
  });
});
