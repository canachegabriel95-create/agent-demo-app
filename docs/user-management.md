# Author management (Admin Portal)

Administrators find authors, read their profile and see what they have written. The HTTP details are in [api.md](api.md#users-administrator); the actions that change an author (edit, suspend, reactivate) are on the detail page, described below.

## Authors list

Route `/users` (navigation item "Authors"), administrators only like the rest of the portal.

| Part | Behavior |
|---|---|
| Search | Part of a name or an email, ignoring case. Waits 300 ms after typing, then updates the URL and the request. `%`, `_` and `\` are ordinary characters (the API escapes them). |
| Status | All, Active, Suspended. |
| Sort | The "Registered" column header is a button: newest first (default), oldest first. `aria-sort` and hidden text tell screen-reader users the current order. |
| Table | Name (link to the detail page), Email, Status as a text badge, Registered (date). 20 per page. |
| URL | All state lives in the address: `/users?search=ann&status=Suspended&direction=asc&page=2`. Defaults are left out (`direction=desc`, `page=1`). An unknown `status` or `direction` is ignored. Changing a filter or the sort returns to page 1. Dashboards can deep-link (F13.3). |
| States | Loading, empty ("No authors match the filters"), error with a reference id and "Try again". A Refresh button reloads. |

## Author detail

Route `/users/:id`.

| Part | Behavior |
|---|---|
| Header | Name and a status badge (`Active` / `Suspended`, always written out). |
| Suspension | Only for suspended authors: when they were suspended and the reason. |
| Profile | Name, email, role, registration date, biography. The biography and the reason are **text**: `white-space: pre-wrap` keeps line breaks, markup is never interpreted (no `dangerouslySetInnerHTML`). No biography shows "—". |
| Articles | Four tiles: Draft, Pending Review, Published, Rejected (counts from the details endpoint). |
| States | Loading; error with "Try again"; an unknown id (or an administrator's id, which the API hides) shows "Author not found" with a link back to the list. |

Test ids: `user-notice`, `users-refresh`, `users-sort-registered`, `user-row-<id>`, `user-status`, `user-full-name`, `user-email`, `user-bio`, `user-suspension`, `user-suspension-reason`, `user-count-draft|pendingReview|published|rejected`.

## Actions on the detail page (F06.5)

Three buttons sit under the header. All three open a modal dialog (`role="dialog"`, labelled by its title, Tab stays inside, Escape closes, focus goes back to the button that opened it). Every request carries the `version` that was loaded.

| Button | Offered | Dialog | API |
|---|---|---|---|
| Edit profile (`user-edit-open`) | always | Display name (2-100) and Biography (0-1000) with live character counters. Email, role and password are not fields: a note says they cannot be changed here. Save is `profile-save`. | `PUT /api/admin/users/{id}/profile` |
| Suspend (`user-suspend-open`) | Active authors only | Explains that the author is signed out everywhere at once. Required Reason (10-500 characters, counter). Confirm is `suspend-confirm`. | `POST .../suspend` |
| Reactivate (`user-reactivate-open`) | Suspended authors only | Confirmation that explains the author must sign in again, because the sessions ended by the suspension stay ended. Confirm is `reactivate-confirm`. | `POST .../reactivate` |

- **Validation** uses the same sentences as the API, runs on submit and nothing is sent while a field is invalid. A field's message disappears as soon as that field is edited. A 400 from the API is shown next to its field.
- **After success** the dialog closes, the page shows the author exactly as the API returned them (new `version`, status, suspension box) and a success message in a `role="status"` region: "The profile was saved.", "<name> was suspended and signed out everywhere.", "<name> was reactivated and can sign in again."
- **Conflict (409)**: the dialog stays open, its fields are disabled and it says "This profile was changed by someone else. Reload to continue." (suspend and reactivate: "This author was changed by someone else. Reload to continue."). The **Reload** button (`profile-reload`, `suspend-reload`, `reactivate-reload`) loads the author again and closes the dialog, so nothing the administrator typed is saved over the other change. A stale version, an author who was suspended or reactivated meanwhile, and a lost race all end here.
- **Other failures** appear in the dialog with the reference id and the dialog stays open, so nothing typed is lost.

## Where the code is

| What | Where |
|---|---|
| API client and types | `frontend/admin-portal/src/api/users.ts` |
| List page | `src/pages/UsersPage.tsx`, `UsersPage.module.css` |
| Detail page | `src/pages/UserDetailPage.tsx`, `UserDetailPage.module.css` |
| Edit / suspend / reactivate dialogs | `src/pages/EditProfileDialog.tsx`, `SuspendUserDialog.tsx`, `ReactivateUserDialog.tsx` |
| Routes and navigation | `src/app/router.tsx`, `src/app/Layout.tsx` |

## Tests

`UsersPage.test.tsx`, `UserDetailPage.test.tsx` and `UserActions.test.tsx` (Vitest): table contents, debounced search, status filter, sort, pagination, URL round trips, loading / empty / error / not-found states, biography rendered as text, suspension reason, counts; buttons by status, validation limits, request bodies and versions, success messages, 409 and failure handling, dialog focus and Escape. The Playwright scenario for the whole flow is added by F16.4.
