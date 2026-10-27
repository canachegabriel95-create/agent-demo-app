# My Articles (Author Portal)

Route `/articles` (nav "My Articles"), source `frontend/author-portal/src/pages/MyArticlesPage.tsx`. Data: `GET /api/me/articles` (see [api.md](api.md#my-articles-author)).

## Filters (all in the address)

| Control | Parameter | Notes |
|---|---|---|
| Search | `search` | title; sent 300 ms after typing stops, not per keystroke |
| Status | `status` | `Draft`, `PendingReview`, `Published`, `Rejected`; the dashboard cards link here |
| Category | `categoryId` | from `GET /api/categories` |
| Sort by | `sort` | Last updated (default, no parameter), Submitted date (`submittedAt`), Title (`title`) |
| Pagination | `page` | changing any filter goes back to page 1 |

An unknown `status` or `sort` is ignored. The back button restores earlier filters, including the text in the search box.

## Row

Title (link to the article), category (or "No category"), status badge (the status is written out), and the date for the status: Draft and Rejected "Updated …", Pending review "Submitted …", Published "Published …".

Actions come from the row's `allowedActions`, never from the status in the browser:

| Status | Actions |
|---|---|
| Draft | Edit, Submit, Delete |
| PendingReview | Withdraw |
| Rejected | Edit, Resubmit, See feedback |
| Published | none |

Submit, Withdraw and Delete ask for confirmation, send the row's `version`, then reload the list and show a message. A 409 (the article changed, e.g. an administrator decided first) shows "The article changed in the meantime" and the reloaded state. A submission that fails validation (400) keeps the dialog open and names what is missing. "See feedback" opens the latest rejection reason, shown as text (never HTML).

## States

- No articles at all: "You have no articles yet" with a New article button.
- Filters without a match: "No articles match your filters" with Clear filters (empties the address and the search box).
- Loading, and an error state with "Try again".
- A message from another page (deleting a draft in the article view) is shown once, then removed from the history entry.

Tests: `pages/MyArticlesPage.test.tsx`.
