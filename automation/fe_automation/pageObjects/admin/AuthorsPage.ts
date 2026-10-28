import type { Locator, Page } from '@playwright/test';
import { portals } from '../../baseTest/login';
import type { AuthorStatus } from '../../testData/authors';

/** Admin Portal > Authors (/users). */
export class AuthorsPage {
  readonly table: Locator;
  readonly emailCells: Locator;
  private readonly searchInput: Locator;
  private readonly statusSelect: Locator;

  constructor(private readonly page: Page) {
    this.table = page.getByRole('region', { name: 'Authors table' });
    this.emailCells = this.table.locator('tbody tr td:nth-child(2)');
    this.searchInput = page.getByRole('searchbox', { name: 'Search' });
    this.statusSelect = page.getByLabel('Status');
  }

  async open(): Promise<void> {
    await Promise.all([this.waitForList(), this.page.goto(`${portals.admin.url}/users`)]);
  }

  /** Types into the search box and waits for the list request with that search (the page debounces typing). */
  async search(text: string): Promise<void> {
    await Promise.all([this.waitForList('search', text), this.searchInput.fill(text)]);
  }

  async filterByStatus(status: AuthorStatus): Promise<void> {
    await Promise.all([this.waitForList('status', status), this.statusSelect.selectOption(status)]);
  }

  row(email: string): Locator {
    return this.table.getByRole('row').filter({ hasText: email });
  }

  /** Waits for GET /api/admin/users; with a param, only for the request that carries that value. */
  private waitForList(param?: string, value?: string) {
    return this.page.waitForResponse((response) => {
      const url = new URL(response.url());
      if (url.pathname !== '/api/admin/users' || response.request().method() !== 'GET') return false;
      return !param || url.searchParams.get(param) === value;
    });
  }
}
