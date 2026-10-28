import { approve, createDraft, createSubmittedArticle, firstCategoryId, ok, reject } from '../support/api';
import { test, expect } from '../support/fixtures';

interface Saved {
  title: string;
  summary: string;
  body: string;
  status: string;
  version: number;
}

test('a draft with only a title can be saved', async ({ page, author }) => {
  await page.goto('/articles/new');

  await page.getByTestId('article-title').fill('Only a title');
  await page.getByTestId('article-save').click();

  await expect(page).toHaveURL(/\/articles\/[0-9a-f-]+\/edit$/);
  const id = page.url().split('/').at(-2);
  const saved = await ok<Saved>(await author.api.get(`/api/articles/${id}`));
  expect(saved).toMatchObject({ title: 'Only a title', status: 'Draft' });
});

test('the title, summary and body limits are enforced', async ({ page, author }) => {
  void author;
  await page.goto('/articles/new');

  await page.getByTestId('article-title').fill('x'.repeat(151));
  await page.getByTestId('article-summary').fill('x'.repeat(501));
  await page.getByTestId('article-body').fill('x'.repeat(20001));
  await page.getByTestId('article-save').click();

  await expect(page.getByText('Title must be at most 150 characters.')).toBeVisible();
  await expect(page.getByText('Summary must be at most 500 characters.')).toBeVisible();
  await expect(page.getByText('Body must be at most 20000 characters.')).toBeVisible();
  await expect(page).toHaveURL('/articles/new');
});

test('a draft and a rejected article can be edited and saved', async ({ page, author, adminApi }) => {
  const draft = await createDraft(author.api, 'Draft before');
  const rejected = await reject(adminApi, await createSubmittedArticle(author.api, 'Rejected before'));

  for (const [article, title] of [
    [draft, 'Draft after'],
    [rejected, 'Rejected after'],
  ] as const) {
    await page.goto(`/articles/${article.id}/edit`);
    await page.getByTestId('article-title').fill(title);
    await page.getByTestId('article-save').click();
    await expect(page.getByTestId('article-status')).toContainText('Draft saved at');

    const saved = await ok<Saved>(await author.api.get(`/api/articles/${article.id}`));
    expect(saved.title).toBe(title);
  }
  const stillRejected = await ok<Saved>(await author.api.get(`/api/articles/${rejected.id}`));
  expect(stillRejected.status).toBe('Rejected');
});

test('a pending and a published article are read-only', async ({ page, author, adminApi }) => {
  const pending = await createSubmittedArticle(author.api, 'Pending read-only');
  const published = await approve(adminApi, await createSubmittedArticle(author.api, 'Published read-only'));

  for (const article of [pending, published]) {
    await page.goto(`/articles/${article.id}/edit`);

    await expect(page).toHaveURL(`/articles/${article.id}`);
    await expect(page.getByTestId('article-save')).toHaveCount(0);
  }
});

test('a change made elsewhere shows a conflict and is not overwritten', async ({ page, author }) => {
  const draft = await createDraft(author.api, 'Original title');
  const current = await ok<Saved & { category: { id: string } }>(await author.api.get(`/api/articles/${draft.id}`));
  await page.goto(`/articles/${draft.id}/edit`);
  await expect(page.getByTestId('article-title')).toHaveValue('Original title');

  await ok(
    await author.api.put(`/api/articles/${draft.id}`, {
      title: 'Changed elsewhere',
      summary: current.summary,
      body: current.body,
      categoryId: await firstCategoryId(author.api),
      version: current.version,
    }),
  );
  await page.getByTestId('article-title').fill('Changed in the editor');
  await page.getByTestId('article-save').click();

  await expect(page.getByTestId('article-conflict')).toBeVisible();
  const saved = await ok<Saved>(await author.api.get(`/api/articles/${draft.id}`));
  expect(saved.title).toBe('Changed elsewhere');
});

test('markup in an article is shown as text and never run', async ({ page, author }) => {
  const markup = '<script>window.__xss = true</script>';
  const draft = await createDraft(author.api, markup, `${markup} `.repeat(10));

  await page.goto(`/articles/${draft.id}`);
  await expect(page.getByTestId('article-view-title')).toHaveText(markup);
  await expect(page.getByTestId('article-view-body')).toContainText(markup);

  await page.goto('/articles');
  await expect(page.getByTestId('article-row').getByText(markup)).toBeVisible();
  expect(await page.evaluate(() => '__xss' in window)).toBe(false);
});

test('the categories come from the seeded list', async ({ page, author }) => {
  const seeded = await ok<{ name: string }[]>(await author.api.get('/api/categories'));
  await page.goto('/articles/new');

  await expect(page.getByTestId('article-category').locator('option')).toHaveText([
    '— Select —',
    ...seeded.map((category) => category.name),
  ]);
});
