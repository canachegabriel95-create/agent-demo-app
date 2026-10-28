# The automation project (`automation/fe_automation`)

Playwright + TypeScript, Chromium only, tests run in parallel. Read the existing files before adding
new ones; extend them rather than writing a second version.

## Layout

```
fe_automation/
  playwright.config.ts        loads .env, globalSetup, testDir ./tests
  .env / .env.example         URLs, QA accounts, POSTGRES_* (never read .env)
  baseTest/
    baseTest.ts               `test` with fixtures adminPage, authorPage (signed in), db (worker)
    globalSetup.ts            one API login per portal per run (the API allows 10 logins/minute)
    login.ts                  portals, credentials(), login() (the UI form), apiLogin(), useSession()
  db/
    db.ts                     runQuery<T>(name, params) runs db/queries/<name>.sql; closeDb()
    queries/*.sql             one statement per file, positional params $1, $2 …
  testData/*.ts               factories returning fresh, run-tagged data (newAuthors(), newFeedArticles())
  pageObjects/admin/*.ts      Admin Portal pages
  pageObjects/author/*.ts     Author Portal pages
  tests/admin/*.spec.ts       Admin Portal tests
  tests/author/*.spec.ts      Author Portal tests
```

Existing pieces to reuse (check with `Glob` — the list grows):
- Queries: `insertAuthor`, `deleteUsersByEmail`, `insertPublishedArticle`, `deleteArticlesByAuthorEmail`, `getUserByEmail`.
- Test data: `testData/authors.ts` (`newAuthors()`, types `Author`, `AuthorStatus`), `testData/articles.ts` (`newFeedArticles()`).
- Page objects: `admin/AuthorsPage` (`/users`), `author/FeedPage` (`/feed`).

## Naming

| What | Rule | Example |
|---|---|---|
| Spec | `tests/<portal>/<feature>.spec.ts`, one per page or feature | `tests/admin/invitations.spec.ts` |
| Test title | the Test Case title | `'Admin: revoke an available invitation'` |
| Test tags | `@PBI-<id>`, `@TC-<id>` | `{ tag: ['@PBI-48', '@TC-140'] }` |
| Page object | `pageObjects/<portal>/<Name>Page.ts`, class `<Name>Page` | `InvitationsPage` |
| Query | verb + noun, camelCase, `.sql` | `insertInvitation.sql`, `deleteInvitationsByEmail.sql` |
| Test data | `testData/<domain>.ts`, factory `new<Thing>()` | `newInvitation()` |

## Spec shape

```ts
import { test, expect } from '../../baseTest/baseTest';
import { InvitationsPage } from '../../pageObjects/admin/InvitationsPage';
import { newInvitation } from '../../testData/invitations';

let data: ReturnType<typeof newInvitation>;

test.beforeEach(async ({ db }) => {
  data = newInvitation();
  await db.runQuery('insertInvitation', [data.email, data.code]);
});

test.afterEach(async ({ db }) => {
  await db.runQuery('deleteInvitationsByEmail', [[data.email]]);
});

test.describe('Admin Portal - Invitations', () => {
  test('Admin: revoke an available invitation', { tag: ['@PBI-48', '@TC-140'] }, async ({ adminPage }) => {
    const invitationsPage = new InvitationsPage(adminPage);
    await invitationsPage.open();

    await invitationsPage.revoke(data.email);

    await expect(invitationsPage.status(data.email)).toHaveText('Revoked');
  });
});
```

- `beforeEach` / `afterEach` (not `beforeAll`): tests run in parallel on several workers, each test owns
  its data. `afterEach` runs also when the test fails.
- Arrange (open the page), act (one user action through the page object), assert (one `expect`).
- No `if`, loops or try/catch in a test. No hardcoded ids, emails or texts — they come from `testData`.

## Page objects

```ts
export class InvitationsPage {
  private readonly emailInput: Locator;

  constructor(private readonly page: Page) {
    this.emailInput = page.getByLabel('Email');
  }

  async open(): Promise<void> {
    await Promise.all([this.waitForList(), this.page.goto(`${portals.admin.url}/invitations`)]);
  }
}
```

- Locators in the constructor (or a method taking the row key, like `row(email)`), actions as methods.
  Assertions stay in the spec; page objects only expose locators and actions.
- Locator order: `getByRole(role, { name })` → `getByLabel` → `getByTestId` → CSS only for structure
  that has no role (e.g. `'[data-testid^="feed-card-"] h2'`).
- Portal URLs from `portals.admin.url` / `portals.author.url`, never typed.

## Waits

- Every action that loads data waits for **its** API response inside the page object:
  `await Promise.all([this.waitForX(...), <action>])`.
- Match the response by path and by the query parameter the action sets, not just the path. In dev
  mode React runs effects twice, so a page sends its first request twice; a wait that matches any
  response of the path can resolve on that duplicate and the next action then races the app.
  See `waitForList(param, value)` in `pageObjects/admin/AuthorsPage.ts`.
- Search boxes are debounced (300 ms): the wait for the search request covers it.
- Assertions retry by themselves (`toHaveText`, `toBeVisible`) — no extra waits before them.
- Never `waitForTimeout`, `setTimeout` or a retry loop.

## Signed-in users

- `adminPage` / `authorPage` fixtures: the QA admin / QA author, signed in once per run (`globalSetup`).
  Use them when the signed-in user only **acts** (reviews, searches, reads).
- When the signed-in author must **own** data (their articles, their profile), create a dedicated author
  in the test (invitation + registration through the API, `test-data-and-cleanup.md` §3), sign in with
  `apiLogin('author', { email, password })` + `useSession(page, 'author', token)`, and delete that
  author in `afterEach`. Do not attach test data to the shared QA author.
- `login(page, portal, user)` drives the real login form; use it only when the login itself is under test.
- Logins count against the 10/minute limit: never sign in per test unless the test needs its own user,
  and then do it once per test through the API.

## Run

From `automation/`:

```
npm --prefix fe_automation test -- --grep @PBI-<id> --repeat-each=3 --reporter=list   # new tests, 3x
npm --prefix fe_automation test -- --reporter=list                                       # full suite
```

A 429 at startup means the login limit was hit by runs in the last minute: wait for the minute to pass
(check `POST /api/auth/login` with an empty body until it is not 429), do not add retries to the code.
