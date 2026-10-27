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

Authentication failures are RFC 9457 `application/problem+json`: `401` includes `WWW-Authenticate: Bearer`; an authenticated user without the required role gets `403`. Correlation ids and the error format of the whole API are described in [api.md](api.md); role policies and ownership checks in [permissions.md](permissions.md).

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

## Author Portal

`frontend/author-portal` mirrors the Admin Portal (story F03.4) with `portal: "Author"` and the session key `cr.author.session`. The same rules apply: a stored session is validated with `GET /api/auth/me`, every page except the public ones is behind `RequireAuth`, `returnUrl` must be a relative in-app path, any 401 on a request that carried a token ends the session with the expiry message, and logout always ends the local session.

Differences from the Admin Portal:

- **Public routes:** `/login` and `/register`. `/register` is a placeholder until the registration form (F05.3).
- **Login page:** a link "Have an invitation? Create an account" to `/register` (`data-testid="register-link"`) and a success banner (`role="status"`, `data-testid="login-registered"`) when the page is opened with `?registered=1`. The banner is not shown for any other value, nor after a failed sign-in.
- **Generic error:** an Administrator account gets the same "Invalid email or password." as a wrong password (the API returns the same 401 for a role that does not match the portal).
- **Navigation for a signed-in author:** Dashboard (`/`), My Articles (`/articles`), Feed (`/feed`), Profile (`/profile`) and Log out. The last three open a "Coming soon" page until their stories are delivered.

Tests: `src/auth/auth.test.tsx` (includes the registration entry points), `src/auth/returnUrl.test.ts`, `src/app/router.test.tsx` (navigation and protected placeholders).

The portal code under `src/auth`, `src/api/auth.ts` and `src/api/client.ts` is a deliberate copy of the Admin Portal's, as the two portals do not share a package; keep them in step when one changes.
