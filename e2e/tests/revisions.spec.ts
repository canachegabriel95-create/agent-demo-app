import { expect, test, type Page } from '@playwright/test';
import { createAuthor, feedTitle, loginAdmin, publishArticle, type Author } from './support/api';
import {
  ADMIN_SESSION_KEY,
  ADMIN_URL,
  AUTHOR_SESSION_KEY,
  AUTHOR_URL,
} from './support/env';

/** F17.1: an author revises a published article; readers keep the published version until a revision is approved. */

const REVISED_SUMMARY = 'The revised summary, also long enough to be submitted.';
const REVISED_BODY = 'The revised body of the article. '.repeat(8);

let adminToken: string;
let author: Author;

async function signedIn(page: Page, key: string, token: string) {
  await page.addInitScript(
    ([name, value]) => window.sessionStorage.setItem(name!, value!),
    [key, token],
  );
}

test.beforeAll(async ({ request }) => {
  adminToken = await loginAdmin(request);
  author = await createAuthor(request, adminToken);
});

test('revising keeps the published version until the administrator approves the revision', async ({
  browser,
  request,
}) => {
  const original = `E2E original ${Date.now()}`;
  const revised = `E2E revised ${Date.now()}`;
  const articleId = await publishArticle(request, author, adminToken, original);

  // The author starts a revision of the published article and sends it for review.
  const authorPage = await browser.newPage();
  await signedIn(authorPage, AUTHOR_SESSION_KEY, author.token);
  await authorPage.goto(`${AUTHOR_URL}/articles/${articleId}`);
  await expect(authorPage.getByTestId('article-status-badge')).toHaveText('Published');
  await authorPage.getByTestId('article-revise').click();

  await expect(authorPage).toHaveURL(/\/revisions\//);
  await expect(authorPage.getByTestId('revision-status-badge')).toHaveText('Revision draft');
  await authorPage.getByTestId('revision-title').fill(revised);
  await authorPage.getByTestId('revision-summary').fill(REVISED_SUMMARY);
  await authorPage.getByTestId('revision-body').fill(REVISED_BODY);
  await authorPage.getByTestId('revision-save').click();
  await expect(authorPage.getByTestId('revision-flash')).toContainText('Revision saved');
  await authorPage.getByTestId('revision-submit').click();
  await authorPage.getByTestId('revision-submit-confirm').click();
  await expect(authorPage.getByTestId('revision-status-badge')).toHaveText('Revision pending review');
  await expect(authorPage.getByTestId('revision-readonly-notice')).toBeVisible();

  // The published article is untouched and still the one readers see; the dashboard shows the revision next to it.
  expect(await feedTitle(request, author.token, articleId)).toBe(original);
  await authorPage.goto(`${AUTHOR_URL}/articles/${articleId}`);
  await expect(authorPage.getByTestId('article-view-title')).toHaveText(original);
  await expect(authorPage.getByTestId('article-revision-status')).toHaveText('Revision pending review');
  await expect(authorPage.getByTestId('article-revise')).toHaveCount(0);
  await authorPage.goto(`${AUTHOR_URL}/articles?status=Published`);
  await expect(authorPage.getByTestId('article-row-revision').first()).toHaveText('Revision pending review');

  // The administrator finds it in the queue, marked as a revision, and sees both versions side by side.
  const adminPage = await browser.newPage();
  await signedIn(adminPage, ADMIN_SESSION_KEY, adminToken);
  await adminPage.goto(`${ADMIN_URL}/reviews`);
  const row = adminPage.getByTestId(`review-row-${articleId}`);
  await expect(row.getByTestId('review-row-kind')).toHaveText('Revision');
  await row.getByRole('link', { name: revised }).click();

  await expect(adminPage.getByTestId('revision-review-kind')).toBeVisible();
  await expect(adminPage.getByTestId('published-title')).toHaveText(original);
  await expect(adminPage.getByTestId('revision-title')).toHaveText(revised);
  await expect(adminPage.getByTestId('changed-title')).toBeVisible();

  // Approving replaces the published content; readers now see the new version.
  await adminPage.getByTestId('revision-review-approve').click();
  await adminPage.getByTestId('revision-review-approve-confirm').click();
  await expect(adminPage.getByTestId('review-flash')).toContainText('replaces the published version');
  await expect(adminPage.getByTestId(`review-row-${articleId}`)).toHaveCount(0);

  expect(await feedTitle(request, author.token, articleId)).toBe(revised);
  await authorPage.goto(`${AUTHOR_URL}/feed/${articleId}`);
  await expect(authorPage.getByRole('heading', { level: 1 })).toHaveText(revised);
  await authorPage.goto(`${AUTHOR_URL}/articles/${articleId}`);
  await expect(authorPage.getByTestId('article-view-title')).toHaveText(revised);
  await expect(authorPage.getByTestId('article-revise')).toBeVisible();
  await expect(authorPage.getByTestId('article-revision')).toHaveCount(0);

  await authorPage.close();
  await adminPage.close();
});

test('a rejected revision leaves the published version alone and the author sees why', async ({
  browser,
  request,
}) => {
  const original = `E2E kept ${Date.now()}`;
  const articleId = await publishArticle(request, author, adminToken, original);
  const reason = 'Please add concrete sources for the claims.';

  const authorPage = await browser.newPage();
  await signedIn(authorPage, AUTHOR_SESSION_KEY, author.token);
  await authorPage.goto(`${AUTHOR_URL}/articles/${articleId}`);
  await authorPage.getByTestId('article-revise').click();
  await authorPage.getByTestId('revision-title').fill(`${original} (edited)`);
  await authorPage.getByTestId('revision-submit').click();
  await authorPage.getByTestId('revision-submit-confirm').click();
  await expect(authorPage.getByTestId('revision-status-badge')).toHaveText('Revision pending review');
  const revisionUrl = authorPage.url();

  const adminPage = await browser.newPage();
  await signedIn(adminPage, ADMIN_SESSION_KEY, adminToken);
  await adminPage.goto(`${ADMIN_URL}/reviews`);
  await adminPage.getByTestId(`review-row-${articleId}`).getByRole('link').click();
  await adminPage.getByTestId('revision-review-reject').click();
  await adminPage.getByLabel(/Reason/).fill(reason);
  await adminPage.getByTestId('review-reject-confirm').click();
  await expect(adminPage.getByTestId('review-flash')).toContainText('Rejected the revision');

  expect(await feedTitle(request, author.token, articleId)).toBe(original);
  await authorPage.goto(revisionUrl);
  await expect(authorPage.getByTestId('revision-status-badge')).toHaveText('Revision rejected');
  await expect(authorPage.getByTestId('revision-review-reason')).toHaveText(reason);
  await expect(authorPage.getByTestId('revision-title')).toBeEnabled();

  // The author edits it and sends it again; the earlier decision stays in the history.
  await authorPage.getByTestId('revision-title').fill(`${original} (second try)`);
  await authorPage.getByTestId('revision-submit').click();
  await authorPage.getByTestId('revision-submit-confirm').click();
  await expect(authorPage.getByTestId('revision-status-badge')).toHaveText('Revision pending review');
  await expect(authorPage.getByTestId('revision-history')).toContainText('Submission 1: Rejected');

  await authorPage.close();
  await adminPage.close();
});

test('a revision can be discarded and the published article is unaffected', async ({ browser, request }) => {
  const original = `E2E discard ${Date.now()}`;
  const articleId = await publishArticle(request, author, adminToken, original);

  const page = await browser.newPage();
  await signedIn(page, AUTHOR_SESSION_KEY, author.token);
  await page.goto(`${AUTHOR_URL}/articles/${articleId}`);
  await page.getByTestId('article-revise').click();
  await page.getByTestId('revision-discard').click();
  await page.getByTestId('revision-discard-confirm').click();

  await expect(page).toHaveURL(new RegExp(`/articles/${articleId}$`));
  await expect(page.getByTestId('article-view-title')).toHaveText(original);
  await expect(page.getByTestId('article-revise')).toBeVisible();
  expect(await feedTitle(request, author.token, articleId)).toBe(original);
  await page.close();
});
