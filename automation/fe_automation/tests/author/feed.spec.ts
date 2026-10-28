import { test, expect } from '../../baseTest/baseTest';
import { FeedPage } from '../../pageObjects/author/FeedPage';
import { newFeedArticles } from '../../testData/articles';

let data: ReturnType<typeof newFeedArticles>;

test.beforeEach(async ({ db }) => {
  data = newFeedArticles();
  await db.runQuery('insertAuthor', [data.author.fullName, data.author.email, data.author.status]);
  for (const article of [data.technology, data.science]) {
    await db.runQuery('insertPublishedArticle', [
      data.author.email,
      article.title,
      article.summary,
      article.body,
      article.category,
    ]);
  }
});

test.afterEach(async ({ db }) => {
  await db.runQuery('deleteArticlesByAuthorEmail', [data.author.email]);
  await db.runQuery('deleteUsersByEmail', [[data.author.email]]);
});

test.describe('Author Portal - Feed', () => {
  test('shows the published articles', async ({ authorPage }) => {
    const feedPage = new FeedPage(authorPage);
    await feedPage.open();

    await expect(feedPage.card(data.technology.title)).toBeVisible();
  });

  test('search by title shows only the matching article', async ({ authorPage }) => {
    const feedPage = new FeedPage(authorPage);
    await feedPage.open();

    await feedPage.search(data.technology.title);

    await expect(feedPage.cardTitles).toHaveText([data.technology.title]);
  });

  test('category filter shows only articles from that category', async ({ authorPage }) => {
    const feedPage = new FeedPage(authorPage);
    await feedPage.open();
    await feedPage.search(data.tag);

    await feedPage.filterByCategory('Science');

    await expect(feedPage.cardTitles).toHaveText([data.science.title]);
  });
});
