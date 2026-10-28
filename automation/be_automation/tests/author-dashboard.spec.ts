import type { APIRequestContext } from '@playwright/test';
import { test, expect } from '../fixtures';
import { expectProblem } from '../contract';
import { approveArticle, createDraft, rejectArticle, submittedArticle } from '../articles';

const stats = async (api: APIRequestContext) => (await api.get('/api/me/articles/stats')).json();

test.describe('GET /api/me/articles/stats', () => {
  test('a new author has zero in every status', async ({ newAuthor }) => {
    const author = await newAuthor();
    const response = await author.api.get('/api/me/articles/stats');
    expect(response.status()).toBe(200);
    expect(await response.json()).toEqual({ draft: 0, pendingReview: 0, published: 0, rejected: 0 });
  });

  test('counts the author\'s articles per status', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    await createDraft(author.api);
    await createDraft(author.api);
    await submittedArticle(author.api);
    await approveArticle(adminApi, await submittedArticle(author.api));
    await rejectArticle(adminApi, await submittedArticle(author.api));

    expect(await stats(author.api)).toEqual({ draft: 2, pendingReview: 1, published: 1, rejected: 1 });
  });

  test('does not count another author\'s articles', async ({ newAuthor }) => {
    const author = await newAuthor();
    const other = await newAuthor();
    await createDraft(other.api);
    await submittedArticle(other.api);

    expect(await stats(author.api)).toEqual({ draft: 0, pendingReview: 0, published: 0, rejected: 0 });
  });

  test('counts follow submit, withdraw, approve and reject', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    await createDraft(author.api);
    expect(await stats(author.api)).toMatchObject({ draft: 1, pendingReview: 0 });

    const pending = await submittedArticle(author.api);
    expect(await stats(author.api)).toMatchObject({ draft: 1, pendingReview: 1 });

    await author.api.post(`/api/articles/${pending.id}/withdraw`, { data: { version: pending.version } });
    expect(await stats(author.api)).toMatchObject({ draft: 2, pendingReview: 0 });

    const second = await submittedArticle(author.api);
    await approveArticle(adminApi, second);
    expect(await stats(author.api)).toMatchObject({ draft: 2, pendingReview: 0, published: 1 });

    await rejectArticle(adminApi, await submittedArticle(author.api));
    expect(await stats(author.api)).toMatchObject({ rejected: 1 });
  });

  test('deleting a Draft lowers the count', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api);
    await author.api.delete(`/api/articles/${draft.id}?version=${draft.version}`);

    expect((await stats(author.api)).draft).toBe(0);
  });

  test('without a token returns 401, an administrator token 403', async ({ request, adminApi }) => {
    await expectProblem(await request.get('/api/me/articles/stats'), 401);
    await expectProblem(await adminApi.get('/api/me/articles/stats'), 403);
  });
});
