# Backend API E2E tests

Playwright `request` fixture + TypeScript against the running API. No browser is used.

## Run

1. Start the API (`scripts/start.sh`) and provision an administrator (`scripts/provision-admin.sh`).
2. Login attempts are limited per IP (10/min by default) and every test signs in, so start the API with a higher
   limit when running the suite: `Auth__LoginAttemptsPerMinute=100000`.
3. Test data is deleted when the run ends (`global-setup.ts`), straight from the database, so `API_DB_CONNECTION`
   is required. Test users use the `@example.test` domain; the admin's sessions, invitations and audit events created
   during the run are removed by timestamp.
4. Export the variables from `.env.example`, then:

```bash
npm install
npm test
npm run lint
npm run typecheck
```

## Layout

- `fixtures.ts` - `adminApi` (signed-in administrator) and `newAuthor()` (invitation, registration and login through the API).
- `global-setup.ts` - records the run start and returns the teardown that cleans the database.
- `db.ts` - direct DB helper for what the API cannot do (expiring a session).
- `contract.ts` - `expectProblem` (ProblemDetails + correlation id) and `expectPage` (pagination envelope).
- `tests/` - one spec file per controller.
