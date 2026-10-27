# API conventions

Base URL `http://localhost:5080`, routes under `/api`. JSON is camelCase and enums are strings (`"PendingReview"`). The endpoint reference arrives with the Swagger story (F14.1); this page describes the conventions every endpoint follows. Authentication is described in [authentication.md](authentication.md) and permissions in [permissions.md](permissions.md).

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

