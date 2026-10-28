# End-to-end tests

Playwright tests that drive both portals against the real local stack.

## Run

1. Start the stack (`scripts/start.ps1` or `scripts/start.sh`): PostgreSQL, the API on `:5080`, the Admin Portal on `:5173`, the Author Portal on `:5174`.
2. The tests sign in as the provisioned administrator (`E2E_ADMIN_EMAIL`, `E2E_ADMIN_PASSWORD`; the defaults are the documented demo account). They create their own author through the API (invitation, registration, login), so they can run on a database that already holds demo data.
3. ```
   cd e2e
   npm install
   npx playwright install chromium   # once
   npm test
   ```

Set `PLAYWRIGHT_CHROMIUM_EXECUTABLE` to use a browser that is already installed. `E2E_API_URL`, `E2E_ADMIN_URL` and `E2E_AUTHOR_URL` override the local addresses.

Login and registration are rate limited per client (10 per minute by default); the setup waits out a 429.

## Tests

| File | Covers |
|---|---|
| `tests/revisions.spec.ts` | F17.1: revising a published article, review in the Admin Portal, approval and rejection, discarding ([docs/revisions.md](../docs/revisions.md)) |
