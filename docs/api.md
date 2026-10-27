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
| 429 | Too many login attempts | Too many login attempts. Try again later. | `Retry-After` |
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
