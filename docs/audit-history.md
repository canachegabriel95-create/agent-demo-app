# Audit history (Admin Portal)

Route `/audit` (nav "Audit history"), source `frontend/admin-portal/src/pages/AuditPage.tsx`. Data: `GET /api/admin/audit-events` and `/actions` ([api.md](api.md#dashboard-and-audit-history-administrator)); what is recorded and why it holds no secrets: [audit.md](audit.md). The page is read-only: it has no edit, delete or save control.

## Filters (all in the address, so a link such as the dashboard's "Published articles" card works)

| Control | Parameter | Notes |
|---|---|---|
| Actions (checkboxes, from `/actions`) | `action` (repeated) | one or several; no checkbox means all |
| Actor id | `actorId` | paste an id, or use "filter" next to a name in the table; sent once it is a complete id |
| From / To | `from`, `to` (dates) | whole days in the administrator's time zone; **To is inclusive** (the request ends at the start of the next day) |
| Pagination | `page` | a filter change goes back to page 1 |
| Clear filters | | empties the address and the controls |

A "To" before "From" shows a message and asks nothing. If the action list cannot be loaded the table still works without the checkboxes.

## Table

| Column | Shows |
|---|---|
| Time | local time; the `title` (tooltip) and `datetime` hold the UTC instant |
| Action | the action name |
| Actor | the person's name, with a "filter" button; "System" for events written by the system |
| Entity | the type and the last 8 characters of its id (full id in the tooltip; UUIDv7 ids start alike when created together); User links to `/users/:id`, Article to `/reviews/:id` |
| Details | the recorded facts as `key: value` text lines; nested values as JSON text; never HTML |
| Correlation id | its first 8 characters and a Copy button (it matches the `X-Correlation-Id` of the request and its log lines) |

States: loading, error with "Try again", "No audit events yet", and "No audit events match the filters" with Clear filters.

Tests: `pages/AuditPage.test.tsx`.
