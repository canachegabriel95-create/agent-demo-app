import { randomUUID } from 'node:crypto';
import type { APIRequestContext } from '@playwright/test';
import { test, expect } from '../fixtures';
import { expectPage, expectProblem } from '../contract';
import { approveArticle, createDraft, rejectArticle, submitArticle, submittableContent } from '../articles';

// Every article of a test carries the same unique tag in its title, so the shared feed can be searched for it.
async function submittedWithTag(api: APIRequestContext, tag: string) {
  const content = await submittableContent(api);
  return submitArticle(api, await createDraft(api, { ...content, title: `${tag} ${randomUUID()}` }));
}

const ids = (page: { items: { id: string }[] }) => page.items.map((i) => i.id);

test.describe('GET /api/feed', () => {
  test('lists only Published articles', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const tag = randomUUID();
    await createDraft(author.api, { title: `${tag} draft` });
    await submittedWithTag(author.api, tag);
    await rejectArticle(adminApi, await submittedWithTag(author.api, tag));
    const published = await approveArticle(adminApi, await submittedWithTag(author.api, tag));

    const body = await expectPage(await author.api.get(`/api/feed?search=${tag}`));
    expect(ids(body)).toEqual([published.id]);
  });

  test('shows the articles of every author', async ({ adminApi, newAuthor }) => {
    const writer = await newAuthor();
    const reader = await newAuthor();
    const tag = randomUUID();
    const published = await approveArticle(adminApi, await submittedWithTag(writer.api, tag));

    const body = await expectPage(await reader.api.get(`/api/feed?search=${tag}`));
    expect(ids(body)).toEqual([published.id]);
  });

  test('newest publication first', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const tag = randomUUID();
    const first = await approveArticle(adminApi, await submittedWithTag(author.api, tag));
    const second = await approveArticle(adminApi, await submittedWithTag(author.api, tag));

    const body = await expectPage(await author.api.get(`/api/feed?search=${tag}`));
    expect(ids(body)).toEqual([second.id, first.id]);
  });

  test('pages the results', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const tag = randomUUID();
    for (let i = 0; i < 3; i++) await approveArticle(adminApi, await submittedWithTag(author.api, tag));

    const first = await expectPage(await author.api.get(`/api/feed?search=${tag}&pageSize=2`));
    expect(first).toMatchObject({ page: 1, pageSize: 2, totalCount: 3 });
    expect(first.items).toHaveLength(2);
    const second = await expectPage(await author.api.get(`/api/feed?search=${tag}&pageSize=2&page=2`));
    expect(second.items).toHaveLength(1);
  });

  test('an article approved just now appears', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const tag = randomUUID();
    const pending = await submittedWithTag(author.api, tag);
    expect((await expectPage(await author.api.get(`/api/feed?search=${tag}`))).totalCount).toBe(0);

    await approveArticle(adminApi, pending);

    expect(ids(await expectPage(await author.api.get(`/api/feed?search=${tag}`)))).toEqual([pending.id]);
  });

  test('an invalid page or pageSize returns 400', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await author.api.get('/api/feed?page=0'), 400);
    await expectProblem(await author.api.get('/api/feed?pageSize=101'), 400);
  });

  test('without a token returns 401, an administrator token 403', async ({ request, adminApi }) => {
    await expectProblem(await request.get('/api/feed'), 401);
    await expectProblem(await adminApi.get('/api/feed'), 403);
  });
});

test.describe('GET /api/feed/{id}', () => {
  test('returns a Published article in full', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const published = await approveArticle(adminApi, await submittedWithTag(author.api, randomUUID()));

    const response = await author.api.get(`/api/feed/${published.id}`);
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(body).toMatchObject({ id: published.id, title: published.title });
    expect(body.body).toBeTruthy();
  });

  test('a Draft, PendingReview and Rejected article return 404', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api);
    const pending = await submittedWithTag(author.api, randomUUID());
    const rejected = await rejectArticle(adminApi, await submittedWithTag(author.api, randomUUID()));

    for (const article of [draft, pending, rejected]) {
      await expectProblem(await author.api.get(`/api/feed/${article.id}`), 404);
    }
  });

  test('an unknown id returns 404', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await author.api.get(`/api/feed/${randomUUID()}`), 404);
  });

  test('without a token returns 401, an administrator token 403', async ({ request, adminApi }) => {
    const url = `/api/feed/${randomUUID()}`;
    await expectProblem(await request.get(url), 401);
    await expectProblem(await adminApi.get(url), 403);
  });
});

test.describe('read-only', () => {
  for (const method of ['post', 'put', 'delete'] as const) {
    test(`${method.toUpperCase()} /api/feed returns 405`, async ({ newAuthor }) => {
      const author = await newAuthor();
      expect((await author.api[method]('/api/feed', { data: {} })).status()).toBe(405);
    });
  }
});
