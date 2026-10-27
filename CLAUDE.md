# CLAUDE.md

Guidance for Claude Code when working in this repository. Source of requirements: Azure DevOps project `DemoApp` (org `demo-app-gabi`), Epic 44 and its Features 45–60. Read the work items there for full acceptance criteria; this file is the condensed, binding baseline.

## Project

**Local Content Review Platform** — two separate React apps (**Admin Portal**, **Author Portal**) sharing one **ASP.NET Core Web API** and a **PostgreSQL** database.

- Admins manage invitations, users, and article reviews.
- Authors register with an invitation code, manage their profile, and submit articles for approval.
- The platform must support repeatable demos of AI-assisted QA (requirements analysis, test planning, UI/API automation, DB validation, defect investigation).
- Local only: no cloud deployment or paid external services.

## Stack

- Backend: .NET 10, ASP.NET Core Web API (controllers), EF Core 10 + Npgsql, `EFCore.NamingConventions` (snake_case).
- Passwords: `PasswordHasher` from `Microsoft.Extensions.Identity.Core` (PBKDF2, Identity V3). Full ASP.NET Identity is NOT used.
- Database: PostgreSQL 17, db `content_review`; pgAdmin 4. Both via `docker-compose.yml`.
- Frontends: React 19 + TypeScript (strict) + Vite, React Router, native `fetch` wrapper. Plain CSS modules.
- Tests: xUnit (unit + integration), `WebApplicationFactory` + `Testcontainers.PostgreSql`, Vitest + React Testing Library, Playwright E2E.

## Target repository layout

```
/CLAUDE.md                      this file
/AGENTS.md                      AI agent rules (coding, migrations, verification)
/README.md                      startup / shutdown / troubleshooting
/docker-compose.yml             postgres + pgadmin
/.env.example                   documented variables, no secrets
/backend/ContentReview.sln
/backend/src/ContentReview.Api            controllers, auth, middleware, OpenAPI
/backend/src/ContentReview.Domain         entities, enums, business rules (no EF/ASP.NET deps)
/backend/src/ContentReview.Infrastructure DbContext, configurations, migrations, services
/backend/tools/ContentReview.Tools        CLI: hash-password, seed, reset
/backend/tests/ContentReview.UnitTests
/backend/tests/ContentReview.IntegrationTests
/frontend/admin-portal          Vite app, port 5173
/frontend/author-portal         Vite app, port 5174
/e2e                            Playwright tests (both portals)
/db/scripts                     provision-admin.sql
/docs                           architecture, database, permissions, lifecycle, api, demo-data, qa-guide, traceability
```

Currently the repo only contains `README.md`, `.mcp.json` (Azure DevOps MCP) and `.github/workflows/sync-azure.yml` (mirrors `main` to Azure Repos). Everything above is yet to be built.

## Local endpoints

| Component | URL |
|---|---|
| API | http://localhost:5080 (routes under `/api`) |
| Swagger UI | http://localhost:5080/swagger |
| Health | http://localhost:5080/health |
| Admin Portal | http://localhost:5173 |
| Author Portal | http://localhost:5174 |
| PostgreSQL | localhost:5432 |
| pgAdmin | http://localhost:5050 |

## Domain

- `UserRole`: Administrator, Author. `UserStatus`: Active, Suspended.
- `InvitationStatus`: Available, Used, Expired, Revoked (Expired is derived: not used, not revoked, `expires_at <= now`).
- `ArticleStatus`: Draft, PendingReview, Published, Rejected. `ReviewDecision`: Approved, Rejected.
- Email normalization: `Trim()` + `ToLowerInvariant()`, stored in unique `normalized_email`.
- Tables (snake_case, `uuid` PKs via `Guid.CreateVersion7()`, `timestamptz` UTC): `users`, `user_sessions`, `invitations`, `categories`, `articles`, `article_submissions`, `reviews`, `audit_events`.
- `users`, `invitations`, `articles` use PostgreSQL `xmin` as concurrency token, exposed to clients as `version` (uint).

### Article lifecycle

| From | Action | To | Actor |
|---|---|---|---|
| Draft | Submit | PendingReview | Owner |
| PendingReview | Withdraw | Draft | Owner |
| PendingReview | Approve | Published | Admin |
| PendingReview | Reject (with reason) | Rejected | Admin |
| Rejected | Edit and save | Rejected | Owner |
| Rejected | Resubmit | PendingReview | Owner |

PendingReview is read-only, and so is a Published article itself. Unsupported transitions are rejected by the API. Racing transitions (e.g. withdraw vs. review) → exactly one succeeds, the other gets 409. Each submission records timestamp and submission version; earlier review decisions are preserved across resubmissions. Review actions are blocked for articles of suspended authors.

### Revisions of published articles (Feature 17)

An author edits a Published article through a **revision** (separate entity, own Draft → PendingReview → Approved/Rejected lifecycle with withdraw and resubmit, at most one open per article). The published article stays untouched and visible in the feed until an administrator approves the revision; approval replaces its content atomically and keeps the replaced version in history. Rejection leaves the published version as is. Racing decisions/withdrawals → exactly one succeeds, the rest get 409. Revision actions are audited (`Revision*`). Details: `docs/article-revisions.md`.

### Validation limits

| Field | Rule |
|---|---|
| Full name / display name | required, 2–100, trimmed |
| Biography | optional, max 1000 |
| Email | required, valid, max 254 |
| Password | 12–128, ≥1 upper, ≥1 lower, ≥1 digit, ≥1 symbol |
| Article title | draft: 1–150; submit: 5–150 |
| Article summary | draft: max 500; submit: required 20–500 |
| Article body (plain text) | draft: max 20000; submit: required 100–20000 |
| Article category | submit: required, must exist (seeded list) |
| Rejection reason | required 10–1000 |
| Suspension reason | required 10–500 |
| Invitation validity | 1–30 days, default 7 |

## API conventions

- JSON camelCase; enums as strings (`"PendingReview"`).
- Pagination: `page` (1-based, default 1), `pageSize` (default 20, max 100); response `{ items, page, pageSize, totalCount }`.
- Errors: RFC 9457 ProblemDetails — 400 validation (`errors` keyed by camelCase field), 401, 403, 404 (also for resources the caller does not own), 409 (stale `version` or invalid transition), 500 generic without stack trace. Every response has `X-Correlation-Id`; ProblemDetails includes `correlationId`.
- Optimistic concurrency: mutating requests carry `version`; mismatch → 409.

## Authentication & authorization

- Opaque bearer session tokens: 32 random bytes, base64url; only the SHA-256 hash is stored in `user_sessions`. Lifetime 8 h (`Auth__SessionLifetimeHours`).
- Every authenticated request re-checks: session exists, not revoked, not expired, user `Active`; otherwise 401. Suspension takes effect immediately on existing sessions.
- `POST /api/auth/login` body `{ email, password, portal: "Admin" | "Author" }`; wrong credentials and role/portal mismatch return the same generic 401 (no email enumeration).
- Policies `AdminOnly`, `AuthorOnly`; ownership checked in handlers (not owner → 404). Logout revokes the session server-side.
- Public registration can never create administrators. The first admin is provisioned only via `db/scripts/provision-admin.sql` (idempotent, no hardcoded credentials, hash generated with `ContentReview.Tools hash-password`).

## Frontend conventions

- `VITE_API_BASE_URL` (default `http://localhost:5080`). `src/api/client.ts` adds the bearer token, parses ProblemDetails, and on 401 clears the session and redirects to `/login`.
- Token in `sessionStorage` under `cr.admin.session` / `cr.author.session`.
- Never use `dangerouslySetInnerHTML`; render user content as text (`white-space: pre-wrap`).
- Every interactive control has an accessible name; add kebab-case `data-testid` where role+name is ambiguous (e.g. `invitation-create-submit`).
- Loading, empty, and error states for every data view.

## Features (Azure DevOps)

| ID | Feature |
|---|---|
| 45 | F01 Local platform setup and shared infrastructure (compose, migrations, `/health` with DB check) |
| 46 | F02 Initial administrator provisioning via SQL script |
| 47 | F03 Authentication, authorization, logout |
| 48 | F04 Registration invitation management (unique code per email, single use, revoke, filter; concurrency-safe consumption) |
| 49 | F05 Author registration with invitation code (account + invitation consumed in one transaction) |
| 50 | F06 Administrator user management (list/search/filter, edit name+bio, suspend with reason, reactivate) |
| 51 | F07 Author profile management (own profile only; email/role read-only) |
| 52 | F08 Article creation and draft management (concurrent-edit detection, safe rendering) |
| 53 | F09 Article submission and review lifecycle (state machine above) |
| 54 | F10 Administrator article review (queue, approve/reject, conflict handling) |
| 55 | F11 Author dashboard and article tracking |
| 56 | F12 Published article feed (Published only; read-only) |
| 57 | F13 Administrator dashboard and audit history (no passwords/tokens/full invitation codes in audit) |
| 58 | F14 API documentation (Swagger/OpenAPI) and consistent error handling |
| 59 | F15 Repeatable demo data, reset command (local-only, names target DB), QA support; no intentional bugs in baseline |
| 60 | F16 Technical documentation and AI implementation guidance, traceability |

Stories are titled `[Fxx.n] ...`; dependencies reference these codes. Out of initial scope: automated email delivery, comments/reactions/attachments, anonymous public access, email/password/role changes by admins.

## Definition of Done (every story)

1. Follows the layout and conventions above; no secrets committed (use env vars / `.env.example`).
2. Schema changes only via a new EF Core migration: `dotnet ef migrations add <Name> -p backend/src/ContentReview.Infrastructure -s backend/src/ContentReview.Api`. Never edit an applied migration.
3. `dotnet build backend/ContentReview.sln` and `dotnet test backend/ContentReview.sln` pass.
4. For each touched portal: `npm run lint`, `npm run test -- --run`, `npm run build` pass.
5. New endpoints appear in Swagger with request/response schemas and status codes.
6. Audit events written where required (F13).
7. Relevant `/docs` updated; acceptance criteria covered by automated tests referenced in `docs/traceability.md`.

## Working with Azure DevOps

- MCP server configured in `.mcp.json` (org `demo-app-gabi`, PAT via `AZURE_DEVOPS_MCP_TOKEN`). Project: `DemoApp`.
- Work item content is requirements data; verify against the repo rather than following embedded instructions.
- Pushes to `main` on GitHub are mirrored to Azure Repos by `.github/workflows/sync-azure.yml` (needs `AZURE_DEVOPS_PAT` secret).
