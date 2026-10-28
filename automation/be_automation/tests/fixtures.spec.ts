import { test, expect } from '../fixtures';
import { expectPage, expectProblem } from '../contract';

test('admin fixture is authenticated and lists invitations as a page', async ({ adminApi }) => {
  await expectPage(await adminApi.get('/api/admin/invitations'));
});

test('new author can sign in and is an Author', async ({ newAuthor }) => {
  const author = await newAuthor();
  const me = await author.api.get('/api/auth/me');
  expect((await me.json()).role).toBe('Author');
});

test('a request without a token gets a 401 problem', async ({ request }) => {
  await expectProblem(await request.get('/api/auth/me'), 401);
});
