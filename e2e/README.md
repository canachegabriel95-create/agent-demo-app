# End-to-end tests

Playwright tests that drive both portals against a running local stack.

1. Start PostgreSQL and the API (`http://localhost:5080`) and both portals (`http://localhost:5173`, `http://localhost:5174`), see the root README.
2. Provision an administrator (`scripts/provision-admin.sh`).
3. Run:

```bash
cd e2e
npm ci
npx playwright install chromium      # once
E2E_ADMIN_EMAIL=you@example.local E2E_ADMIN_PASSWORD='…' npx playwright test
```

`E2E_API_URL`, `E2E_ADMIN_PORTAL_URL` and `E2E_AUTHOR_PORTAL_URL` override the default local URLs. Credentials are read from the environment only; nothing is stored in the repository. The tests invite and register their own authors (unique emails), so they can be run repeatedly.

Current suite: `tests/article-revision.spec.ts` (revision and reapproval across both portals, including two administrators deciding at once). See `docs/traceability.md`.
