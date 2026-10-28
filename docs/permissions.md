# Permissions

Roles and resource ownership are enforced by the API itself, independently of what the portals show. This page is the skeleton of the permission model; story F16.2 completes it with the per-endpoint matrix.

## Roles and policies

| Policy | Who | Used for |
|---|---|---|
| `AdminOnly` | authenticated, role `Administrator` | `/api/admin/**` |
| `AuthorOnly` | authenticated, role `Author` | `/api/me/**`, `/api/articles/**`, `/api/feed/**` |
| *(authenticated)* | any signed-in user | `GET /api/categories`, `GET /api/auth/me`, `POST /api/auth/logout` |
| *(fallback)* | any signed-in user | every endpoint that declares nothing |

A request is authenticated by the `Session` bearer scheme (see [authentication.md](authentication.md)); the role comes from the session's user, never from the portal the user signed in to.

## Status codes

| Situation | Response |
|---|---|
| No token, invalid token, expired or revoked session, suspended user | `401` ProblemDetails, `WWW-Authenticate: Bearer` |
| Valid token, wrong role for the route | `403` ProblemDetails |
| No token and a URL that matches no endpoint (or the wrong method) | `401`, not `404`/`405`: the fallback policy answers first, so the route table is not disclosed |
| Author asks for a resource owned by another author, or an id that does not exist | `404` ProblemDetails (identical, so the id is not disclosed) |

## Route groups

| Route group | Policy | Base class |
|---|---|---|
| `/health` | anonymous | - |
| `POST /api/auth/login` | anonymous | - |
| `POST /api/registration` *(F05)* | anonymous | - |
| Swagger UI and OpenAPI document (F14.1) | anonymous in Development/Demo only; 401 in Production | - |
| `GET /api/auth/me`, `POST /api/auth/logout` | any authenticated | - |
| `GET /api/categories` *(later)* | any authenticated | - |
| `/api/admin/**` | `AdminOnly` | `AdminControllerBase` |
| `/api/me/**`, `/api/articles/**`, `/api/revisions/**`, `/api/feed/**` | `AuthorOnly` | `AuthorControllerBase` |

Only the first four rows are allowed to be anonymous. The test `Every_registered_endpoint_requires_authorization_except_the_documented_anonymous_ones` lists the anonymous endpoints exactly; the story that adds registration or Swagger must add its endpoint to that list (and to this table) on purpose.

## Ownership

Author resources are always read and changed through the current user:

```csharp
var article = await db.Articles.OwnedBy(currentUser.UserId).FirstOrDefaultAsync(a => a.Id == id);
if (article is null) return NotFound();   // not owned and not existing look the same
```

`OwnedBy` (`ContentReview.Api/Auth/AuthorOwnership.cs`) filters by `author_id`. `ICurrentUser` exposes `UserId`, `Role` and `SessionId` from the session claims and may only be used on authorized endpoints.

## Adding an endpoint

1. Put it under the route prefix of its audience and derive from `AdminControllerBase` or `AuthorControllerBase`, which carry the policy. For endpoints open to every signed-in user, use `[Authorize]`. For anonymous ones, use `[AllowAnonymous]` and update the allow-list test and the table above.
2. For author resources, query through `OwnedBy(currentUser.UserId)` and return `NotFound()` when nothing matches.
3. Run the tests; the convention tests fail with the name of any action that has no policy or `[AllowAnonymous]`, or a route whose prefix does not match its policy.

## Tests

`backend/tests/ContentReview.IntegrationTests/Auth`:

- `AuthorizationTests`: role matrix (401 / 403 / 200) across the admin, author, any-authenticated, unmarked and anonymous groups, using the test-only endpoints in `Probes.cs`, plus ownership (own article 200, another author's or unknown id 404 with the same body).
- `AuthorizationConventionTests`: every controller action has a policy or `[AllowAnonymous]`; route prefixes match their policy; every registered endpoint carries authorization metadata and the anonymous set is exactly `/health` and `POST /api/auth/login`.
