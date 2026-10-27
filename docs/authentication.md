# Authentication

Opaque bearer sessions. There are no JWTs and no cookies: the Admin and Author portals send `Authorization: Bearer <token>` and the API checks the session in the database on **every** request.

## Endpoints (`/api/auth`)

| Method | Route | Auth | Success | Failures |
|---|---|---|---|---|
| POST | `/login` | anonymous | `200` `{ token, expiresAt, user: { id, fullName, email, role } }` | `400` malformed body, `401` any credential problem, `429` too many attempts |
| POST | `/logout` | bearer | `204`; the session is revoked on the server | `401` |
| GET | `/me` | bearer | `200` `{ id, fullName, email, role, status }` | `401` |

Login body: `{ "email": "...", "password": "...", "portal": "Admin" | "Author" }`. `portal` selects the role that is allowed: `Admin` requires an Administrator, `Author` requires an Author.

```bash
curl -s http://localhost:5080/api/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"<password>","portal":"Admin"}'

curl -s http://localhost:5080/api/auth/me -H "Authorization: Bearer $TOKEN"
curl -s -X POST http://localhost:5080/api/auth/logout -H "Authorization: Bearer $TOKEN"   # 204
```

## Login rules

- The email is trimmed and lower-cased before it is looked up (`normalized_email`).
- A wrong password, an unknown email, a malformed email, a suspended account and a role that does not match the portal all return the **same** `401` body (`"Invalid email or password."`). The response never says which one it was.
- The password hash is verified even when the email is unknown (against a throw-away hash), so response time does not reveal whether an account exists.
- Login is limited to 10 attempts per minute per client IP (`Auth__LoginAttemptsPerMinute`); more returns `429` with `Retry-After`.

## Sessions

- The token is 32 random bytes, base64url encoded (43 characters). Only its **SHA-256 hash** (lowercase hex) is stored in `user_sessions.token_hash`; the token itself is shown once, in the login response.
- Lifetime: `Auth__SessionLifetimeHours` (default 8). `expires_at` is set at login.
- A request is authenticated only if the session exists, is not revoked, is not expired **and** the user is `Active`. Suspending a user therefore ends their sessions immediately, without waiting for expiry. `ISessionService.RevokeAllForUserAsync` also marks them revoked (used by user suspension, F06.3).
- `last_seen_at` is updated at most once a minute.
- Logout revokes only the session whose token was sent.
- The authenticated principal carries the claims `sub` (user id), `sid` (session id), `role`, `name` and `email`.

## Errors

Authentication failures are RFC 9457 `application/problem+json`: `401` includes `WWW-Authenticate: Bearer`; an authenticated user without the required role gets `403`. Consistent correlation ids and exception mapping for the whole API are delivered by F14.2; role policies and ownership checks are described in [permissions.md](permissions.md).

Tokens and passwords are never logged; the API does not log request headers or bodies.

## Code map

| Piece | Location |
|---|---|
| Session entity behavior (`IsValid`, `Revoke`, `TryMarkSeen`) | `ContentReview.Domain/Entities/UserSession.cs` |
| `ISessionService`, `SessionService`, `AuthOptions` | `ContentReview.Infrastructure/Sessions` |
| Authentication handler (scheme `Session`) | `ContentReview.Api/Auth/SessionAuthentication.cs` |
| Credential check (identical failures, timing) | `ContentReview.Api/Auth/CredentialsValidator.cs` |
| Endpoints | `ContentReview.Api/Controllers/AuthController.cs` |

## Tests

`backend/tests/ContentReview.IntegrationTests/Auth` (login per portal, identical 401s, malformed requests, token stored only as a hash, logout, expiry with a fake clock, suspension, last-seen throttling, rate limit) and `Scripts/AdminProvisioningTests.cs` (an administrator created by `provision-admin.sql` can log in with `portal=Admin`, not `Author`). Domain behavior: `UserSessionTests`.

## Admin Portal

`frontend/admin-portal` signs in through these endpoints (story F03.3):

- **Session:** the token is kept in `sessionStorage` under `cr.admin.session` (so it ends with the browser tab) and sent as `Authorization: Bearer` by `apiFetch`. On start `AuthProvider` validates a stored token with `GET /api/auth/me`; while that runs, protected pages show "Checking your session…".
- **Route guard:** `RequireAuth` wraps every page except `/login`, unknown paths included. An anonymous visitor is sent to `/login?returnUrl=<path>`. After signing in the portal goes to `returnUrl`, but only if it is a relative in-app path (`auth/returnUrl.ts` rejects absolute URLs, `//host`, backslashes, control characters and the login page itself, falling back to `/`).
- **Login form:** `portal: "Admin"`; fields are required before any request; the button and fields are disabled while the request runs. A `401` always shows "Invalid email or password." in a `role="alert"` region (`data-testid="login-error"`), whatever the cause, so an Author account looks like a wrong password. `429` asks the user to wait; other failures show a neutral message.
- **Expiry:** a `401` on any request that carried a token (expired, revoked, suspended) clears the stored token, signs the user out and shows "Your session has expired. Please sign in again." on the login page. A failed sign-in is not treated as expiry.
- **Logout:** the header shows the administrator's name and a "Log out" button (`data-testid="logout-button"`). It calls `POST /api/auth/logout`, clears the token and goes to `/login`, even if that call fails.

Tests: `src/auth/auth.test.tsx`, `src/auth/returnUrl.test.ts`, `src/app/router.test.tsx`.
