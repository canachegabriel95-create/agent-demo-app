import { randomUUID } from 'node:crypto';
import { test, expect } from '../fixtures';
import { expectProblem } from '../contract';
import { createDraft, submitArticle, submittableContent } from '../articles';

test.describe('GET /api/categories', () => {
  test('returns the seeded categories to an author', async ({ newAuthor }) => {
    const author = await newAuthor();
    const response = await author.api.get('/api/categories');
    expect(response.status()).toBe(200);
    const categories = await response.json();
    expect(categories.length).toBeGreaterThan(0);
    expect(categories[0]).toEqual({ id: expect.any(String), name: expect.any(String), slug: expect.any(String) });
  });

  test('returns the same list to an administrator', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const forAuthor = await (await author.api.get('/api/categories')).json();
    const forAdmin = await (await adminApi.get('/api/categories')).json();
    expect(forAdmin).toEqual(forAuthor);
  });

  test('without a token returns 401', async ({ request }) => {
    await expectProblem(await request.get('/api/categories'), 401);
  });

  for (const method of ['post', 'put', 'delete'] as const) {
    test(`${method.toUpperCase()} returns 405`, async ({ adminApi }) => {
      const response = await adminApi[method]('/api/categories', { data: {} });
      expect(response.status()).toBe(405);
    });
  }
});

test.describe('categories used by articles', () => {
  test('every returned id is accepted on a draft and on submit', async ({ newAuthor }) => {
    const author = await newAuthor();
    const categories = await (await author.api.get('/api/categories')).json();

    for (const { id } of categories) {
      const draft = await createDraft(author.api, { ...(await submittableContent(author.api)), categoryId: id });
      const submitted = await submitArticle(author.api, draft);
      expect(submitted.status).toBe('PendingReview');
    }
  });

  test('an unknown id is rejected', async ({ newAuthor }) => {
    const author = await newAuthor();
    const response = await author.api.post('/api/articles', { data: { title: 'Unknown category', categoryId: randomUUID() } });
    const body = await expectProblem(response, 400);
    expect(body.errors.categoryId).toBeDefined();
  });

  test('submit is refused without a category', async ({ newAuthor }) => {
    const author = await newAuthor();
    const { categoryId, ...withoutCategory } = await submittableContent(author.api);
    void categoryId;
    const draft = await createDraft(author.api, withoutCategory);
    const body = await expectProblem(
      await author.api.post(`/api/articles/${draft.id}/submit`, { data: { version: draft.version } }),
      400,
    );
    expect(body.errors.categoryId).toBeDefined();
  });
});
