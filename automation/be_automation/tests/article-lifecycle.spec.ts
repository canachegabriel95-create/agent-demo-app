import { randomUUID } from 'node:crypto';
import type { APIRequestContext } from '@playwright/test';
import { test, expect } from '../fixtures';
import { expectProblem } from '../contract';
import { approveArticle, createDraft, rejectArticle, submittableContent, submitArticle, submittedArticle, type Article } from '../articles';

const post = (api: APIRequestContext, article: Article, action: 'submit' | 'withdraw', version = article.version) =>
  api.post(`/api/articles/${article.id}/${action}`, { data: { version } });

async function withdrawn(api: APIRequestContext, article: Article): Promise<Article> {
  const response = await post(api, article, 'withdraw');
  expect(response.status(), 'withdraw').toBe(200);
  return response.json();
}

test.describe('submit', () => {
  test('moves a Draft to PendingReview and records the submission', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api, await submittableContent(author.api));

    const response = await post(author.api, draft, 'submit');
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(body).toMatchObject({ status: 'PendingReview', submissionVersion: 1 });
    expect(Date.parse(body.submittedAt)).not.toBeNaN();
    expect(body.allowedActions).toContain('withdraw');
  });

  test('a draft that breaks the rules returns 400 on every broken field and changes nothing', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api, { title: 'abcd', summary: 'too short', body: 'too short' });

    const body = await expectProblem(await post(author.api, draft, 'submit'), 400);
    expect(Object.keys(body.errors).sort()).toEqual(['body', 'categoryId', 'summary', 'title']);
    expect((await (await author.api.get(`/api/articles/${draft.id}`)).json()).status).toBe('Draft');
  });

  const edges: [string, Record<string, string>, string][] = [
    ['title of 4 characters', { title: 'abcd' }, 'title'],
    ['summary of 19 characters', { summary: 'S'.repeat(19) }, 'summary'],
    ['body of 99 characters', { body: 'B'.repeat(99) }, 'body'],
  ];
  for (const [name, change, field] of edges) {
    test(`${name} returns 400 on ${field}`, async ({ newAuthor }) => {
      const author = await newAuthor();
      const draft = await createDraft(author.api, { ...(await submittableContent(author.api)), ...change });
      const body = await expectProblem(await post(author.api, draft, 'submit'), 400);
      expect(body.errors[field]).toBeDefined();
    });
  }

  test('minimum lengths are accepted', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api, {
      ...(await submittableContent(author.api)),
      title: 'T'.repeat(5),
      summary: 'S'.repeat(20),
      body: 'B'.repeat(100),
    });
    expect((await post(author.api, draft, 'submit')).status()).toBe(200);
  });
});

test.describe('withdraw and resubmit', () => {
  test('withdraw returns a PendingReview article to Draft', async ({ newAuthor }) => {
    const author = await newAuthor();
    const back = await withdrawn(author.api, await submittedArticle(author.api));
    expect(back.status).toBe('Draft');
  });

  test('a withdrawn article can be submitted again with a new submission version', async ({ newAuthor }) => {
    const author = await newAuthor();
    const again = await submitArticle(author.api, await withdrawn(author.api, await submittedArticle(author.api)));
    expect(again).toMatchObject({ status: 'PendingReview', submissionVersion: 2 });
  });

  test('a Rejected article is resubmitted with a new submission version and keeps its earlier decision', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const rejected = await rejectArticle(adminApi, await submittedArticle(author.api), 'First rejection reason.');

    const resubmitted = await submitArticle(author.api, rejected);
    expect(resubmitted).toMatchObject({ status: 'PendingReview', submissionVersion: 2 });

    await rejectArticle(adminApi, resubmitted, 'Second rejection reason.');
    const reviews = await (await author.api.get(`/api/articles/${resubmitted.id}/reviews`)).json();
    expect(reviews).toHaveLength(2);
    expect(reviews.map((r: { reason: string }) => r.reason).sort()).toEqual(['First rejection reason.', 'Second rejection reason.']);
  });
});

test.describe('unsupported transitions return 409', () => {
  test('withdraw a Draft', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await post(author.api, await createDraft(author.api), 'withdraw'), 409);
  });

  test('submit a PendingReview article', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await post(author.api, await submittedArticle(author.api), 'submit'), 409);
  });

  test('submit a Published article', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const published = await approveArticle(adminApi, await submittedArticle(author.api));
    await expectProblem(await post(author.api, published, 'submit'), 409);
  });

  test('withdraw a Published article', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const published = await approveArticle(adminApi, await submittedArticle(author.api));
    await expectProblem(await post(author.api, published, 'withdraw'), 409);
  });

  test('withdraw a Rejected article', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const rejected = await rejectArticle(adminApi, await submittedArticle(author.api));
    await expectProblem(await post(author.api, rejected, 'withdraw'), 409);
  });

  test('a stale version', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api, await submittableContent(author.api));
    await expectProblem(await post(author.api, draft, 'submit', draft.version + 1), 409);
  });
});

test.describe('ownership and access', () => {
  test('another author gets 404 on submit and withdraw', async ({ newAuthor }) => {
    const owner = await newAuthor();
    const other = await newAuthor();
    const draft = await createDraft(owner.api, await submittableContent(owner.api));
    await expectProblem(await post(other.api, draft, 'submit'), 404);

    const pending = await submitArticle(owner.api, draft);
    await expectProblem(await post(other.api, pending, 'withdraw'), 404);
  });

  test('an unknown article returns 404', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await author.api.post(`/api/articles/${randomUUID()}/submit`, { data: { version: 1 } }), 404);
  });

  test('without a token returns 401, an administrator token 403', async ({ request, adminApi }) => {
    const url = `/api/articles/${randomUUID()}/submit`;
    await expectProblem(await request.post(url, { data: { version: 1 } }), 401);
    await expectProblem(await adminApi.post(url, { data: { version: 1 } }), 403);
  });

  test('a suspended author gets 401', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api, await submittableContent(author.api));
    const { version } = await (await adminApi.get(`/api/admin/users/${author.id}`)).json();
    await adminApi.post(`/api/admin/users/${author.id}/suspend`, { data: { reason: 'Suspended by an automated test.', version } });

    await expectProblem(await post(author.api, draft, 'submit'), 401);
  });
});

test.describe('race', () => {
  test('withdraw against approve: exactly one succeeds, the other gets 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);

    const [withdraw, approve] = await Promise.all([
      post(author.api, pending, 'withdraw'),
      adminApi.post(`/api/admin/articles/${pending.id}/approve`, {
        data: { version: pending.version, submissionVersion: pending.submissionVersion },
      }),
    ]);

    expect([withdraw.status(), approve.status()].sort()).toEqual([200, 409]);
  });
});
