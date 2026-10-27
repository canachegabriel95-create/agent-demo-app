# Audit events

Every important action is recorded by the backend as a row of `audit_events`. Nothing in a portal or a request can write, change or delete one.

## What is recorded

| Action | Written by | Actor | Entity |
|---|---|---|---|
| `AdministratorProvisioned` | `db/scripts/provision-admin.sql` | none (system) | User |
| `InvitationCreated` | `POST /api/admin/invitations` | the administrator | Invitation |
| `InvitationRevoked` | `POST /api/admin/invitations/{id}/revoke` | the administrator | Invitation |
| `UserRegistered` | `POST /api/registration` | the new user | User |
| `AdminProfileEdited` | `PUT /api/admin/users/{id}/profile` (only when something changed) | the administrator | User |
| `UserSuspended` / `UserReactivated` | `POST /api/admin/users/{id}/suspend` / `reactivate` | the administrator | User |
| `ArticleSubmitted` / `ArticleWithdrawn` | author submit / withdraw (a resubmission is another `ArticleSubmitted`) | the author | Article |
| `ArticleApproved` / `ArticleRejected` | `POST /api/admin/articles/{id}/approve` / `reject` | the administrator | Article |

An action that is refused or fails (validation, 403, 409, a lost race) writes nothing. Constants: `AuditActions` in `ContentReview.Domain`.

Each row has `occurred_at` (UTC, from `TimeProvider`), `actor_user_id` (null for the system), `action`, `entity_type`, `entity_id`, `details` (`jsonb`) and `correlation_id` (the `X-Correlation-Id` of the request, so an event can be matched with its response and log lines; null outside a request).

## Same transaction

`IAuditWriter.Record(...)` only adds the event to the current `DbContext` (`AuditWriter`). The event is saved by the same `SaveChanges`, and so in the same transaction, as the change it describes: a rollback, a failed save or a lost race leaves no event, and no change without its event.

## No secrets

Details are small anonymous or typed objects serialized as camelCase JSON, then passed through `AuditDetailsSanitizer`:

- a property is refused when its name contains `password`, `token`, `hash` or `secret`, or ends with `code` (`codeSuffix`, the last 4 characters of an invitation code, is the only form of a code that is recorded);
- a text value is refused when it is a full invitation code, whatever the property is called.

Refused means: in Development an exception (so the mistake shows while developing); elsewhere the value is removed and the event is still written (`Audit:ThrowOnSensitiveDetails` overrides, `true` or `false`). Passwords, hashes and session tokens are never passed to the writer in the first place; a test scans every event of a full scenario for them.

## Append-only

`AuditImmutabilityInterceptor` makes `SaveChanges` throw `AuditImmutableException` when an `AuditEvent` is modified or deleted. There is no endpoint that writes audit events (reading them is F13.2). Raw SQL and `ExecuteUpdate`/`ExecuteDelete` do not pass through `SaveChanges`; the application has none for audit events, and no database trigger is added so that the local reset command (F15) can empty the table.

Tests: `ContentReview.IntegrationTests/Auditing/AuditEventTests.cs` (the real writer: one event per action, rollback, no secrets, immutability) and `ContentReview.UnitTests/AuditDetailsSanitizerTests.cs`.
