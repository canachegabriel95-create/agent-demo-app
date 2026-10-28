import type { Locator, Page } from '@playwright/test';
import { portals } from '../../baseTest/login';

/** Author Portal > Feed (/feed). */
export class FeedPage {
  readonly cardTitles: Locator;
  private readonly searchInput: Locator;
  private readonly categorySelect: Locator;

  constructor(private readonly page: Page) {
    this.cardTitles = page.locator('[data-testid^="feed-card-"] h2');
    this.searchInput = page.getByTestId('feed-search');
    this.categorySelect = page.getByTestId('feed-category');
  }

  async open(): Promise<void> {
    await Promise.all([this.waitForFeed(), this.page.goto(`${portals.author.url}/feed`)]);
  }

  /** Types into the search box and waits for the feed request with that search (the page debounces typing). */
  async search(text: string): Promise<void> {
    await Promise.all([this.waitForFeed('search', text), this.searchInput.fill(text)]);
  }

  async filterByCategory(name: string): Promise<void> {
    await Promise.all([this.waitForFeed('categoryId'), this.categorySelect.selectOption({ label: name })]);
  }

  card(title: string): Locator {
    return this.page.locator('[data-testid^="feed-card-"]').filter({ hasText: title });
  }

  /** Waits for GET /api/feed; with a param, only for the request that carries it (and its value, if given). */
  private waitForFeed(param?: string, value?: string) {
    return this.page.waitForResponse((response) => {
      const url = new URL(response.url());
      if (url.pathname !== '/api/feed' || response.request().method() !== 'GET') return false;
      if (!param) return true;
      return value === undefined ? url.searchParams.has(param) : url.searchParams.get(param) === value;
    });
  }
}
