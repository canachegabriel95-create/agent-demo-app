# Test data and cleanup

The two non-negotiable rules: **each test creates its own data** and **each test removes all of it**.

## 1. Run tag

Every factory in `testData/` makes a fresh tag per call and puts it in every value it returns:

```ts
const tag = `qa${Date.now()}${Math.floor(Math.random() * 1000)}`;
// names: `Feed Author ${tag}`, titles: `Science article ${tag}`, emails: `feed.${tag}@example.test`
```

- Emails always end in `@example.test` (the explorer uses `example.com`, so automation data is told apart).
- The tag makes a test independent of every other record: filter the page by the tag (search box) when
  the test checks a list, so other data on the page cannot change the result.
- What may be read from existing data: the QA admin / QA author as the signed-in user and the seeded
  `categories` (look them up by name in SQL, never hardcode their ids). Nothing else.

## 2. Building data with SQL (default)

- One statement per file in `db/queries/`, positional parameters, a comment line listing them.
- Cast parameters that appear more than once (`$2::text`) — otherwise Postgres fails with
  "inconsistent types deduced for parameter".
- Ids: `gen_random_uuid()`. Times: `now()`. Fill every NOT NULL column with a realistic value that
  respects the app's limits (`CLAUDE.md` → Validation limits) and its state rules (e.g. a Published
  article has `published_at`, `submitted_at`, `published_version = 1`).
- `RETURNING id` when the test needs the id; look up related rows by the tagged email / title inside
  the SQL (`FROM users u WHERE u.normalized_email = lower(trim($1::text))`).
- Before writing an INSERT: `get_table_schema` and the constraints of the table (`postgres` MCP).

## 3. Building data through the API (only when SQL cannot)

A row inserted by SQL cannot sign in (the password hash is PBKDF2, made by the API). When the test needs
a user that signs in, create it through the API in `beforeEach`, still with tagged values:
1. As the QA admin (`process.env.ADMIN_SESSION_TOKEN`): `POST /api/admin/invitations` for the tagged email.
2. `POST /api/registration` with the code, the tagged name and a password generated in the test
   (satisfying the password rules); keep it in a variable, never log it.
3. `apiLogin('author', { email, password })`.
Check the exact routes and bodies with the `swagger` MCP first. Cleanup is still SQL (§4), and it must
also remove what the API created on the way (invitation, sessions, audit events).

## 4. Cleanup — delete order

`afterEach` removes children before parents (foreign keys are RESTRICT, except `article_submissions`,
which cascades from `articles`). Scope every DELETE to the test's tagged emails / titles or the ids it
created:

| Order | Table | Scope |
|---|---|---|
| 1 | `audit_events` | `actor_user_id` in the test users, or `entity_id` in the test's records (articles, users, invitations, revisions) — also events written by the QA admin acting on test records |
| 2 | `user_sessions` | `user_id` in the test users |
| 3 | `revision_reviews` | `revision_id` in the test revisions |
| 4 | `article_published_versions` | `article_id` in the test articles |
| 5 | `article_revisions` | `article_id` in the test articles |
| 6 | `reviews` | `article_id` in the test articles |
| 7 | `articles` | `author_id` in the test users (`article_submissions` cascade) |
| 8 | `invitations` | `email` / `normalized_email` tagged, or `used_by_user_id` in the test users |
| 9 | `users` | tagged `normalized_email` |

Only include the tables the test can touch. Re-check the list with `list_foreign_keys` when a test
touches a table not listed here.

**Side effects count.** If the action under test creates records (an invitation created through the UI,
a review, an audit event, a session), the cleanup deletes them too — find them by the tag or by the
test's ids, never by "the latest row".

## 5. Leftover check (after every run)

With the `postgres` MCP `run_select`, for each table the tests touched, e.g.:

```sql
SELECT
  (SELECT count(*) FROM public.users WHERE email LIKE '%@example.test') AS users,
  (SELECT count(*) FROM public.invitations WHERE email LIKE '%@example.test') AS invitations,
  (SELECT count(*) FROM public.articles WHERE title ~ 'qa[0-9]{13}') AS articles,
  (SELECT count(*) FROM public.audit_events WHERE details::text ~ 'qa[0-9]{13}') AS audit_events
```

Audit events by a test user cannot outlive that user (the foreign key blocks the user's delete), so the
`audit_events` check above catches the events written by the QA admin about test records. Add a check
by `entity_id` when the test knows the ids.

Every count must be `0`. A non-zero count means the cleanup is incomplete: fix the `afterEach` / the
delete query and re-run the tests (their cleanup removes the old rows only if it matches them — if not,
add a cleanup query scoped to `@example.test` and run it from a test's `afterEach`, never by hand).
Record the final counts in the task summary.
