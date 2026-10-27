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

## Provisioning the initial administrator

Step-by-step guide: [admin-provisioning.md](admin-provisioning.md). The first administrator is created only by `db/scripts/provision-admin.sql`; public registration can never create administrators. The script takes three `psql` variables and has no values of its own:

| Variable | Meaning |
|---|---|
| `admin_email` | Sign-in email (valid, max 254). Stored trimmed; the unique `normalized_email` is `lower(trim(email))` |
| `admin_name` | Full name, 2-100 characters |
| `admin_password_hash` | Identity V3 hash from `ContentReview.Tools hash-password` (see [password-hashing.md](password-hashing.md)) |

Behavior:

- Runs in one transaction with `ON_ERROR_STOP`. A missing or malformed variable raises an error (`psql` exits with 3) and nothing is written. Error messages never echo the hash.
- No user with that normalized email: inserts an `Active` `Administrator` (`gen_random_uuid()` id, `now()` timestamps) and one `audit_events` row `AdministratorProvisioned` (no actor, `entity_type` `User`, details `{"email": ...}`; no password or hash).
- A user with that email exists: nothing is inserted or updated. The script prints `NOTICE: Administrator already exists - no changes made` (or a `WARNING` naming the role when the existing user is not an administrator). The existing password is never overwritten.

### Wrappers

`scripts/provision-admin.sh` (macOS/Linux/Git Bash) and `scripts/provision-admin.ps1` (Windows PowerShell 5.1 / PowerShell 7) read `ADMIN_EMAIL` and `ADMIN_NAME` from `.env` (or `--email`/`--name`, `-Email`/`-Name`), ask for the password in the masked `hash-password` prompt, and run the SQL through `docker compose exec postgres psql`:

```bash
cp .env.example .env        # once; set ADMIN_EMAIL and ADMIN_NAME, leave the password out
scripts/start.sh            # or: docker compose up -d
scripts/provision-admin.sh
```

Options: `--connection` / `-Connection` (or `PSQL_CONNECTION`) runs the local `psql` client against another database instead of the container; `ADMIN_PASSWORD_HASH` skips the prompt when you already have a hash.

Running `psql` directly:

```bash
HASH=$(dotnet run --project backend/tools/ContentReview.Tools -- hash-password)
psql "$CONNECTION" -v ON_ERROR_STOP=1 -v admin_email="$ADMIN_EMAIL" -v admin_name="$ADMIN_NAME" \
     -v admin_password_hash="$HASH" -f db/scripts/provision-admin.sql
```

The hash is passed as a command-line argument, so it is visible in the process list while `psql` runs; it is a salted one-way hash, but do not paste it into shared logs.

Tests: `backend/tests/ContentReview.IntegrationTests/Scripts/ProvisionAdminScriptTests.cs` runs the script with `psql` against a migrated Testcontainers database (creation, audit event, idempotency and password preservation, existing non-administrator, invalid input writes nothing, no hash in error messages).
