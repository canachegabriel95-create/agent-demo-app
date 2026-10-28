import { randomUUID } from 'node:crypto';
import { test, expect } from '../fixtures';
import { expectPage, expectProblem } from '../contract';
import { approveArticle, createDraft, rejectArticle, submittedArticle } from '../articles';

test.describe('POST /api/articles', () => {
  test('creates a Draft from a title alone', async ({ newAuthor }) => {
    const author = await newAuthor();
    const response = await author.api.post('/api/articles', { data: { title: 'Only a title' } });
    expect(response.status()).toBe(201);
    const body = await response.json();
    expect(body).toMatchObject({ title: 'Only a title', status: 'Draft', submissionVersion: 0 });
    expect(response.headers()['location']).toContain(body.id);
  });

  const invalid: [string, Record<string, string>, string][] = [
    ['a missing title', {}, 'title'],
    ['a title of 151 characters', { title: 'T'.repeat(151) }, 'title'],
    ['a summary of 501 characters', { title: 'Valid title', summary: 'S'.repeat(501) }, 'summary'],
    ['a body of 20001 characters', { title: 'Valid title', body: 'B'.repeat(20001) }, 'body'],
  ];
  for (const [name, data, field] of invalid) {
    test(`${name} returns 400 on ${field}`, async ({ newAuthor }) => {
      const author = await newAuthor();
      const body = await expectProblem(await author.api.post('/api/articles', { data }), 400);
      expect(body.errors[field]).toBeDefined();
    });
  }

  test('limits are accepted at their edge', async ({ newAuthor }) => {
    const author = await newAuthor();
    const response = await author.api.post('/api/articles', {
      data: { title: 'T'.repeat(150), summary: 'S'.repeat(500), body: 'B'.repeat(20000) },
    });
    expect(response.status()).toBe(201);
  });

  test('HTML and script content is stored and returned verbatim', async ({ newAuthor }) => {
    const author = await newAuthor();
    const content = {
      title: '<b>Bold</b> & <script>alert(1)</script>',
      summary: '<img src=x onerror=alert(1)>',
      body: '<script>alert("body")</script>',
    };
    const draft = await createDraft(author.api, content);
    const fetched = await (await author.api.get(`/api/articles/${draft.id}`)).json();
    expect(fetched).toMatchObject(content);
  });

  test('without a token returns 401, an administrator token 403', async ({ request, adminApi }) => {
    await expectProblem(await request.post('/api/articles', { data: { title: 'Nobody' } }), 401);
    await expectProblem(await adminApi.post('/api/articles', { data: { title: 'Admin' } }), 403);
  });
});

test.describe('GET /api/articles/{id}', () => {
  test('returns the author\'s own article', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api, { title: 'Mine' });
    expect(await (await author.api.get(`/api/articles/${draft.id}`)).json()).toMatchObject({ id: draft.id, title: 'Mine' });
  });

  test('another author\'s article returns 404, like an unknown id', async ({ newAuthor }) => {
    const owner = await newAuthor();
    const other = await newAuthor();
    const draft = await createDraft(owner.api);
    await expectProblem(await other.api.get(`/api/articles/${draft.id}`), 404);
    await expectProblem(await other.api.get(`/api/articles/${randomUUID()}`), 404);
  });
});

test.describe('PUT /api/articles/{id}', () => {
  test('updates a Draft and moves the version', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api);
    const response = await author.api.put(`/api/articles/${draft.id}`, {
      data: { title: 'Edited title', summary: 'Edited summary', body: 'Edited body', version: draft.version },
    });
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(body).toMatchObject({ title: 'Edited title', summary: 'Edited summary', body: 'Edited body' });
    expect(body.version).not.toBe(draft.version);
  });

  test('a Rejected article can be edited and stays Rejected', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const rejected = await rejectArticle(adminApi, await submittedArticle(author.api));
    const response = await author.api.put(`/api/articles/${rejected.id}`, {
      data: { title: 'Fixed after rejection', version: rejected.version },
    });
    expect(response.status()).toBe(200);
    expect(await response.json()).toMatchObject({ title: 'Fixed after rejection', status: 'Rejected' });
  });

  test('a PendingReview article returns 409', async ({ newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    await expectProblem(
      await author.api.put(`/api/articles/${pending.id}`, { data: { title: 'Too late', version: pending.version } }),
      409,
    );
  });

  test('a Published article returns 409', async ({ adminApi, newAuthor }) => {
    const author = await newAuthor();
    const published = await approveArticle(adminApi, await submittedArticle(author.api));
    await expectProblem(
      await author.api.put(`/api/articles/${published.id}`, { data: { title: 'Too late', version: published.version } }),
      409,
    );
  });

  test('a stale version returns 409', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api);
    await author.api.put(`/api/articles/${draft.id}`, { data: { title: 'First edit', version: draft.version } });
    await expectProblem(
      await author.api.put(`/api/articles/${draft.id}`, { data: { title: 'Second edit', version: draft.version } }),
      409,
    );
  });

  test('an empty title returns 400', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api);
    const body = await expectProblem(
      await author.api.put(`/api/articles/${draft.id}`, { data: { title: '', version: draft.version } }),
      400,
    );
    expect(body.errors.title).toBeDefined();
  });

  test('another author\'s article returns 404 and is not changed', async ({ newAuthor }) => {
    const owner = await newAuthor();
    const other = await newAuthor();
    const draft = await createDraft(owner.api, { title: 'Original' });
    await expectProblem(
      await other.api.put(`/api/articles/${draft.id}`, { data: { title: 'Hijacked', version: draft.version } }),
      404,
    );
    expect((await (await owner.api.get(`/api/articles/${draft.id}`)).json()).title).toBe('Original');
  });
});

test.describe('DELETE /api/articles/{id}', () => {
  test('deletes a Draft', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api);
    expect((await author.api.delete(`/api/articles/${draft.id}?version=${draft.version}`)).status()).toBe(204);
    await expectProblem(await author.api.get(`/api/articles/${draft.id}`), 404);
  });

  test('a PendingReview article returns 409', async ({ newAuthor }) => {
    const author = await newAuthor();
    const pending = await submittedArticle(author.api);
    await expectProblem(await author.api.delete(`/api/articles/${pending.id}?version=${pending.version}`), 409);
  });

  test('a stale version returns 409', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api);
    await expectProblem(await author.api.delete(`/api/articles/${draft.id}?version=${draft.version + 1}`), 409);
  });

  test('a missing version returns 400', async ({ newAuthor }) => {
    const author = await newAuthor();
    const draft = await createDraft(author.api);
    await expectProblem(await author.api.delete(`/api/articles/${draft.id}`), 400);
  });

  test('another author\'s Draft returns 404 and survives', async ({ newAuthor }) => {
    const owner = await newAuthor();
    const other = await newAuthor();
    const draft = await createDraft(owner.api);
    await expectProblem(await other.api.delete(`/api/articles/${draft.id}?version=${draft.version}`), 404);
    expect((await owner.api.get(`/api/articles/${draft.id}`)).status()).toBe(200);
  });
});

test.describe('GET /api/me/articles', () => {
  test('lists only the caller\'s articles', async ({ newAuthor }) => {
    const first = await newAuthor();
    const second = await newAuthor();
    const mine = await createDraft(first.api);
    await createDraft(second.api);

    const body = await expectPage(await first.api.get('/api/me/articles'));
    expect(body.items.map((i: { id: string }) => i.id)).toEqual([mine.id]);
  });

  test('pages the results', async ({ newAuthor }) => {
    const author = await newAuthor();
    for (let i = 0; i < 3; i++) await createDraft(author.api);

    const first = await expectPage(await author.api.get('/api/me/articles?pageSize=2'));
    expect(first).toMatchObject({ page: 1, pageSize: 2, totalCount: 3 });
    expect(first.items).toHaveLength(2);
    const second = await expectPage(await author.api.get('/api/me/articles?pageSize=2&page=2'));
    expect(second.items).toHaveLength(1);
  });

  test('filters by status', async ({ newAuthor }) => {
    const author = await newAuthor();
    await createDraft(author.api);
    const pending = await submittedArticle(author.api);

    const body = await expectPage(await author.api.get('/api/me/articles?status=PendingReview'));
    expect(body.items.map((i: { id: string }) => i.id)).toEqual([pending.id]);
  });

  test('an unknown status returns 400', async ({ newAuthor }) => {
    const author = await newAuthor();
    await expectProblem(await author.api.get('/api/me/articles?status=Bogus'), 400);
  });

  test('without a token returns 401, an administrator token 403', async ({ request, adminApi }) => {
    await expectProblem(await request.get('/api/me/articles'), 401);
    await expectProblem(await adminApi.get('/api/me/articles'), 403);
  });
});
