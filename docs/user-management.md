# Author management (Admin Portal)

Administrators find authors, read their profile and see what they have written. The HTTP details are in [api.md](api.md#users-administrator); the actions that change an author (edit, suspend, reactivate) are added to the detail page by F06.5.

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

Test ids: `users-refresh`, `users-sort-registered`, `user-row-<id>`, `user-status`, `user-full-name`, `user-email`, `user-bio`, `user-suspension`, `user-suspension-reason`, `user-count-draft|pendingReview|published|rejected`.

## Where the code is

| What | Where |
|---|---|
| API client and types | `frontend/admin-portal/src/api/users.ts` |
| List page | `src/pages/UsersPage.tsx`, `UsersPage.module.css` |
| Detail page | `src/pages/UserDetailPage.tsx`, `UserDetailPage.module.css` |
| Routes and navigation | `src/app/router.tsx`, `src/app/Layout.tsx` |

## Tests

`UsersPage.test.tsx` and `UserDetailPage.test.tsx` (Vitest): table contents, debounced search, status filter, sort, pagination, URL round trips, loading / empty / error / not-found states, biography rendered as text, suspension reason, counts. The Playwright scenario for the whole flow is added by F16.4.
