# Database

PostgreSQL 17, database `content_review`. The schema is owned by EF Core migrations in `backend/src/ContentReview.Infrastructure/Migrations`. Never edit the database by hand and never edit a migration that has been applied or merged; add a new one.

## Conventions

- Tables and columns are `snake_case` (`EFCore.NamingConventions`).
- Primary keys are `uuid`, generated in code with `Guid.CreateVersion7()`.
- Timestamps are `timestamptz`, always UTC.
- Enums are stored by name as `text`, each with a `CHECK` constraint listing the allowed values.
- `users`, `invitations` and `articles` use the PostgreSQL system column `xmin` as the optimistic concurrency token. It is mapped to the `Version` property (`uint`) and exposed to API clients as `version`. `xmin` is a system column, so it does not appear in the migration SQL.
- `InvitationStatus` is **not** stored. It is derived: Used and Revoked win, otherwise Expired when `expires_at <= now`, otherwise Available (`Invitation.GetStatus`).

## Tables

| Table | Purpose | Key constraints and indexes |
|---|---|---|
| `users` | Administrators and authors | unique `normalized_email`; index `(role, status)`; checks on `role`, `status` |
| `user_sessions` | Login sessions (only the SHA-256 token hash is stored) | unique `token_hash`; index `user_id`; FK to `users` **cascade** |
| `invitations` | Registration invitations | unique `code`; index `normalized_email`; unique `used_by_user_id` **where not null** (an account consumes at most one invitation); FKs to `users` restrict |
| `categories` | Article categories (seeded later) | unique `name`; unique `slug` |
| `articles` | Articles and their current state | index `(author_id, status)`; index `(status, submitted_at)`; check on `status`; FKs to `users` and `categories` restrict |
| `article_submissions` | Snapshot of each submission | unique `(article_id, version)`; FK to `articles` **cascade** |
| `reviews` | Review decisions | unique `(article_id, submission_version)` (a submission can be reviewed only once, which makes racing reviews safe); check on `decision`; FKs restrict |
| `audit_events` | Audit trail (`details` is `jsonb`) | index `occurred_at DESC`; index `action`; index `actor_user_id`; FK to `users` restrict |

Delete behavior: every foreign key is `RESTRICT`, except `user_sessions.user_id` and `article_submissions.article_id`, which cascade.

EF Core also adds plain indexes on foreign key columns that have no other index (for example `articles.category_id`, `reviews.reviewer_id`).

## Migrations

The EF Core tool is pinned in `.config/dotnet-tools.json`.

```bash
dotnet tool restore

# create a migration after changing the model
dotnet ef migrations add <Name> -p backend/src/ContentReview.Infrastructure -s backend/src/ContentReview.Api

# apply migrations to the database in ConnectionStrings__Default
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=content_review;Username=content_review;Password=<POSTGRES_PASSWORD>"
dotnet ef database update -p backend/src/ContentReview.Infrastructure -s backend/src/ContentReview.Api

# review the SQL that a migration will run
dotnet ef migrations script -p backend/src/ContentReview.Infrastructure -s backend/src/ContentReview.Api
```

`migrations add` never connects to the database. When `ConnectionStrings__Default` is not set, a credential-free placeholder is used (`ContentReviewDbContextFactory`), so no secret ever lives in source. `database update` needs the real variable.

The API can also apply migrations at startup: set `Database__MigrateOnStartup=true` (the default in Development).

## Guarding the schema

Integration tests in `backend/tests/ContentReview.IntegrationTests/Schema` apply the migrations to a throwaway PostgreSQL (Testcontainers) and verify the tables, snake_case columns, column types, unique and supporting indexes, foreign key delete behavior, check constraints and the `xmin` concurrency token. They also prove the constraints reject bad data. `The_model_has_no_changes_missing_from_the_migrations` fails when the model changes without a new migration.
