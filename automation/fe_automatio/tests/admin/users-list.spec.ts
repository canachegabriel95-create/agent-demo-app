import { randomUUID } from 'node:crypto';
import { test, expect } from '../support/fixtures';

const tag = () => randomUUID().slice(0, 8);
const row = (page: import('@playwright/test').Page, id: string) => page.getByTestId(`user-row-${id}`);

test('lists authors with their name, email and status', async ({ page, admin, newUser }) => {
  const active = await newUser({ role: 'Author', fullName: `Active ${tag()}` });
  const suspended = await newUser({ role: 'Author', status: 'Suspended', fullName: `Suspended ${tag()}` });

  await page.goto('/users');

  await expect(row(page, active.id)).toContainText(active.email);
  await expect(row(page, active.id)).toContainText('Active');
  await expect(row(page, suspended.id)).toContainText('Suspended');
  await expect(row(page, admin.user.id)).toHaveCount(0);
});

test('searches by name and by email', async ({ page, admin, newUser }) => {
  void admin;
  const key = tag();
  const target = await newUser({ role: 'Author', fullName: `Findme ${key}` });
  const other = await newUser({ role: 'Author', fullName: `Other ${tag()}` });
  await page.goto('/users');

  await page.getByLabel('Search').fill(key);
  await expect(row(page, target.id)).toBeVisible();
  await expect(row(page, other.id)).toBeHidden();

  await page.getByLabel('Search').fill(other.email);
  await expect(row(page, other.id)).toBeVisible();
  await expect(row(page, target.id)).toBeHidden();
});

test('filters by status', async ({ page, admin, newUser }) => {
  void admin;
  const active = await newUser({ role: 'Author', fullName: `Active ${tag()}` });
  const suspended = await newUser({ role: 'Author', status: 'Suspended', fullName: `Suspended ${tag()}` });
  await page.goto('/users');

  await page.getByLabel('Status').selectOption('Suspended');

  await expect(row(page, suspended.id)).toBeVisible();
  await expect(row(page, active.id)).toBeHidden();
});

test('paginates 20 authors per page', async ({ page, admin, newUser }) => {
  void admin;
  for (let i = 0; i < 21; i++) await newUser({ role: 'Author', fullName: `Page ${tag()}` });

  await page.goto('/users');
  await expect(page.getByTestId(/^user-row-/)).toHaveCount(20);

  await page.getByRole('button', { name: 'Next page' }).click();
  await expect(page.getByTestId(/^user-row-/)).toHaveCount(1);
});

test('clicking an author opens the detail page', async ({ page, admin, newUser }) => {
  void admin;
  const author = await newUser({ role: 'Author', fullName: `Detail ${tag()}` });
  await page.goto('/users');

  await row(page, author.id).getByRole('link').click();

  await expect(page).toHaveURL(`/users/${author.id}`);
});

test('shows the empty state when nothing matches', async ({ page, admin }) => {
  void admin;
  await page.goto('/users');

  await page.getByLabel('Search').fill(`nobody-${randomUUID()}`);

  await expect(page.getByText('No authors match the filters')).toBeVisible();
});

test('shows a loading state until the authors arrive', async ({ page, admin }) => {
  void admin;
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => (release = resolve));
  await page.route(/\/api\/admin\/users(\?.*)?$/, async (route) => {
    await gate;
    await route.continue();
  });

  await page.goto('/users');

  await expect(page.getByText('Loading authors…')).toBeVisible();
  release();
  await expect(page.getByText('Loading authors…')).toBeHidden();
});

test('shows an error state when the authors cannot be loaded', async ({ page, admin }) => {
  void admin;
  await page.route(/\/api\/admin\/users(\?.*)?$/, (route) => route.fulfill({ status: 500, json: { title: 'Server error' } }));

  await page.goto('/users');

  await expect(page.getByRole('alert')).toContainText('The authors could not be loaded');
});

test('passwords and hashes are never sent or shown', async ({ page, admin, newUser }) => {
  void admin;
  await newUser({ role: 'Author' });
  const response = page.waitForResponse(/\/api\/admin\/users(\?.*)?$/);

  await page.goto('/users');

  expect(await (await response).text()).not.toMatch(/password|AQAAAA/i);
  await expect(page.locator('body')).not.toContainText('AQAAAA');
});
