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

## Browser and logins

`chrome-devtools-mcp` is attached to the **QA Chrome** (`--browserUrl http://127.0.0.1:9222`), a Chrome
with its own profile that `tools/qa-login.mjs` starts. The helper and the MCP share its tabs.

Run from `automation/` with Bash, before the first browser call:

| Command | Effect | Output |
|---|---|---|
| `node tools/qa-login.mjs start` | starts the QA Chrome only (the login commands also start it when needed) | `OK QA Chrome …` |
| `node tools/qa-login.mjs admin` | signs the QA administrator in to the Admin Portal tab (API login, token into `cr.admin.session`) | `OK Admin Portal signed in as … (tab …)` |
| `node tools/qa-login.mjs admin --form` | same, through the real login form — use when the admin login form is under test (F03) | same |
| `node tools/qa-login.mjs author` | signs the optional fixed QA author in (only if the human configured one) | same |

- Exit 0 = OK, 1 = login refused (`FAILED Admin login: API answered 401` → ask the human to check the QA
  admin account), 2 = setup problem (Chrome or `.secrets/qa.env` missing → ask the human; never create
  or read that file).
- After the helper, `list_pages` and `select_page` the portal tab it named. Keep one tab per portal and
  role; `sessionStorage` is per tab, so a second author needs its own tab.
- On an unexpected `401` (session expired, 8 h) run the helper again. A `401` you caused on purpose
  (logout, suspension scenario) is the result to judge, not a reason to re-login first.
- Run-created **authors** (see `fixtures.md`) are signed in by their own in-browser script; the helper is
  only for the administrator and the optional fixed author.
- Credential rules: SKILL.md → Guardrails.

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

- Anonymous / wrong-role checks: omit the header, or call from the other portal's tab.
- Look up a request/response shape with the `swagger` MCP (`get_endpoint`, `get_request_schema`) before
  sending it; never dump the whole document.
- For an endpoint not in Swagger, use the `Grep` tool (pattern `^##`, `-n`) on `../docs/api.md` and
  `Read` only that section with `offset`/`limit`. Never use Bash for this.

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
you have not used in this run.
