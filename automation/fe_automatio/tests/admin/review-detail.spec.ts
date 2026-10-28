import { randomUUID } from 'node:crypto';
import type { Page } from '@playwright/test';
import { apiAs, createSubmittedArticle, ok, reject, resubmit, withdraw } from '../support/api';
import { apiLogin } from '../support/auth';
import { suspendUser } from '../support/db';
import { test, expect } from '../support/fixtures';

interface ReviewArticle {
  status: string;
  reviewHistory: { submissionVersion: number; decision: string; reason: string | null }[];
}

async function submittedBy(request: Parameters<typeof apiLogin>[0], author: Parameters<typeof apiLogin>[1], title?: string, body?: string) {
  const api = apiAs(request, await apiLogin(request, author, 'Author'));
  return { api, article: await createSubmittedArticle(api, title, body) };
}

const articleOf = async (admin: { api: ReturnType<typeof apiAs> }, id: string) =>
  ok<ReviewArticle>(await admin.api.get(`/api/admin/articles/${id}`));

const open = (page: Page, id: string) => page.goto(`/reviews/${id}`);

test('shows the title, summary and body as plain text', async ({ page, request, admin, newUser }) => {
  void admin;
  const body = `<script>window.__pwned = true</script><b>not bold</b> ${'padding text '.repeat(10)}`;
  const { article } = await submittedBy(request, await newUser({ role: 'Author' }), 'Plain <i>text</i> title', body);

  await open(page, article.id);

  await expect(page.getByTestId('review-title')).toHaveText('Plain <i>text</i> title');
  await expect(page.getByTestId('review-body')).toContainText('<script>window.__pwned = true</script><b>not bold</b>');
  await expect(page.getByTestId('review-body').locator('b, script')).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { __pwned?: boolean }).__pwned)).toBeUndefined();
});

test('approving publishes the article and removes it from the queue', async ({ page, request, admin, newUser }) => {
  const { article } = await submittedBy(request, await newUser({ role: 'Author' }));
  await open(page, article.id);

  await page.getByTestId('review-approve').click();
  await page.getByTestId('review-approve-confirm').click();

  await expect(page).toHaveURL(/\/reviews$/);
  await expect(page.getByTestId(`review-row-${article.id}`)).toHaveCount(0);
  expect((await articleOf(admin, article.id)).status).toBe('Published');
});

test('rejecting needs a reason of at least 10 characters and stores it', async ({ page, request, admin, newUser }) => {
  const { article } = await submittedBy(request, await newUser({ role: 'Author' }));
  await open(page, article.id);
  await page.getByTestId('review-reject').click();

  await page.getByRole('dialog').getByLabel('Reason').fill('too short');
  await page.getByTestId('review-reject-confirm').click();
  await expect(page.getByRole('dialog').getByLabel('Reason')).toHaveAttribute('aria-invalid', 'true');

  await page.getByRole('dialog').getByLabel('Reason').fill('Please add sources for the claims.');
  await page.getByTestId('review-reject-confirm').click();

  await expect(page).toHaveURL(/\/reviews$/);
  const saved = await articleOf(admin, article.id);
  expect(saved.status).toBe('Rejected');
  expect(saved.reviewHistory[0].reason).toBe('Please add sources for the claims.');
});

test('approving an article the author withdrew meanwhile shows the conflict message', async ({
  page,
  request,
  admin,
  newUser,
}) => {
  void admin;
  const { api, article } = await submittedBy(request, await newUser({ role: 'Author' }));
  await open(page, article.id);
  await expect(page.getByTestId('review-approve')).toBeEnabled();
  await withdraw(api, article);

  await page.getByTestId('review-approve').click();
  await page.getByTestId('review-approve-confirm').click();

  await expect(page.getByTestId('review-conflict-message')).toContainText('This article has changed since you opened it');
});

test("review actions are blocked for a suspended author's article", async ({ page, request, admin, newUser }) => {
  void admin;
  const author = await newUser({ role: 'Author' });
  const { article } = await submittedBy(request, author);
  await suspendUser(author.id);

  await open(page, article.id);

  await expect(page.getByTestId('review-blocked')).toBeVisible();
  await expect(page.getByTestId('review-approve')).toBeDisabled();
  await expect(page.getByTestId('review-reject')).toBeDisabled();
});

test('earlier decisions stay in the history after the article is resubmitted', async ({ page, request, admin, newUser }) => {
  const { api, article } = await submittedBy(request, await newUser({ role: 'Author' }));
  const rejected = await reject(admin.api, article, 'The first version lacks detail.');
  await resubmit(api, rejected);

  await open(page, article.id);

  await expect(page.getByTestId('review-status')).toContainText('Pending review');
  await expect(page.getByTestId('review-history')).toContainText('Submission 1: Rejected');
  await expect(page.getByTestId('review-history')).toContainText('The first version lacks detail.');
});

test('an unknown article id shows the not-found state', async ({ page, admin }) => {
  void admin;

  await open(page, randomUUID());

  await expect(page.getByRole('heading', { name: /not found/i })).toBeVisible();
});
