# Backend API E2E tests

Playwright `request` fixture + TypeScript against the running API. No browser is used.

## Run

1. Start the API (`scripts/start.sh`) and provision an administrator (`scripts/provision-admin.sh`).
2. Login attempts are limited per IP (10/min by default) and every test signs in, so start the API with a higher
   limit when running the suite: `Auth__LoginAttemptsPerMinute=1000`.
3. Export the variables from `.env.example`, then:

```bash
npm install
npm test
npm run lint
npm run typecheck
```

## Layout

- `fixtures.ts` - `adminApi` (signed-in administrator) and `newAuthor()` (invitation, registration and login through the API).
- `contract.ts` - `expectProblem` (ProblemDetails + correlation id) and `expectPage` (pagination envelope).
- `tests/` - one spec file per controller.
