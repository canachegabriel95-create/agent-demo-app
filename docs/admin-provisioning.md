# Provisioning the initial administrator

The platform has no public way to create an administrator: registration only creates authors. The first administrator is created from the command line with `db/scripts/provision-admin.sql`, which is safe to run more than once. This guide takes you from an empty checkout to a verified administrator account.

Reference for the pieces used here: [password-hashing.md](password-hashing.md) (hash and password policy) and [database.md](database.md#provisioning-the-initial-administrator) (what the script does).

> **Signing in:** the login API exists (`POST /api/auth/login`, see [authentication.md](authentication.md)), so you can already check the account with `curl`. The Admin Portal login page is delivered by story F03.3; until then the portal at http://localhost:5173 cannot sign you in.

## 1. Prerequisites

- Docker, the .NET 10 SDK and the repository cloned (see [Run locally](../README.md#run-locally)).
- `.env` created from `.env.example` (`cp .env.example .env`, or `Copy-Item .env.example .env` in PowerShell).
- PostgreSQL running and **the migrations applied**. The easiest way is `scripts/start.sh` (or `scripts/start.ps1`): it starts PostgreSQL, then the API, which applies the migrations at startup (`Database__MigrateOnStartup`, on by default in Development). Check with:

  ```bash
  docker compose exec postgres psql -U content_review -d content_review -c "\dt"
  ```

  You should see `users`, `audit_events` and the other tables. If the list is empty, the migrations have not run; see [Database migrations](../README.md#database-migrations).

## 2. Choose the administrator

Set these two values in `.env` (they are git-ignored; never commit them):

```
ADMIN_EMAIL=you@example.com
ADMIN_NAME=Your Name
```

The email is stored as typed and matched case-insensitively. The name must be 2-100 characters. **The password is never written to `.env`.**

The password must follow the policy: 12-128 characters with an uppercase letter, a lowercase letter, a digit and a symbol.

## 3. Run the wrapper script (recommended)

```bash
scripts/provision-admin.sh          # macOS, Linux, Git Bash
```

```powershell
scripts/provision-admin.ps1         # Windows PowerShell 5.1 / PowerShell 7
```

The script reads `ADMIN_EMAIL` and `ADMIN_NAME`, asks for the password twice in a hidden prompt, generates the hash with `hash-password`, and runs the SQL inside the PostgreSQL container. Options: `--email`/`--name` (`-Email`/`-Name`) override `.env`; `--connection` (`-Connection`) targets another database with a local `psql`; `ADMIN_PASSWORD_HASH` skips the prompt if you already have a hash.

**Expected output, first run:**

```
Choose the administrator password (input is hidden).
Password:
Confirm password:
psql:<stdin>:86: NOTICE:  Administrator created
  [ok] Done. Sign in to the Admin Portal (http://localhost:5173) as you@example.com.
```

**Expected output, running it again** (same email; it asks for a password again but the new one is ignored):

```
psql:<stdin>:86: NOTICE:  Administrator already exists - no changes made
```

(With `--connection` the `psql:<stdin>:86:` prefix shows the script path instead; the message is the same.) Nothing is inserted or updated on a re-run, and the existing password is never overwritten. If the email belongs to a user that is not an administrator, you see a `WARNING ... already exists with role Author - no changes made` instead, and that user is left untouched.

If the input is invalid (a malformed email, a name that is too short, a hash that is not an Identity V3 hash) the script stops with `ERROR: ...`, `psql` exits with code 3 and nothing is written.

## 4. Or run the pieces yourself

Generate the hash (only the hash is printed to stdout; prompts go to stderr):

```bash
# bash: interactive, hidden prompt
HASH=$(dotnet run --project backend/tools/ContentReview.Tools -- hash-password)

# bash: from a variable or a secret store, without typing
read -rs PASSWORD
HASH=$(printf '%s\n' "$PASSWORD" | dotnet run --project backend/tools/ContentReview.Tools -- hash-password --stdin)
```

```powershell
# PowerShell: interactive
$hash = dotnet run --project backend/tools/ContentReview.Tools -- hash-password

# PowerShell: from a variable, without typing
$hash = $plainPassword | dotnet run --project backend/tools/ContentReview.Tools -- hash-password --stdin
```

A password that breaks the policy exits with code 1 and lists the rules; piped input needs `--stdin`.

Run the script with `psql` (inside the container, or any `psql` that can reach the database):

```bash
docker compose exec -T postgres psql -U content_review -d content_review -X -q -v ON_ERROR_STOP=1 \
  -v admin_email="$ADMIN_EMAIL" -v admin_name="$ADMIN_NAME" -v admin_password_hash="$HASH" \
  -f - < db/scripts/provision-admin.sql
```

```powershell
Get-Content -Raw db/scripts/provision-admin.sql | docker compose exec -T postgres psql -U content_review -d content_review -X -q `
  -v ON_ERROR_STOP=1 -v "admin_email=$env:ADMIN_EMAIL" -v "admin_name=$env:ADMIN_NAME" -v "admin_password_hash=$hash" -f -
```

The output is the same as in step 3.

## 5. Verify

In pgAdmin (http://localhost:5050, server **ContentReview Local**, database `content_review`, Query Tool) or with `psql`:

```sql
SELECT email, role, status FROM users WHERE role = 'Administrator';
```

Expected: one row with your email, `Administrator` and `Active`. The creation is also recorded:

```sql
SELECT action, entity_type, details FROM audit_events WHERE action = 'AdministratorProvisioned';
```

Expected: one row per administrator created (not one per run), with `{"email": "you@example.com"}` and no password or hash.

## 6. Check the login

With the API running (`scripts/start.sh`), use the email and password from step 3:

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5080/api/auth/login \
  -H 'Content-Type: application/json' -d '{"email":"you@example.com","password":"<password>","portal":"Admin"}'   # 200
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5080/api/auth/login \
  -H 'Content-Type: application/json' -d '{"email":"you@example.com","password":"<password>","portal":"Author"}'  # 401
```

An administrator can only sign in to the Admin portal; the same credentials on the Author portal return `401`.

## Security notes

- Credentials live only in your local, git-ignored `.env` (email and name) and in the hidden prompt (password). Do not put a password or hash in a commit, an issue or a chat. `.env.example` contains placeholders only.
- The hash is passed to `psql` as a command-line argument, so it is visible in the process list of your machine while the command runs. It is a salted one-way hash, not the password, but keep it out of shared logs.
- Re-running the script **never** changes an existing account or password, so it cannot be used to reset a password or to promote a user.
- Public registration can never create administrators; this script is the only way.

### Rotating the administrator password

There is deliberately no reset command in this story. If you must change the password, do it explicitly and knowingly:

1. Generate a new hash (step 4).
2. In a transaction, replace the hash and revoke the existing sessions, then check that each statement reports `UPDATE 1` (or the expected number of sessions) before committing:

   ```sql
   BEGIN;
   UPDATE users SET password_hash = '<new hash>', updated_at = now()
   WHERE normalized_email = lower('you@example.com') AND role = 'Administrator';
   UPDATE user_sessions SET revoked_at = now()
   WHERE revoked_at IS NULL AND user_id = (SELECT id FROM users WHERE normalized_email = lower('you@example.com'));
   COMMIT;
   ```

> **Warning:** this bypasses the application. It writes no audit event, and a wrong `WHERE` clause changes the wrong account. Use it only on your own local database, never paste the real hash into shared places, and prefer a rollback (`ROLLBACK;`) if the row count is not what you expect.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `ADMIN_EMAIL and ADMIN_NAME are required` | Set both in `.env` or pass `--email` and `--name`. |
| `Docker is not running` | Start Docker and `scripts/start.sh`, or use `--connection`. |
| `relation "users" does not exist` | Migrations have not been applied; start the API once (or run `dotnet ef database update`). |
| `admin_password_hash is not an Identity V3 password hash` | The value is not output of `hash-password` (a plain password, a truncated or quoted hash). Generate it again. |
| `The password does not meet the password policy` | Use 12-128 characters with upper case, lower case, a digit and a symbol. |
| `Standard input is redirected ... Use --stdin` | The hash tool needs a terminal for the hidden prompt; pipe the password with `--stdin` instead. |
| Re-run says `already exists` but you forgot the password | See [Rotating the administrator password](#rotating-the-administrator-password). |
