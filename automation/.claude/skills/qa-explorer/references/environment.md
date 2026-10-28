# Environment and how to drive it

Read once at the start of a run. Facts here come from `CLAUDE.md` and `docs/`; if the live app
disagrees, the live app wins.

## Endpoints (all local)

| Component | URL | Session key in `sessionStorage` |
|---|---|---|
| Admin Portal | http://localhost:5173 | `cr.admin.session` |
| Author Portal | http://localhost:5174 | `cr.author.session` |
| API (routes under `/api`) | http://localhost:5080 | — |
| Swagger / OpenAPI | http://localhost:5080/swagger, `/swagger/v1/swagger.json` | — |
| Health (with DB check) | http://localhost:5080/health | — |
| PostgreSQL `content_review` | via the `postgres` MCP only (schema `public`) | — |

Start-of-run check: `GET /health` answers 200 and both portals load. If not, stop and ask the human to
start the stack (`scripts/start.ps1` or `npm run dev:all` in the repo root) — never start or stop it yourself.

## Browser sessions (`chrome-devtools-mcp`)

- One tab per portal and role. `list_pages` / `select_page` to switch. `sessionStorage` is per tab, so a
  second author needs its own tab (or sign the first one out).
- The human signs the **administrator** in by hand. Never ask for, accept or type the admin password.
- A run-created **author** (see `fixtures.md`) is signed in by putting the token from the API login into
  `sessionStorage` of an Author Portal tab, then reloading. This is a precondition shortcut and is not
  allowed when login itself (F03) is the behaviour under test — then use the login form.
- A `401` on any call clears the stored token and the portal goes to `/login` ("Your session has expired…").
  When that happens mid-run without a reason (suspension, logout), it is either a finding or an expired
  8 h session — check before filing.

## Calling the API (BE scenarios, setup, cleanup)

Use `evaluate_script` in a tab of the portal whose role you need, so the token never leaves the browser
and never appears in the transcript or a file:

```js
async () => {
  const token = sessionStorage.getItem('cr.admin.session'); // or 'cr.author.session'
  const r = await fetch('http://localhost:5080/api/admin/invitations', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({ email: 'qa.author.r1a2@example.com', validForDays: 1 }),
  });
  return { status: r.status, correlationId: r.headers.get('X-Correlation-Id'), body: await r.json().catch(() => null) };
}
```

- Never `return` the token, and strip `token` from a login response before returning it.
- Anonymous / wrong-role checks: omit the header, or call from the other portal's tab.
- Look up a request/response shape with the `swagger` MCP (`get_endpoint`, `get_request_schema`) before
  sending it; never dump the whole document.
- For an endpoint not in Swagger, `grep -n '^## \|^### ' docs/api.md` and read only that section.

## Conventions to judge results against

- JSON camelCase; enums as strings (`"PendingReview"`). Paging `{ items, page, pageSize, totalCount }`,
  `pageSize` max 100.
- Errors are ProblemDetails: 400 with `errors` keyed by camelCase field; 401; 403 (wrong role);
  **404 for someone else's resource** (never 403); 409 for a stale `version` or an invalid transition
  (carries `currentVersion` / `currentStatus` where documented); 500 without a stack trace.
  Every response has `X-Correlation-Id`, and ProblemDetails has `correlationId`.
- Mutations carry `version`; the lifecycle tables are in `CLAUDE.md` (articles) and `docs/revisions.md`.
- `allowedActions` on an article / revision is the API's statement of what the UI may offer — a button
  the UI shows that is not in `allowedActions` (or the reverse) is a finding.
- UI: user content renders as plain text (`white-space: pre-wrap`, never HTML); every control has an
  accessible name; ambiguous controls have a kebab-case `data-testid`; every data view has loading,
  empty and error states, and errors show the correlation id as "Reference".
- Audit events must never contain passwords, tokens or full invitation codes (only `codeSuffix`).

## Database (`postgres` MCP)

`SELECT` only, on `public`. Tables are snake_case: `users`, `user_sessions`, `invitations`,
`categories`, `articles`, `article_submissions`, `reviews`, `article_revisions`, `revision_reviews`,
`article_published_versions`, `audit_events`. Use `get_table_schema` before writing a query on a table
you have not used in this run. Never select `password_hash` or `token_hash` values; `count(*)` or
`IS NULL` checks on them are fine.
