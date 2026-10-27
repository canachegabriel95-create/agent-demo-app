import { expect, test } from '@playwright/test';
import { AUTHOR_PASSWORD, adminToken, publishedArticle, registerAuthor, type Author } from './support/api';
import { signInAsAdmin, signInAsAuthor } from './support/portals';
import { ADMIN_PORTAL_URL, AUTHOR_PORTAL_URL } from './support/stack';

let admin: string;
let author: Author;

test.beforeAll(async () => {
  admin = await adminToken();
  author = await registerAuthor(admin, 'reviser');
});

/** Opens the revision of an article in the Author Portal, rewrites the title and sends it for review. */
async function submitRevision(page: import('@playwright/test').Page, articleId: string, title: string) {
  await page.goto(`${AUTHOR_PORTAL_URL}/articles/${articleId}`);
  await page.getByTestId('article-revise').click();
  await expect(page).toHaveURL(new RegExp(`/articles/${articleId}/revision$`));
  await page.getByTestId('revision-title-input').fill(title);
  await page.getByTestId('revision-save').click();
  await expect(page.getByTestId('revision-flash')).toContainText('Revision saved.');
  await page.getByTestId('revision-submit').click();
  await page.getByTestId('revision-submit-confirm').click();
  await expect(page.getByTestId('revision-status-badge')).toHaveText('Revision pending review');
}

test('a revision keeps the published version visible until it is approved, and survives a rejection', async ({
  browser,
}) => {
  const unique = Date.now();
  const original = `Original title ${unique}`;
  const rejectedTitle = `First attempt ${unique}`;
  const finalTitle = `Final title ${unique}`;
  const articleId = await publishedArticle(admin, author, original);

  const authorPage = await browser.newPage();
  const adminPage = await browser.newPage();
  await signInAsAuthor(authorPage, author.email, AUTHOR_PASSWORD);
  await signInAsAdmin(adminPage);

  // The author edits the published article: the change goes to review, the published version stays.
  await submitRevision(authorPage, articleId, rejectedTitle);
  await authorPage.goto(`${AUTHOR_PORTAL_URL}/feed/${articleId}`);
  await expect(authorPage.getByTestId('feed-article-title')).toHaveText(original);

  // The administrator sees a revision (not a new article), next to the published version, and rejects it.
  await adminPage.goto(`${ADMIN_PORTAL_URL}/reviews`);
  const row = adminPage.getByRole('row', { name: new RegExp(rejectedTitle) });
  await expect(row.getByTestId('review-row-kind')).toHaveText('Revision');
  await row.getByRole('link', { name: rejectedTitle }).click();
  await expect(adminPage.getByTestId('revision-review-published-title')).toHaveText(original);
  await expect(adminPage.getByTestId('revision-review-proposed-title')).toHaveText(rejectedTitle);
  await adminPage.getByTestId('revision-review-reject').click();
  await adminPage.getByLabel(/reason/i).fill('The new title does not match the article.');
  await adminPage.getByTestId('revision-reject-confirm').click();
  await expect(adminPage.getByTestId('review-flash')).toContainText('The published version is unchanged.');

  // The feed still shows the published version; the author sees why the revision was rejected.
  await authorPage.goto(`${AUTHOR_PORTAL_URL}/feed/${articleId}`);
  await expect(authorPage.getByTestId('feed-article-title')).toHaveText(original);
  await authorPage.goto(`${AUTHOR_PORTAL_URL}/articles/${articleId}/revision`);
  await expect(authorPage.getByTestId('revision-review-reason')).toHaveText(
    'The new title does not match the article.',
  );

  // The author reworks and resubmits it; the administrator approves it.
  await authorPage.getByTestId('revision-title-input').fill(finalTitle);
  await authorPage.getByTestId('revision-submit').click();
  await authorPage.getByTestId('revision-submit-confirm').click();
  await expect(authorPage.getByTestId('revision-status-badge')).toHaveText('Revision pending review');

  await adminPage.goto(`${ADMIN_PORTAL_URL}/reviews`);
  await adminPage
    .getByRole('row', { name: new RegExp(finalTitle) })
    .getByRole('link', { name: finalTitle })
    .click();
  await expect(adminPage.getByTestId('revision-review-history')).toContainText('Submission 1: Rejected');
  await adminPage.getByTestId('revision-review-approve').click();
  await adminPage.getByTestId('revision-review-approve-confirm').click();
  await expect(adminPage.getByTestId('review-flash')).toContainText('It replaced the published version.');

  // Now the feed and the article show the new version.
  await authorPage.goto(`${AUTHOR_PORTAL_URL}/feed/${articleId}`);
  await expect(authorPage.getByTestId('feed-article-title')).toHaveText(finalTitle);
  await authorPage.goto(`${AUTHOR_PORTAL_URL}/articles/${articleId}`);
  await expect(authorPage.getByTestId('article-view-title')).toHaveText(finalTitle);
  await expect(authorPage.getByTestId('article-revise')).toBeVisible();

  await authorPage.close();
  await adminPage.close();
});

test('two administrators deciding on the same revision: one succeeds, the other is told it changed', async ({
  browser,
}) => {
  const unique = Date.now();
  const original = `Contested original ${unique}`;
  const revised = `Contested revision ${unique}`;
  const articleId = await publishedArticle(admin, author, original);

  const authorPage = await browser.newPage();
  await signInAsAuthor(authorPage, author.email, AUTHOR_PASSWORD);
  await submitRevision(authorPage, articleId, revised);

  const first = await browser.newPage();
  const second = await browser.newPage();
  await signInAsAdmin(first);
  await signInAsAdmin(second);
  for (const page of [first, second]) {
    await page.goto(`${ADMIN_PORTAL_URL}/reviews`);
    await page.getByRole('row', { name: new RegExp(revised) }).getByRole('link', { name: revised }).click();
    await expect(page.getByTestId('revision-review-proposed-title')).toHaveText(revised);
  }

  await first.getByTestId('revision-review-approve').click();
  await first.getByTestId('revision-review-approve-confirm').click();
  await expect(first.getByTestId('review-flash')).toContainText('It replaced the published version.');

  await second.getByTestId('revision-review-approve').click();
  await second.getByTestId('revision-review-approve-confirm').click();
  await expect(second.getByTestId('revision-review-conflict-message')).toContainText('current status: Approved');
  await second.getByTestId('revision-review-refresh').click();
  await expect(second.getByTestId('revision-review-status')).toHaveText('Approved');
  await expect(second.getByTestId('revision-review-approve')).toHaveCount(0);

  await authorPage.goto(`${AUTHOR_PORTAL_URL}/feed/${articleId}`);
  await expect(authorPage.getByTestId('feed-article-title')).toHaveText(revised);

  await Promise.all([authorPage.close(), first.close(), second.close()]);
});
