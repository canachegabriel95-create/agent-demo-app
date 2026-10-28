# API conventions

Base URL `http://localhost:5080`, routes under `/api`. JSON is camelCase and enums are strings (`"PendingReview"`). The live endpoint reference is the Swagger UI (see below); this page describes the conventions every endpoint follows. Authentication is described in [authentication.md](authentication.md) and permissions in [permissions.md](permissions.md).

## Swagger UI and OpenAPI document

- Swagger UI: http://localhost:5080/swagger; OpenAPI JSON: http://localhost:5080/swagger/v1/swagger.json.
- Served anonymously only when `ASPNETCORE_ENVIRONMENT` is `Development` or `Demo`. In any other environment (Production) `/swagger…` answers 401 for anonymous callers.
- Operations are grouped by area (Auth, Registration, Admin – Invitations/Users/Reviews/Dashboard & Audit, Me, Articles, Feed, Categories, Health). Every operation has a summary; enums are strings; successes are `application/json`, errors are `application/problem+json` (except `/health`, which reports its checks as JSON).
- Request examples use demo data only (`jane.author@demo.local`, `Example-Passw0rd!`, `D3M0-0000-0001`).

### Authenticating in Swagger UI

1. Call `POST /api/auth/login` with a demo account and the right `portal` (`Admin` or `Author`).
2. Copy the `token` from the response.
3. Click **Authorize**, paste the token (without `Bearer `) and confirm.
4. Try `GET /api/auth/me`: it returns 200 with your profile. Logging out or suspension makes the token answer 401.

## Errors

Every error response is [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Resource not found.",
  "status": 404,
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
  "correlationId": "5b1c9b0e-6d0c-4c8e-9a43-0d1f6a5f1c11"
}
```

| Status | When | Title | Extra |
|---|---|---|---|
| 400 | Invalid input: a validation error or a body that does not bind | One or more validation errors occurred. | `errors`: messages per camelCase field name |
| 401 | No token, invalid, expired or revoked session, suspended user; wrong credentials at login; also any request that matches no endpoint while anonymous | Authentication is required. / Invalid email or password. | `WWW-Authenticate: Bearer` |
| 403 | Valid session, wrong role | You do not have permission to perform this action. | |
| 404 | The resource does not exist or is not the caller's | Resource not found. | no `detail`, so the two cases look the same |
| 405 | Wrong HTTP method for a route | Method Not Allowed | |
| 409 | Stale `version`, invalid lifecycle transition, duplicate value, already-used invitation | The request conflicts with the current state of the resource. | `detail` says what happened |
| 429 | Too many login or invitation-check attempts from one address | Too many attempts. Try again later. | `Retry-After` |
| 500 | Anything unexpected | An unexpected error occurred. | nothing else: no message, no stack trace, no inner exception, in every environment |

Validation errors look like this; field names are camelCase and nested names use dots (`nested.name`, `items[0].name`):

```json
{ "title": "One or more validation errors occurred.", "status": 400,
  "errors": { "fullName": ["Full name is required."], "count": ["The field Count must be between 1 and 10."] },
  "correlationId": "..." }
```

An anonymous request to a URL that does not exist is answered with 401, not 404, so the route table cannot be probed without signing in.

### Correlation id

Every response carries `X-Correlation-Id`, and every ProblemDetails repeats it as `correlationId`, so a user can quote one value and support can find every log line of that request.

- A client may send its own `X-Correlation-Id`. It is echoed only if it is 1-64 characters from `A-Z a-z 0-9 -`; otherwise the API ignores it and generates a GUID.
- The id is in the logging scope of the request (`CorrelationId=...`), so all log lines written while handling it include it.
- The portals show it as "Reference" next to error messages. CORS exposes the header to them.

### Logging rules

- An unhandled exception is logged at `Error` with its exception, on the server only.
- Passwords, tokens and `Authorization` headers are never logged, and request bodies of `/api/auth/*` and `/api/registration*` are never logged. The API does not log request headers or bodies at all; if HTTP logging is ever enabled it must exclude these.

### Throwing errors from application code

Controllers and services raise errors by throwing the exceptions in `ContentReview.Domain.Exceptions`; they never build error responses by hand.

| Exception | Result |
|---|---|
| `ValidationException(errors)` / `ValidationException(field, message)` | 400 with `errors` |
| `NotFoundException` | 404 (also for resources the caller does not own) |
| `ForbiddenException` | 403 |
| `ConflictException(message)`, `InvalidTransitionException(message)` | 409; the message is shown to the user, so write it as a sentence for them |
| `DbUpdateConcurrencyException` (stale `xmin` / `version`) | 409 "The resource was changed by someone else. Reload it and try again." |
| `DbUpdateException` caused by a unique violation (PostgreSQL 23505) | 409 "A resource with the same unique value already exists." |
| anything else | 500 |

Model-binding and `[ApiController]` validation errors need no code: they produce the same 400 shape.

## Code map

| Piece | Location |
|---|---|
| Correlation id middleware, accessor, id rules | `ContentReview.Api/Errors/CorrelationId*.cs` |
| Exception to status mapping | `ContentReview.Api/Errors/ExceptionHandlers.cs` |
| Model-state to camelCase `errors` | `ContentReview.Api/Errors/ModelStateErrors.cs` |
| Exceptions | `ContentReview.Domain/Exceptions` |
| Pipeline order | `Program.cs`: correlation id, exception handler, status code pages, CORS, authentication, authorization, rate limiter |

## Tests

`backend/tests/ContentReview.IntegrationTests/Errors/ErrorHandlingTests.cs` uses test-only endpoints (`ErrorProbes.cs`) that raise each kind of failure: status, title and shape for every row above, camelCase keys, no leaks in the 500 (also in Development), correlation id echo / replacement / length limit, the id in the log scope, no secrets in the logs, 404/405/401/403/429 shapes and CORS headers on errors.

## Invitations (administrator)

All routes are under `/api/admin/invitations` and need an Administrator token (no token: 401, Author token: 403).

| Method | Route | Body / query | Success |
|---|---|---|---|
| POST | `/api/admin/invitations` | `{ email, validForDays? }` (1-30 days, default 7) | 201 `InvitationDto` + `Location` |
| GET | `/api/admin/invitations` | `?email=&status=&page=&pageSize=` | 200 paged `InvitationDto`, newest first |
| GET | `/api/admin/invitations/{id}` | | 200 `InvitationDto` |
| POST | `/api/admin/invitations/{id}/revoke` | `{ version }` | 200 `InvitationDto` |

```json
{
  "id": "…", "code": "7KQ2-M9XD-3TWA", "email": "new.author@example.com", "status": "Available",
  "createdAt": "…", "createdBy": { "id": "…", "fullName": "Alice Admin" },
  "expiresAt": "…", "revokedAt": null, "revokedBy": null, "usedAt": null, "usedBy": null, "version": 1234
}
```

- **Codes** are 12 characters of the Crockford base32 alphabet (digits and letters without I, L, O, U) drawn from a cryptographic random generator and shown as `XXXX-XXXX-XXXX`. The full code is returned to administrators so it can be copied and shared; it is never written to audit events or logs.
- **Status** is derived in one place, `Invitation.GetStatus(now)`: Used if `usedAt` is set, otherwise Revoked if `revokedAt` is set, otherwise Expired if `expiresAt <= now`, otherwise Available. The `status` filter uses the same rules as SQL predicates, so paging and `totalCount` are exact.
- **Filters:** `email` matches any part of the invited address, ignoring case; `status` is `Available`, `Used`, `Expired` or `Revoked`. An unknown status, `page < 1` or a `pageSize` outside 1-100 is a 400.
- **Create:** a 400 lists `email` and/or `validForDays`; an email that already belongs to a user is a 409 ("A user with this email already exists."). Several Available invitations may exist for the same email.
- **Revoke:** only an Available invitation can be revoked. A Used, Expired or Revoked invitation, or a `version` that is no longer current, is a 409; so is the losing request of two simultaneous revokes. Reload the invitation and try again.
- **Audit:** `InvitationCreated` and `InvitationRevoked` with details `{ email, codeSuffix }` (last 4 characters). Until the audit history story (F13.1) provides the real writer, `IAuditWriter` is a no-op (`NoOpAuditWriter`); the integration tests replace it with a recorder and check what would be written.

Tests: `backend/tests/ContentReview.IntegrationTests/Invitations/AdminInvitationsTests.cs`, `backend/tests/ContentReview.UnitTests/Domain/InvitationCodeTests.cs`, `InvitationTests.cs`.

## Registration (anonymous)

### `POST /api/registration`

Creates an Author account with an invitation code. Anonymous, rate limited with the login limiter. The field rules are in [business-rules.md](business-rules.md#accounts).

```json
{ "invitationCode": "ABCD-EFGH-JKMN", "fullName": "Jane Doe", "email": "jane@demo.local", "password": "...", "confirmPassword": "..." }
```

- `201 { "id", "email", "fullName" }`. No session is created and no token is returned: the person signs in afterwards (`POST /api/auth/login`, portal `Author`).
- The role is **not** an input. A `role` in the body is ignored; every account created here is an Active Author.
- `400` ValidationProblemDetails with every invalid field at once, keyed in camelCase:

| Field | Messages |
|---|---|
| `invitationCode` | "Invitation code is required."; or the invitation failure below |
| `fullName` | "Full name is required."; "Full name must be between 2 and 100 characters." (after trimming) |
| `email` | "Email is required."; "Enter a valid email address of at most 254 characters."; or the mismatch below |
| `password` | "Password is required."; or one sentence per broken rule ("Must be between 12 and 128 characters long." and so on) |
| `confirmPassword` | "Confirm your password."; "Passwords do not match." |

- `400` for an invitation that cannot be used, on the field it belongs to:

| Reason | Field | Message |
|---|---|---|
| NotFound (unknown or malformed code) | `invitationCode` | This invitation code is not valid. Check the code or ask an administrator for a new one. |
| Expired | `invitationCode` | This invitation has expired. Ask an administrator for a new one. |
| Revoked | `invitationCode` | This invitation has been revoked. Ask an administrator for a new one. |
| AlreadyUsed | `invitationCode` | This invitation has already been used. |
| EmailMismatch | `email` | The email address does not match this invitation. |

- `409` "An account with this email already exists." (in `detail`). The code and email are checked first, so someone without a valid invitation for an address cannot use this response to find out whether it has an account.
- `429` when the rate limit is exceeded.

How it is done (`RegistrationService`): the fields are validated first (nothing is hashed for an invalid password); then, in one transaction at ReadCommitted, the user is inserted, `ConsumeAsync` uses the invitation, and the `UserRegistered` audit event (`userId`, `email`, `invitationId`; no password, no code) is recorded; then the transaction commits. Any failure rolls everything back, so no account exists and the invitation is still Available. When two requests race for one invitation, exactly one succeeds; the other is told the invitation was already used.

### `POST /api/registration/validate-invitation`

Body `{ code, email }`. Checks an invitation code and email without using the code up, so the registration form can warn early. Rate limited with the login limiter (`Auth:LoginAttemptsPerMinute` per client address, shared with `POST /api/auth/login`).

- `200 { "valid": true }`
- `400` ProblemDetails whose `title` is the sentence to show and whose `reason` is a stable code:

| `reason` | `title` |
|---|---|
| `NotFound` | This invitation code is not valid. Check the code or ask an administrator for a new one. |
| `EmailMismatch` | The email address does not match this invitation. |
| `Expired` | This invitation has expired. Ask an administrator for a new one. |
| `Revoked` | This invitation has been revoked. Ask an administrator for a new one. |
| `AlreadyUsed` | This invitation has already been used. |

A missing `code` or `email` is an ordinary validation 400 with `errors`. The email is checked before the status, so a wrong email never learns whether the code was used or revoked. The reasons do tell a caller that a code exists, which the story asks for; the rate limit is what slows guessing, and a code has 60 random bits.

### Consuming an invitation (used by registration)

`IInvitationRedemptionService` in `ContentReview.Infrastructure.Invitations` is the only code that may use an invitation.

```csharp
await using var transaction = await db.Database.BeginTransactionAsync(ct);
db.Users.Add(user);
await db.SaveChangesAsync(ct);
await redemption.ConsumeAsync(request.Code, request.Email, user.Id, ct); // throws InvitationNotRedeemableException
await transaction.CommitAsync(ct);
```

- `ConsumeAsync` runs one conditional `UPDATE` (`used_at IS NULL AND revoked_at IS NULL AND expires_at > now AND normalized_email = email`), so of any number of simultaneous calls for a code exactly one updates the row; the others find it used. It must run inside the transaction that creates the account (it throws `InvalidOperationException` otherwise), so a rolled-back registration leaves the invitation available.
- When no row was updated, the invitation is read again to find the exact `InvitationValidationResult` and `InvitationNotRedeemableException(reason)` is thrown; the API answers it as the same 400 ProblemDetails with `reason`.
- Codes are normalized (trimmed, upper-cased, dashes optional); the email is normalized with `Trim().ToLowerInvariant()`. "Now" comes from the injected `TimeProvider`, not the database clock, so tests control it.

Tests: `backend/tests/ContentReview.IntegrationTests/Invitations/InvitationRedemptionTests.cs` (10 parallel consumptions give exactly one winner; every reason; rollback; endpoint and rate limit).

## Users (administrator)

Administrators look up author accounts. Both routes need an Administrator token (no token: 401, Author token: 403). **Administrators are never listed or returned**: their ids give 404 like unknown ids.

| Method | Route | Success |
|---|---|---|
| GET | `/api/admin/users` | 200 paged `{ items, page, pageSize, totalCount }` |
| GET | `/api/admin/users/{id}` | 200 author details |
| PUT | `/api/admin/users/{id}/profile` | 200 updated author details |
| POST | `/api/admin/users/{id}/suspend` | 200 updated author details |
| POST | `/api/admin/users/{id}/reactivate` | 200 updated author details |

### List

Query: `search`, `status` (`Active` or `Suspended`), `sort` (`registeredAt` default, or `fullName`), `direction` (`asc` or `desc`; defaults to `desc` for `registeredAt`, so newest first, and `asc` for `fullName`), `page` (from 1), `pageSize` (1-100, default 20). An invalid value is a 400 with `errors`.

- **Search** matches any part of the full name or of the email, ignoring case (`ILIKE` on `full_name` and `normalized_email`). `%`, `_` and `\` in the search text are ordinary characters: searching `100%` finds only names that contain `100%`. At most 100 characters.
- Equal sort values are ordered by id, so paging never repeats or skips an author.
- Item: `{ id, fullName, email, status, registeredAt }`. No password data is ever returned.

### Details

```json
{
  "id": "…", "fullName": "Ann Author", "email": "ann@demo.local", "bio": "…", "role": "Author",
  "status": "Suspended", "registeredAt": "…", "suspendedAt": "…", "suspensionReason": "…",
  "articleCounts": { "draft": 3, "pendingReview": 1, "published": 2, "rejected": 1 },
  "version": 1234
}
```

The four counts come from one grouped query; statuses with no articles are `0`. `version` is the concurrency token the edit and suspension endpoints (F06.2, F06.3) expect.

Indexes: the list filters on role and status, which `users (role, status)` already covers (created with the initial schema). A trigram index for the contains-search was not added: the platform is local with small data, and `ILIKE '%…%'` is fast enough there. If the user table ever grows large, add `pg_trgm` GIN indexes on `full_name` and `normalized_email` with a new migration.

Tests: `backend/tests/ContentReview.IntegrationTests/Users/AdminUsersTests.cs`.

### Update profile

`PUT /api/admin/users/{id}/profile` changes an author's display name and biography.

```json
{ "fullName": "Ann B. Author", "bio": "Writes about testing.", "version": 1234 }
```

- `fullName` is required (trimmed, 2-100 characters). `bio` is optional (at most 1000 characters); null, empty or whitespace clears it. `version` is required and must be the one last read from the details.
- Only these fields can change. Email, role, status, password and any other property in the body are ignored. Active and Suspended authors can both be edited and keep their status.
- Responses: 200 with the updated details (new `version`); 400 with `errors` keyed `fullName`, `bio`, `version`; 401/403; 404 for unknown ids and for administrators (checked before the body); 409 when `version` is stale (the author was changed meanwhile; nothing is saved). Two simultaneous edits with one version: exactly one wins.
- A request that changes nothing succeeds without a new version or audit event.
- Audit: `AdminProfileEdited` with `{ targetUserId, changedFields }` (`fullName`, `bio`). The text itself is never written to the audit.

Tests: `backend/tests/ContentReview.IntegrationTests/Users/AdminEditProfileTests.cs`, `UserTests` (unit).

### Suspend

`POST /api/admin/users/{id}/suspend`

```json
{ "reason": "Posted spam links repeatedly.", "version": 1234 }
```

- `reason` is required (trimmed, 10-500 characters). `version` is required.
- In **one transaction**: status becomes `Suspended`, `suspensionReason` and `suspendedAt` are stored, **every session of the author is revoked**, and a `UserSuspended` audit event `{ targetUserId, reason }` is written. If any step fails nothing is kept.
- Effect: the author's next request with an old token is 401, and signing in fails with the same generic 401 as a wrong password (no hint that the account exists or is suspended).
- Articles, submissions and reviews are not touched.
- Responses: 200 with the updated details; 400 with `errors.reason` (missing, or not 10-500 characters) or `errors.version`; 400 with `errors.id` = "Administrator accounts cannot be suspended." for an administrator's id (a documented choice: unlike the read endpoints, which hide administrators with a 404, an explicit refusal tells the caller why nothing happens); 401/403; 404 for an unknown id; 409 for a stale `version` and for an author who is already suspended ("The account is already suspended."). Two simultaneous requests with one version: exactly one wins.

### Reactivate

`POST /api/admin/users/{id}/reactivate`, body `{ "version": 1234 }`.

- The status becomes `Active`; the reason and the time are cleared; a `UserReactivated` audit event `{ targetUserId }` is written.
- Sessions revoked by the suspension **stay revoked**: the author signs in again.
- Responses: 200 with the updated details; 400 for an administrator's id or a missing `version`; 401/403; 404 for an unknown id; 409 for a stale `version` and for an author who is not suspended ("The account is not suspended.").

### Defence in depth

Session validation already refuses suspended users. Services that change an author's content (articles, F08/F09) must additionally call `User.EnsureAuthorActive()`, which throws `ForbiddenException` (403) for a suspended account, so they do not depend on the session check alone.

Tests: `backend/tests/ContentReview.IntegrationTests/Users/AdminSuspensionTests.cs`, `UserTests` (unit).

## My profile (author)

An author reads and changes their **own** profile. Both routes need an Author token (no token: 401, Administrator token: 403, a suspended author's token: 401). The author is always the one the session belongs to: there is no id in the route, the query or the body, and no author endpoint returns another author's private profile (the public display name appears only in the feed, F12).

| Method | Route | Success |
|---|---|---|
| GET | `/api/me/profile` | 200 profile |
| PUT | `/api/me/profile` | 200 updated profile |

```json
{ "id": "…", "fullName": "Ann Author", "email": "ann@demo.local", "bio": "…", "role": "Author",
  "registeredAt": "…", "version": 1234 }
```

`bio` is `null` when empty. No password, status or suspension data is returned.

### Update

```json
{ "fullName": "Ann B. Author", "bio": "Writes about testing.", "version": 1234 }
```

- `fullName` is required (trimmed, 2-100 characters). `bio` is optional (at most 1000 characters); null, empty or whitespace clears it. `version` is required and must be the one last read.
- Only these two fields change. `email`, `role`, `status`, `password`, `id` and any other property in the body are ignored.
- Responses: 200 with the updated profile (new `version`); 400 with `errors` keyed `fullName`, `bio`, `version`; 401/403; 409 when `version` is stale (an administrator may have edited the profile meanwhile; nothing is saved). Two simultaneous updates with one version: exactly one wins.
- Text is stored as typed and returned as JSON data; the portals render it as text.
- The service (`IAuthorProfileService`) also refuses an administrator's id with 404, so the rule does not rest on the route policy alone.

Tests: `backend/tests/ContentReview.IntegrationTests/Me/AuthorProfileTests.cs`.

## Categories

`GET /api/categories` lists the article categories for the editor (author) and for filters (both portals).

- **Who:** any signed-in user, administrator or author. No token: 401. A suspended author's token: 401.
- **Response:** 200 `[{ "id": "…", "name": "Technology", "slug": "technology" }, …]`, in display order (`sort_order`): Technology, Science, Business, Health, Culture, Education.
- **Caching:** `Cache-Control: private, max-age=300`. The list never changes while the application runs, so the browser may reuse it for five minutes; `private` keeps it out of shared caches because the request carries a token.
- **Read-only:** there is no endpoint that creates, edits or deletes a category (`POST`, `PUT`, `PATCH` and `DELETE` on `/api/categories`, `/api/categories/{id}` and `/api/admin/categories` all answer 404 or 405). The data is described in [database.md](database.md#seed-data-categories).
- **Frontend:** both portals have `useCategories()` (`src/hooks/useCategories.ts`), which returns `{ status, categories, byId, error, reload }` with `status` `loading`, `ready` or `error`.

Tests: `backend/tests/ContentReview.IntegrationTests/Categories/CategoriesTests.cs`, `useCategories.test.tsx` in each portal.


## Articles (author)

An author creates, reads, edits and deletes their **own** articles while they are drafts. Every route needs an Author token (no token: 401, Administrator token: 403, a suspended author's token: 401). The owner is always the session's user; `authorId`, `status`, `id`, `createdAt` and any other property in a body are ignored. Listing and the review decisions arrive with F11 and F10.

| Method | Route | Success |
|---|---|---|
| POST | `/api/articles` | 201 `ArticleDto`, `Location: /api/articles/{id}` |
| GET | `/api/articles/{id}` | 200 `ArticleDto` |
| PUT | `/api/articles/{id}` | 200 `ArticleDto` (new `version`) |
| DELETE | `/api/articles/{id}?version=n` | 204 |
| GET | `/api/articles/{id}/reviews` | 200 `[{ submissionVersion, decision, reason, decidedAt }]` |
| POST | `/api/articles/{id}/submit` | 200 `ArticleDto` (PendingReview) |
| POST | `/api/articles/{id}/withdraw` | 200 `ArticleDto` (Draft) |

```json
{ "id": "…", "title": "My draft", "summary": "", "body": "",
  "category": { "id": "…", "name": "Science" },
  "status": "Draft", "submissionVersion": 0,
  "createdAt": "…", "updatedAt": "…", "submittedAt": null, "publishedAt": null,
  "latestReview": { "decision": "Rejected", "reason": "…", "decidedAt": "…" },
  "allowedActions": ["edit", "delete", "submit"], "version": 1234 }
```

`category` and `latestReview` are `null` when absent. `allowedActions` comes from the lifecycle table ([article-lifecycle.md](article-lifecycle.md)): Draft `edit, delete, submit`; PendingReview `withdraw`; Rejected `edit, submit`; Published none.

### Create and update

```json
{ "title": "My draft", "summary": "…", "body": "…", "categoryId": "…", "version": 1234 }
```

- Only `title` is required. Draft limits: title 1-150, summary at most 500, body at most 20000 (plain text, trimmed; NUL characters are removed). `categoryId` is optional but must exist. The stricter submit rules apply at submission (F09.2), not here.
- `version` is required on `PUT` and must be the one last read. `PUT` replaces all four content fields: omit `categoryId` or send null to clear it.
- Errors are reported all at once: 400 with `errors` keyed `title`, `summary`, `body`, `categoryId`, `version`.
- Content is stored as typed and returned as JSON data; the portals render it as text.

### Delete

Only a Draft can be deleted, and `version` (query) is required. Any review history the draft still carries from an earlier rejection is deleted with it, in the same transaction.

### Errors and the order they are checked

1. Not found, or owned by somebody else: **404** (identical, so ids cannot be probed).
2. Status does not allow the action (PendingReview or Published edit; anything but Draft delete): **409** `Article cannot be modified in status PendingReview.`
3. Invalid fields: **400**.
4. Stale `version`: **409** `The article was changed by someone else. Reload it and try again.` with an extra `currentVersion` property. Nothing is saved. Two simultaneous requests with one version: exactly one wins, the other gets this 409 (or 404 if the article was deleted).

The service (`IArticleService`) repeats the author checks (an administrator's id: 404, a suspended author: 403), so the rules do not rest on the route policy alone.

Tests: `backend/tests/ContentReview.IntegrationTests/Articles/ArticleDraftTests.cs`.

### Submit and withdraw

Both take `{ "version": n }` (required; the `version` last read) and answer 200 with the updated `ArticleDto`.

**`POST /api/articles/{id}/submit`** sends a Draft, or resubmits a Rejected article, for review. In one transaction it checks the submission rules, adds 1 to `submissionVersion`, sets `status = PendingReview` and `submittedAt`, and stores a snapshot row in `article_submissions` (version, title, summary, body, category, submitted time). Earlier snapshots and reviews are never touched, so a resubmission keeps the decisions of earlier versions.

| Field | Submission rule (after trimming; a body's minimum is counted after trimming too) |
|---|---|
| `title` | 5-150 characters |
| `summary` | 20-500 |
| `body` | 100-20000 |
| `categoryId` | required |

A 400 lists every broken field (`errors.title|summary|body|categoryId`, plus `errors.version`) and changes nothing.

**`POST /api/articles/{id}/withdraw`** takes a PendingReview article back: it becomes a Draft again (editable, deletable) and the current submission row gets `withdrawn_at`. `submissionVersion` stays; the next submit makes the next version.

**Checks, in order:** not found or not the caller's: 404; status does not allow the action: 409 `Article cannot be submitted in status Published.` / `Article cannot be withdrawn in status Draft.`; stale `version`: 409 with `currentVersion`; submission rules (submit only): 400.

**Races.** Every transition updates the article only if its `xmin` is still the one that was read (`WHERE id = @id AND xmin = @version`), and a unique index on `(article_id, version)` backs up the snapshot row. So of several simultaneous submits, withdraws or an administrator's decision on the same version exactly one succeeds; the others get 409 `The article has changed. Refresh to see its current status.` (or the stale-version 409 when they read after the change). A repeated request with the same version is a 409 and creates no second row.

**Audit.** `ArticleSubmitted` and `ArticleWithdrawn` are recorded with `{ articleId, submissionVersion }` and the author as actor, in the same unit of work as the change (the real writer arrives with F13.1; until then the no-op writer records nothing). Titles and bodies are never put in audit details.

### Review history

`GET /api/articles/{id}/reviews` returns the administrators' decisions on the article's submissions, newest submission first: `[{ "submissionVersion": 2, "decision": "Approved", "reason": null, "decidedAt": "…" }, …]`. `reason` is set for rejections. **Who decided is never returned to authors.** An article with no decision yet returns `[]`. Somebody else's article, or an unknown id, is the same 404 as everywhere else (administrator token: 403, no token: 401).

Tests: `backend/tests/ContentReview.IntegrationTests/Articles/ArticleSubmissionTests.cs`, `ArticleReviewsTests.cs`.

## Review queue (administrator)

Administrators read what is waiting for review and open one submission in detail. All routes need an Administrator token (author token: 403, no token: 401). There is no endpoint that edits an article's content.

| Method | Route | Success |
|---|---|---|
| GET | `/api/admin/reviews/queue` | 200 paged `ReviewQueueItem` |
| GET | `/api/admin/articles/{id}` | 200 `ReviewArticleDetails` |
| GET | `/api/admin/authors/lookup?search=` | 200 `[{ id, fullName }]` |
| POST | `/api/admin/articles/{id}/approve` | 200 `ReviewArticleDetails` (Published) |
| POST | `/api/admin/articles/{id}/reject` | 200 `ReviewArticleDetails` (Rejected) |

### Queue

`PendingReview` articles (first submissions) **and** `PendingReview` revisions of published articles, in one list. Query: `authorId`, `categoryId`, `sort=submittedAt` (the only key), `direction=asc|desc` (default **asc**: oldest submission first), `page` (from 1), `pageSize` (default 20, at most 100). Ties on the submission time are broken by id, so pages never repeat or skip an article. A bad value is a 400.

```json
{ "items": [ { "articleId": "…", "title": "…", "category": { "id": "…", "name": "Science" },
    "author": { "id": "…", "fullName": "Ann Author", "status": "Active" },
    "submittedAt": "…", "submissionVersion": 1, "kind": "Submission", "revisionId": null } ],
  "page": 1, "pageSize": 20, "totalCount": 1 }
```

`kind` is `Submission` (opens at `GET /api/admin/articles/{articleId}`) or `Revision` (`articleId` is the published article being revised, `revisionId` is set and the item opens at `GET /api/admin/revisions/{revisionId}`).

Articles of **suspended authors stay in the queue**; `author.status` says `Suspended` so the portal can mark them. Content (summary, body) is not part of a queue item.

### Detail

`GET /api/admin/articles/{id}` works for any article that has been submitted at least once, in any status, read-only:

```json
{ "id": "…", "title": "…", "summary": "…", "body": "…", "category": { "id": "…", "name": "…" },
  "status": "PendingReview", "submissionVersion": 2, "submittedAt": "…", "publishedAt": null,
  "author": { "id": "…", "fullName": "…", "email": "…", "status": "Active" },
  "reviewHistory": [ { "submissionVersion": 1, "decision": "Rejected", "reason": "…", "decidedAt": "…",
                       "reviewer": { "id": "…", "fullName": "…" } } ],
  "canReview": true, "reviewBlockedReason": null, "version": 1234 }
```

- **Content is the submission snapshot** (`article_submissions` for the current `submissionVersion`), so the administrator reviews exactly what was submitted even if the author has edited the article since.
- `reviewHistory` has every decision, newest submission first, with the reviewer (administrators see who decided; authors never do).
- `canReview` is true only while the article is PendingReview **and** its author is Active. Otherwise `reviewBlockedReason` says why: `Author account is suspended.` or `Article is not pending review (status X).`
- `version` is the token to send with the decision (F10.2).
- An article that was **never submitted** is a private draft: 404, like an unknown id.

### Author lookup

`GET /api/admin/authors/lookup?search=` returns up to 20 authors (any account status) whose name or email contains `search` (ignoring case, `%` and `_` literal, at most 100 characters), ordered by name. An empty search returns the first 20. Administrators are never listed.

### Approve and reject

```json
{ "version": 1234, "submissionVersion": 2 }                       // approve
{ "version": 1234, "submissionVersion": 2, "reason": "…" }        // reject (reason 10-1000 characters, trimmed)
```

`version` and `submissionVersion` are the ones in the detail the administrator opened (both required). The response is the updated `ReviewArticleDetails`. In one transaction the article becomes `Published` (`publishedAt` set) or `Rejected`, a `reviews` row is stored (article, submission version, reviewer, decision, reason, time) and an `ArticleApproved` / `ArticleRejected` audit event is written (`{ articleId, submissionVersion, reason? }`, the administrator as actor). The author then sees the decision through `latestReview` and the review history.

**Checks, in order:** unknown article, or one that was never submitted: 404; author suspended: 409 `Review is blocked because the author account is suspended.`; article no longer PendingReview: 409; `submissionVersion` is not the current one (the author resubmitted): 409; stale `version`: 409; for reject only, an invalid reason: 400 (`errors.reason`). So a conflict is reported even when the reason is also wrong.

**Every 409 here carries `currentStatus` and `currentVersion`** (extensions on the ProblemDetails) so the portal can say what the article is now.

**One decision per submission.** The article UPDATE only matches the `xmin` that was read and `reviews` has a unique index on `(article_id, submission_version)`. Of simultaneous approvals, rejections, or a decision against the author's withdrawal, exactly one succeeds; the others get 409 `This article has changed since you opened it. Refresh to see its current status.` and no second review row exists.

Tests: `backend/tests/ContentReview.IntegrationTests/Reviews/ReviewQueueTests.cs`, `ReviewDecisionTests.cs`.

## My articles (author)

An author's overview of their **own** articles, published ones included. The author is always the session's user: nothing in the query can name another author (an `authorId` is ignored). Another author's published article is visible only through the published feed (F12). Administrator token: 403, no token: 401, suspended author's token: 401.

| Method | Route | Success |
|---|---|---|
| GET | `/api/me/articles` | 200 paged `MyArticleItem` |
| GET | `/api/me/articles/stats` | 200 `{ draft, pendingReview, published, rejected }` |

### List

Query: `search` (any part of the title, ignoring case, `%` and `_` literal, at most 150 characters), `status` (`Draft`, `PendingReview`, `Published`, `Rejected`), `categoryId`, `sort` (`updatedAt` default, `submittedAt`, `title`), `direction` (`asc`, `desc`; default `desc` for the dates, `asc` for the title), `page`, `pageSize` (default 20, at most 100). Ties are broken by id so pages never repeat or skip an article; sorting by `submittedAt` puts drafts that were never submitted last in both directions. A bad value is a 400.

```json
{ "items": [ { "id": "…", "title": "…", "category": { "id": "…", "name": "Science" },
    "status": "Rejected", "updatedAt": "…", "submittedAt": "…", "publishedAt": null,
    "latestRejectionReason": "…", "allowedActions": ["edit", "submit"], "version": 1234 } ],
  "page": 1, "pageSize": 20, "totalCount": 1 }
```

`category` is `null` for a draft without one. `latestRejectionReason` is set only while the article is **Rejected** (the reason of its latest rejection) and is `null` otherwise. `allowedActions` comes from the lifecycle ([article-lifecycle.md](article-lifecycle.md)): Draft `edit, delete, submit`; PendingReview `withdraw`; Rejected `edit, submit`; Published none. The body and summary are not part of an item.

### Stats

Always all four statuses; one without articles is 0. One grouped query. Counts reflect a submit, withdraw, approve, reject or delete on the very next request.

Tests: `backend/tests/ContentReview.IntegrationTests/Articles/MyArticlesTests.cs`.

## Feed (author)

The published articles of **all** authors, for any signed-in author to read. Read-only: only `GET` exists under `/api/feed` (POST, PUT, PATCH and DELETE are 405). Visitors get 401, administrators 403, a suspended reader 401.

| Method | Route | Success |
|---|---|---|
| GET | `/api/feed` | 200 paged `FeedItem` |
| GET | `/api/feed/{id}` | 200 `FeedArticle` |

- **Only Published.** The status is part of the query itself, not an option: a draft, pending or rejected article never appears in the list, and its id on the detail route is a plain 404 (the same 404 as an unknown id; nothing says the article exists).
- **Latest approved version.** Title, summary, body and category are those of the article's current published version: the one an administrator approved first, or the latest approved revision that replaced it. Revisions that are drafts, pending or rejected never appear (see [revisions.md](revisions.md)).
- **Suspension does not matter.** An author's published articles stay in the feed when the author is suspended.
- **No identity.** Authors are only named: `authorDisplayName` (their full name). No author id and no email appear anywhere.

Query of the list: `search` (any part of the title, ignoring case, `%` and `_` literal, at most 150 characters), `categoryId`, `page`, `pageSize` (default 20, at most 100). Newest publication first, ties broken by id so pages never repeat or skip. A bad value is a 400.

```json
{ "items": [ { "id": "…", "title": "…", "summary": "…", "category": { "id": "…", "name": "Science" },
    "authorDisplayName": "Ann Author", "publishedAt": "…" } ],
  "page": 1, "pageSize": 20, "totalCount": 1 }
```

The detail has the same fields plus `body`.

Tests: `backend/tests/ContentReview.IntegrationTests/Articles/FeedTests.cs`.

## Revisions of published articles (F17.1)

A published article is edited through a **revision**: a separate version (own title, summary, body, category, status, submission time and submission count) that goes through review while the published version stays visible. Lifecycle, data model and concurrency: [revisions.md](revisions.md).

Author routes (Author token; somebody else's article or revision is a 404; administrators get 403, visitors 401):

| Method | Route | Success |
|---|---|---|
| POST | `/api/articles/{articleId}/revisions` | 201 `RevisionDto` (a Draft copy of the published content) |
| GET | `/api/articles/{articleId}/revisions` | 200 `[RevisionDto]`, newest first |
| GET | `/api/revisions/{id}` | 200 `RevisionDto` |
| PUT | `/api/revisions/{id}` | 200 `RevisionDto` (body: `title`, `summary`, `body`, `categoryId`, `version`) |
| DELETE | `/api/revisions/{id}?version=` | 204 (Draft or Rejected only) |
| POST | `/api/revisions/{id}/submit` | 200 `RevisionDto` (PendingReview) |
| POST | `/api/revisions/{id}/withdraw` | 200 `RevisionDto` (Draft again) |

- `RevisionDto`: `id`, `articleId`, `title`, `summary`, `body`, `category`, `status` (`Draft`, `PendingReview`, `Rejected`, `Approved`), `submissionVersion`, `createdAt`, `updatedAt`, `submittedAt`, `latestReview`, `reviews` (decision, `reason`, `decidedAt`; never who decided), `allowedActions` and `version`.
- Draft limits on save and the submission rules on submit are exactly those of an article (400 with `errors` keyed by camelCase field; nothing changes).
- Create answers **409** when the article is not Published ("Article cannot be revised in status Draft.") or already has an open revision. Two simultaneous creates: exactly one 201.
- Every other mutation needs the current `version` (stale: 409 with `currentVersion`) and answers 409 when the status does not allow the action. A PendingReview or Approved revision cannot be edited or discarded.
- The article's own `GET /api/articles/{id}` and `GET /api/me/articles` carry `openRevision` / `revision` (`id`, `status`, `updatedAt`, `submittedAt`) and offer `revise` in `allowedActions` only while no revision is open. `publishedVersion` on the article counts the published versions (1 after the first publication).

Administrator routes (Administrator token):

| Method | Route | Success |
|---|---|---|
| GET | `/api/admin/revisions/{id}` | 200 `ReviewRevisionDetails` (the revision next to the `published` version it would replace) |
| POST | `/api/admin/revisions/{id}/approve` | 200 `ReviewRevisionDetails` (Approved) |
| POST | `/api/admin/revisions/{id}/reject` | 200 `ReviewRevisionDetails` (Rejected) |

- A revision that was never submitted is a private draft: 404.
- Approve and reject take `{ version, submissionVersion }` (reject also `reason`, 10-1000 characters, 400 otherwise) and are checked in the order of articles: 404; 409 author suspended; 409 not pending, a newer submission, or a stale `version`; 409 when another request decided or the author withdrew first. Every 409 carries `currentStatus` (a revision status) and `currentVersion`.
- **Approve** is one transaction: the revision becomes Approved, its content replaces the article's published content, the replaced version is written to `article_published_versions`, and the review is stored. **Reject** stores the reason and changes nothing on the published article.
- Of simultaneous approve, reject and withdraw on the same revision exactly one succeeds; the others get 409.

Tests: `backend/tests/ContentReview.IntegrationTests/Revisions/RevisionTests.cs`, `backend/tests/ContentReview.UnitTests/Domain/ArticleRevisionTests.cs`, `RevisionLifecycleTests.cs`.

## Dashboard and audit history (administrator)

Administrator token only: an Author token is 403, no token 401. Read-only: POST, PUT, PATCH and DELETE are 405 (or 404 on a path that does not exist); nothing here changes a counter or an audit event.

| Method | Route | Success |
|---|---|---|
| GET | `/api/admin/dashboard` | 200 `{ activeAuthors, suspendedAuthors, availableInvitations, pendingReviews, publishedArticles }` |
| GET | `/api/admin/audit-events` | 200 paged `AuditEventItem` |
| GET | `/api/admin/audit-events/actions` | 200 list of action names |

### Dashboard

Counted from the database on every request. Users are **authors only** (administrators are not counted). `availableInvitations` uses the derived status rule: not used, not revoked, `expires_at > now` (an invitation stops counting the moment it expires). `pendingReviews` is the number of articles in PendingReview, `publishedArticles` the number in Published.

### Audit events

Query: `action` (repeat it for several: `?action=UserSuspended&action=UserReactivated`; an unknown name is a 400), `actorId`, `from` (inclusive) and `to` (exclusive) as ISO 8601 with an offset (e.g. `2026-10-27T00:00:00Z`; an unreadable date or `from` after `to` is a 400 naming the field), `page`, `pageSize` (default 20, at most 100). Newest first; events with the same time are ordered by id so pages never repeat or skip.

```json
{ "items": [ { "id": "…", "occurredAt": "…", "action": "UserSuspended",
    "actor": { "id": "…", "fullName": "Alice Admin", "role": "Administrator" },
    "entityType": "User", "entityId": "…", "details": { "reason": "…" }, "correlationId": "…" } ],
  "page": 1, "pageSize": 20, "totalCount": 1 }
```

`actor` is `null` for events written by the system (the administrator provisioning script); `correlationId` is `null` for events written outside a request. `details` is what [audit.md](audit.md) says is recorded: never passwords, hashes, tokens or full invitation codes. `/actions` returns every action name (`AuditActions.All`), for a filter.

Tests: `backend/tests/ContentReview.IntegrationTests/Admin/AdminDashboardTests.cs`, `AuditHistoryTests.cs`.
