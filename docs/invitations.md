# Invitations

Authors can only register with an invitation code that an administrator created for their email address. This page describes the invitation states, how codes are issued and used, and the Admin Portal page that manages them. The HTTP details are in [api.md](api.md#invitations-administrator).

## States

| Status | Meaning |
|---|---|
| Available | Not used, not revoked, `expiresAt` is in the future |
| Used | An account registered with it (`usedAt`, `usedBy`) |
| Revoked | An administrator withdrew it (`revokedAt`, `revokedBy`) |
| Expired | Not used, not revoked, `expiresAt <= now`. Derived: nothing is stored and nothing runs when it expires |

The status is computed in exactly one place, `Invitation.GetStatus(now)`; the list filter repeats the same rules as SQL predicates (`InvitationService.ListAsync`). Used wins over Revoked, which wins over Expired.

Only an Available invitation can be revoked. Nothing moves an invitation out of Used, Revoked or Expired.

## Codes

12 characters of the Crockford base32 alphabet (`0-9 A-Z` without `I L O U`), shown as `XXXX-XXXX-XXXX`, from `RandomNumberGenerator`. Input is accepted in any case, with or without dashes (`InvitationCode.Normalize`). The code is bound to one email: registering with another address is refused.

Administrators see and copy full codes. Audit events and logs never contain more than the last 4 characters.

## Lifecycle

1. An administrator creates an invitation for an email (`POST /api/admin/invitations`) and shares the code by hand (email delivery is out of scope). The email must not belong to an existing user.
2. The Author Portal registration form calls `POST /api/registration/validate-invitation` for early feedback; nothing is consumed.
3. Registration (`POST /api/registration`, see [api.md](api.md#post-apiregistration)) creates the account and calls `IInvitationRedemptionService.ConsumeAsync` in the same transaction. One conditional UPDATE makes the invitation Used, so concurrent registrations with one code cannot both succeed; the loser gets the `AlreadyUsed` reason.
4. An administrator may revoke an Available invitation at any time before that.

## Admin Portal: Invitations page

Route `/invitations` (navigation item "Invitations"), administrators only like the rest of the portal.

| Part | Behavior |
|---|---|
| Filters | Email text (waits 300 ms after typing) and Status (All, Available, Used, Expired, Revoked). They live in the URL: `/invitations?status=Available&email=ann&page=2`, so dashboards can deep-link. An unknown `status` is ignored. Changing a filter returns to page 1. |
| Table | Code (with a Copy button), Email, Status as a text badge, Created (date and creator), Expires, Used / Revoked (date and who), Actions. 20 per page, newest first. |
| New invitation | Dialog with email and validity (1-30 days, default 7). Server messages appear next to the fields; an existing user's email appears at the email field. On success the code is shown large with a Copy button and the list reloads. |
| Copy | `navigator.clipboard.writeText`, then "Copied" is announced in a polite live region. If the clipboard is unavailable the message says so and the code stays selectable. |
| Revoke | Only on Available rows, after a confirmation dialog, sending the `version` that was loaded. A 409 shows "This invitation changed. Refresh to see the latest status." and reloads. |
| States | Loading, empty ("No invitations match the filters"), error with a reference id and "Try again". There is also a Refresh button. |

Accessibility: dialogs have `role="dialog"`, `aria-modal`, a title label, a focus trap, Escape to close and return focus to the opener. Buttons have names that include the email (`Revoke invitation for ann@example.com`).

Test ids: `invitation-create-open`, `invitation-create-submit`, `invitation-created-code`, `invitation-created-copy`, `invitation-create-done`, `invitation-copy-<id>` (row), `invitation-revoke-<id>`, `invitation-revoke-confirm`, `invitations-notice`, `invitations-refresh`.

## Where the code is

| What | Where |
|---|---|
| Entity, status, code format | `ContentReview.Domain/Entities/Invitation.cs`, `Common/InvitationCode.cs` |
| Management (create, list, revoke) | `ContentReview.Infrastructure/Invitations/InvitationService.cs`, `ContentReview.Api/Controllers/AdminInvitationsController.cs` |
| Validation and consumption | `ContentReview.Infrastructure/Invitations/InvitationRedemptionService.cs`, `ContentReview.Api/Controllers/RegistrationController.cs` |
| Audit hook (no-op until F13.1) | `ContentReview.Domain/Auditing/IAuditWriter.cs` |
| Admin Portal | `frontend/admin-portal/src/pages/InvitationsPage.tsx` (+ dialogs), `src/api/invitations.ts` |

## Tests

`InvitationTests`, `InvitationCodeTests` (unit); `AdminInvitationsTests`, `InvitationRedemptionTests` (integration, real PostgreSQL); `InvitationsPage.test.tsx` (Vitest). The Playwright scenario for this page is added by F16.4.
