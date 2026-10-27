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
