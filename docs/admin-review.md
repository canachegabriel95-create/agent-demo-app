# Article review (Admin Portal)

Administrators review what authors submit. The API behind these pages is in [api.md](api.md#review-queue-administrator); the lifecycle is in [article-lifecycle.md](article-lifecycle.md).

## Review queue (`/reviews`)

Navigation item **Review queue**. A table of the articles that are waiting for review (`PendingReview` only), oldest submission first.

| Column | Content |
|---|---|
| Title | Link to `/reviews/:articleId` (the review page, F10.4). The link remembers the queue's filters so a decision can return to the same view. |
| Author | Name, plus a text badge **Suspended** when the author's account is suspended (their articles stay in the queue but cannot be decided) |
| Category | Name |
| Submitted | Date and time. The header is a button that toggles oldest first (▲, default) and newest first (▼); `aria-sort` follows. |
| Version | The submission number |

### Filters

- **Author**: type part of a name or email; matches (at most 20, `GET /api/admin/authors/lookup`) are listed; choosing one shows a removable chip. A chip that arrives from the address is named from the articles on screen.
- **Category**: a select filled from `GET /api/categories`.
- **Refresh** reloads the current view.

Everything is in the address, so a view can be bookmarked, shared and survives a reload: `?authorId=…&categoryId=…&direction=desc&page=2`. Defaults are left out (`direction=asc`, page 1). Changing a filter or the sort goes back to page 1. The API call is `GET /api/admin/reviews/queue?authorId=…&categoryId=…&sort=submittedAt&direction=desc&page=2`.

### States

Loading ("Loading the review queue…"), empty ("No articles are waiting for review", with a different hint when filters are on), and an error with the correlation id and **Try again**.

The count badge on the navigation item mentioned in the story needs the dashboard statistics (F13) and is not part of this story.

Tests: `frontend/admin-portal/src/pages/ReviewQueuePage.test.tsx`; API `backend/tests/ContentReview.IntegrationTests/Reviews/ReviewQueueTests.cs`.
