# Feature map

Pointers only — the content lives in the application repository (`../../../../../` from this file).
Use this to go from a story code to the few files and sections worth reading. If a row looks wrong,
the repo wins: grep `docs/` and note the mismatch as a gap.

Maintainers: after a story adds or renames a doc or an `api.md` section, update this table and run
`node .claude/skills/qa-planner/scripts/check-feature-map.mjs` (from `automation/`); it fails on stale entries.

`api.md §` is a `##` heading in `docs/api.md`. Read only that section: grep the heading to get its line,
then Read with `offset`/`limit` up to the next `## ` heading.

| Feature | Portal | Page doc(s) | `api.md §` | API routes | Cross-cutting docs |
|---|---|---|---|---|---|
| F01 Platform setup | — | `database.md` | — | `/health` | — |
| F02 Admin provisioning | — | `admin-provisioning.md`, `password-hashing.md` | — | — (SQL script `db/scripts/provision-admin.sql`) | `database.md` |
| F03 Auth / logout | Both | `authentication.md` | — (in `authentication.md`) | `/api/auth/*` | `permissions.md` |
| F04 Invitations | Admin | `invitations.md` | Invitations (administrator) | `/api/admin/invitations` | `audit.md` |
| F05 Registration | Author | `invitations.md` (`/register`), `business-rules.md` | Registration (anonymous) | `/api/registration`, `/api/registration/validate-invitation` | `authentication.md` |
| F06 User management | Admin | `user-management.md` | Users (administrator) | `/api/admin/users` | `authentication.md` (suspension ends sessions), `audit.md` |
| F07 Author profile | Author | `author-profile.md` | My profile (author) | `/api/me/profile` | `business-rules.md` |
| F08 Drafts | Author | `article-editor.md` | Categories; Articles (author) | `/api/articles`, `/api/categories` | `article-lifecycle.md`, `permissions.md` |
| F09 Submission lifecycle | Author | `article-editor.md` | Articles (author) → "Submit and withdraw", "Review history" | `/api/articles/{id}/submit`, `/withdraw`, `/reviews` | `article-lifecycle.md` |
| F10 Admin review | Admin | `admin-review.md` | Review queue (administrator) | `/api/admin/reviews/queue`, `/api/admin/articles/{id}`, `/api/admin/authors/lookup` | `article-lifecycle.md`, `audit.md` |
| F11 Author dashboard | Author | `author-dashboard.md`, `my-articles.md` | My articles (author) | `/api/me/articles`, `/api/me/articles/stats` | — |
| F12 Feed | Author | `feed.md` | Feed (author) | `/api/feed` | — |
| F13 Admin dashboard / audit | Admin | `admin-dashboard.md`, `audit-history.md` | Dashboard and audit history (administrator) | `/api/admin/dashboard`, `/api/admin/audit-events` | `audit.md` |
| F14 Swagger / errors | — | — | Swagger UI and OpenAPI document; Errors | all | `permissions.md` |
| F15 Demo data / reset | — | `demo-data.md` (not written yet) | — | — (`ContentReview.Tools`) | — |
| F16 Docs / traceability | — | `traceability.md`, `qa-guide.md` (not written yet) | — | — | — |

Cross-portal effects worth a scenario:
- F06 suspend → the author's sessions end immediately and review actions on their articles are blocked (F10).
- F10 approve / reject → the author sees the status and the rejection reason (F09, F11); approved articles appear in the feed (F12).
- F04 revoke / F05 register → the invitation status changes in the Admin Portal list.
- Every admin action in F04, F06 and F10 writes an audit event (F13).
