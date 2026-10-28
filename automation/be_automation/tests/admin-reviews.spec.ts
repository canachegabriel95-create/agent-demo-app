import { randomUUID } from 'node:crypto';
import type { APIRequestContext } from '@playwright/test';
import { test, expect } from '../fixtures';
import { expectPage, expectProblem } from '../contract';
import { createDraft, submitArticle, submittedArticle, submittableContent, type Article } from '../articles';

const decide = (adminApi: APIRequestContext, article: Article, decision: 'approve' | 'reject', extra: Record<string, unknown> = {}) =>
  adminApi.post(`/api/admin/articles/${article.id}/${decision}`, {
    data: { version: article.version, submissionVersion: article.submissionVersion, ...extra },
  });

const REASON = 'Rejected by an automated test.';

test.describe('GET /api/admin/reviews/queue', () => {
  test('lists only PendingReview articles', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    await createDraft(author.api);
    const pending = await submittedArticle(author.api);

    const body = await expectPage(await adminApi.get(`/api/admin/reviews/queue?authorId=${author.id}`));
    expect(body.items.map((i: { articleId: string }) => i.articleId)).toEqual([pending.id]);
  });

  test('pages the results', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    for (let i = 0; i < 3; i++) await submittedArticle(author.api);

    const first = await expectPage(await adminApi.get(`/api/admin/reviews/queue?authorId=${author.id}&pageSize=2`));
    expect(first).toMatchObject({ page: 1, pageSize: 2, totalCount: 3 });
    expect(first.items).toHaveLength(2);
    const second = await expectPage(await adminApi.get(`/api/admin/reviews/queue?authorId=${author.id}&pageSize=2&page=2`));
    expect(second.items).toHaveLength(1);
  });

  test('a decided article leaves the queue', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    await decide(adminApi, pending, 'approve');

    const body = await expectPage(await adminApi.get(`/api/admin/reviews/queue?authorId=${author.id}`));
    expect(body.totalCount).toBe(0);
  });

  test('an invalid sort returns 400', async ({ adminApi }) => {
    await expectProblem(await adminApi.get('/api/admin/reviews/queue?sort=bogus'), 400);
  });
});

test.describe('GET /api/admin/articles/{id}', () => {
  test('returns a pending article that can be reviewed', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    const response = await adminApi.get(`/api/admin/articles/${pending.id}`);
    expect(response.status()).toBe(200);
    expect(await response.json()).toMatchObject({ id: pending.id, status: 'PendingReview', canReview: true });
  });

  test('a draft that was never submitted returns 404', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api);
    await expectProblem(await adminApi.get(`/api/admin/articles/${draft.id}`), 404);
  });

  test('an unknown id returns 404', async ({ adminApi }) => {
    await expectProblem(await adminApi.get(`/api/admin/articles/${randomUUID()}`), 404);
  });
});

test.describe('POST approve', () => {
  test('publishes the article and stores the review', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);

    const response = await decide(adminApi, pending, 'approve');
    expect(response.status()).toBe(200);
    expect((await response.json()).status).toBe('Published');

    const article = await (await author.api.get(`/api/articles/${pending.id}`)).json();
    expect(article.status).toBe('Published');
    expect(Date.parse(article.publishedAt)).not.toBeNaN();
    const reviews = await (await author.api.get(`/api/articles/${pending.id}/reviews`)).json();
    expect(reviews).toHaveLength(1);
    expect(reviews[0].decision).toBe('Approved');
  });

  test('approving twice returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    await decide(adminApi, pending, 'approve');
    await expectProblem(await decide(adminApi, pending, 'approve'), 409);
  });

  test('a withdrawn article returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    const withdrawn = await (await author.api.post(`/api/articles/${pending.id}/withdraw`, { data: { version: pending.version } })).json();
    await expectProblem(await decide(adminApi, { ...withdrawn, submissionVersion: pending.submissionVersion }, 'approve'), 409);
  });

  test('a draft that was never submitted returns 404', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api, await submittableContent(author.api));
    await expectProblem(await decide(adminApi, { ...draft, submissionVersion: 1 }, 'approve'), 404);
  });

  test('a stale version returns 409 and the article stays pending', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    await expectProblem(await decide(adminApi, { ...pending, version: pending.version + 1 }, 'approve'), 409);
    expect((await (await author.api.get(`/api/articles/${pending.id}`)).json()).status).toBe('PendingReview');
  });

  test('an older submission version returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    await expectProblem(await decide(adminApi, { ...pending, submissionVersion: pending.submissionVersion + 1 }, 'approve'), 409);
  });

  test('a missing version returns 400, an unknown article 404', async ({ adminApi }) => {
    await expectProblem(await adminApi.post(`/api/admin/articles/${randomUUID()}/approve`, { data: {} }), 400);
    await expectProblem(await adminApi.post(`/api/admin/articles/${randomUUID()}/approve`, { data: { version: 1, submissionVersion: 1 } }), 404);
  });
});

test.describe('POST reject', () => {
  test('rejects the article, stores the reason and shows it to the author', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);

    const response = await decide(adminApi, pending, 'reject', { reason: REASON });
    expect(response.status()).toBe(200);
    expect((await response.json()).status).toBe('Rejected');

    const reviews = await (await author.api.get(`/api/articles/${pending.id}/reviews`)).json();
    expect(reviews[0]).toMatchObject({ decision: 'Rejected', reason: REASON });
  });

  for (const [name, reason] of [['missing', undefined], ['9 characters', 'too short'], ['1001 characters', 'r'.repeat(1001)]] as const) {
    test(`a ${name} reason returns 400 and the article stays pending`, async ({ adminApi, newAuthor }) => {
      const author = await newAuthor();
      const pending = await submittedArticle(author.api);
      const body = await expectProblem(await decide(adminApi, pending, 'reject', { reason }), 400);
      expect(body.errors.reason).toBeDefined();
      expect((await (await author.api.get(`/api/articles/${pending.id}`)).json()).status).toBe('PendingReview');
    });
  }

  test('a reason at the limits is accepted', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    expect((await decide(adminApi, await submittedArticle(author.api), 'reject', { reason: 'r'.repeat(10) })).status()).toBe(200);
    expect((await decide(adminApi, await submittedArticle(author.api), 'reject', { reason: 'r'.repeat(1000) })).status()).toBe(200);
  });

  test('rejecting a decided article returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    await decide(adminApi, pending, 'reject', { reason: REASON });
    await expectProblem(await decide(adminApi, pending, 'reject', { reason: REASON }), 409);
  });

  test('earlier decisions stay visible after a resubmission', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const rejected = await (await decide(adminApi, await submittedArticle(author.api), 'reject', { reason: 'First rejection reason.' })).json();
    const resubmitted = await submitArticle(author.api, rejected);

    const detail = await (await adminApi.get(`/api/admin/articles/${resubmitted.id}`)).json();
    expect(JSON.stringify(detail)).toContain('First rejection reason.');
  });
});

test.describe('suspended author', () => {
  test('review actions return 409 and canReview is false', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    const { version } = await (await adminApi.get(`/api/admin/users/${author.id}`)).json();
    await adminApi.post(`/api/admin/users/${author.id}/suspend`, { data: { reason: 'Suspended by an automated test.', version } });

    await expectProblem(await decide(adminApi, pending, 'approve'), 409);
    await expectProblem(await decide(adminApi, pending, 'reject', { reason: REASON }), 409);
    const detail = await (await adminApi.get(`/api/admin/articles/${pending.id}`)).json();
    expect(detail.canReview).toBe(false);
    expect(detail.reviewBlockedReason).toBeTruthy();
  });
});

test.describe('access', () => {
  test('without a token every route returns 401', async ({ request }) => {
    const id = randomUUID();
    await expectProblem(await request.get('/api/admin/reviews/queue'), 401);
    await expectProblem(await request.get(`/api/admin/articles/${id}`), 401);
    await expectProblem(await request.post(`/api/admin/articles/${id}/approve`, { data: { version: 1, submissionVersion: 1 } }), 401);
    await expectProblem(await request.post(`/api/admin/articles/${id}/reject`, { data: { version: 1, submissionVersion: 1, reason: REASON } }), 401);
  });

  test('an Author token returns 403 on every route', async ({ newAuthor }) => {
    const author = await newAuthor();
    const id = randomUUID();
    await expectProblem(await author.api.get('/api/admin/reviews/queue'), 403);
    await expectProblem(await author.api.get(`/api/admin/articles/${id}`), 403);
    await expectProblem(await author.api.post(`/api/admin/articles/${id}/approve`, { data: { version: 1, submissionVersion: 1 } }), 403);
    await expectProblem(await author.api.post(`/api/admin/articles/${id}/reject`, { data: { version: 1, submissionVersion: 1, reason: REASON } }), 403);
  });
});

test.describe('audit', () => {
  test('approve and reject are recorded', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const toApprove = await submittedArticle(author.api);
    const toReject = await submittedArticle(author.api);
    await decide(adminApi, toApprove, 'approve');
    await decide(adminApi, toReject, 'reject', { reason: REASON });

    const events = (await (await adminApi.get('/api/admin/audit-events?pageSize=100')).json()).items;
    const actionsFor = (id: string) => events.filter((e: { entityId: string }) => e.entityId === id).map((e: { action: string }) => e.action);
    expect(actionsFor(toApprove.id)).toContain('ArticleApproved');
    expect(actionsFor(toReject.id)).toContain('ArticleRejected');
  });
});
