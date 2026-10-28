# Fixtures and cleanup

Read before the test-data preflight. The general constraints are in
[../../qa-planner/references/test-data-constraints.md](../../qa-planner/references/test-data-constraints.md)
— the same file the planner used, so both agents work from one set of facts.

## Run id and naming

Pick a short run id at the start (`r` + 4 random base36 chars, e.g. `r7k2q`) and put it in every record
you create, so leftovers are traceable and never confused with demo data:

| Record | Pattern |
|---|---|
| Author email | `qa.<role-or-purpose>.<run-id>@example.com` (e.g. `qa.owner.r7k2q@example.com`) |
| Author name | `QA <Purpose> <run-id>` |
| Article / revision title | `QA <run-id> <purpose>` (submit needs 5–150 chars) |
| Rejection / suspension reason | `QA <run-id>: <why>` padded to the required length |

Author passwords: generate one inside `evaluate_script` per account (meets 12–128, upper, lower, digit,
symbol) and use it for registration and the API login in the same script (credential rules: SKILL.md →
Guardrails). The account is then reachable only through the token you put in `sessionStorage`; that is intended.

## Recipes (API, via `evaluate_script` — see `environment.md`)

Only use records you created in this run. Never touch seeded / demo records or another run's leftovers.

| Need | Steps (role) |
|---|---|
| Active author | `POST /api/admin/invitations {email, validForDays: 1}` (Admin) → `POST /api/registration {invitationCode, fullName, email, password, confirmPassword}` (anonymous) → `POST /api/auth/login {email, password, portal: "Author"}` → token into `cr.author.session` |
| Draft | `GET /api/categories` → `POST /api/articles` (Author) |
| PendingReview article | Draft with submit-valid fields → `POST /api/articles/{id}/submit {version}` |
| Published article | PendingReview → `POST /api/admin/articles/{id}/approve {version, submissionVersion}` (Admin) |
| Rejected article | PendingReview → `POST /api/admin/articles/{id}/reject {version, submissionVersion, reason}`, reason 10–1000 (Admin) |
| Revision (any state) | Published article → `POST /api/articles/{articleId}/revisions` → `/api/revisions/{id}/submit` → admin `/api/admin/revisions/{id}/approve` or `/reject` |
| Suspended author | `POST /api/admin/users/{id}/suspend` with a 10–500 char reason (Admin); undo with `/reactivate {version}` |
| Available / revoked invitation | create; revoke with `POST /api/admin/invitations/{id}/revoke {version}` |

Check the exact route and body in Swagger before the first use in a run. One fixture per state-consuming
scenario: approve and reject each need their own PendingReview article; a race test needs a fresh one.

## What cleanup means here

The system has **no delete** for users, invitations, published or rejected articles, approved revisions,
reviews or audit events, and there is no reset command yet (F15). So "cleanup" is: return every
run-created record to the most neutral state the API allows, then record what remains.

| Created | Neutral end state (role) |
|---|---|
| Unused invitation | Revoked (Admin) |
| Draft article / Draft or Rejected revision | Deleted (owner) |
| PendingReview article / revision | Withdrawn, then deleted (owner) |
| Suspended author | Reactivated (Admin) — revoked sessions stay revoked |
| Author, used invitation, published / rejected article, approved revision, audit events | Stay; list their ids under residue |

- Run-tagged residue in this list is expected and does **not** block a scenario. Anything else that
  cannot be neutralised (for example a change to a seeded record) does: mark the scenario `Blocked`.
- A run-created published article stays in the feed that every author reads. Acceptable for the local
  demo environment; mention it in the residue so a demo presenter is not surprised.
- Verify each cleanup step (API response or `SELECT`) and write the residue table in
  `TEST_SCENARIOS.md` (template in `output-templates.md`).
