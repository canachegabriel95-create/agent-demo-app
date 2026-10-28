# Article review (Admin Portal)

Administrators review what authors submit. The API behind these pages is in [api.md](api.md#review-queue-administrator); the lifecycle is in [article-lifecycle.md](article-lifecycle.md).

## Review queue (`/reviews`)

Navigation item **Review queue**. A table of the articles that are waiting for review (`PendingReview` only), oldest submission first.

| Column | Content |
|---|---|
| Title | Link to `/reviews/:articleId` (the review page, below). The link remembers the queue's filters so a decision can return to the same view. |
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

# Review page (`/reviews/:articleId`)

The queue also lists pending revisions of published articles (a "Type" column); a revision opens on its own page, see [revisions.md](revisions.md). A first submission is opened from the queue (a link on the title). It shows one submitted article, read-only, and lets the administrator decide.

- **Header:** title, status badge, category, submission number and submitted date.
- **Author:** name (link to the author's page), email and account status; a suspended account is highlighted.
- **Submitted content:** summary and body exactly as submitted (the submission snapshot, not the author's draft), rendered as plain text with line breaks kept (`white-space: pre-wrap`, no HTML). There is no control that edits the article.
- **Review history:** every earlier decision, newest submission first: "Submission 1: Rejected", the reviewer, the date and the reason.
- A missing article, and one that was never submitted (a private draft), show **Article not found**.

## Deciding

| Button | What happens |
|---|---|
| **Approve** (`review-approve`) | A confirmation ("Publish … by …?", Cancel focused), then `POST …/approve`. The article is published. |
| **Reject** (`review-reject`) | A dialog with a required **reason** (10-1000 characters, counted live, trimmed): an empty or wrong-length reason is refused in the dialog and nothing is sent. Then `POST …/reject`. The author reads the reason. |

Both send the `version` and the `submissionVersion` of what was opened. On success the administrator goes back to `/reviews` **with the filters of the queue they came from** and sees a one-time message ("Approved “…”. It is now published." / "Rejected “…”. The author can see your reason."; the navigation entry is replaced so a reload does not repeat it).

The buttons show while the article is pending. When `canReview` is false (the author is suspended) they are **disabled** and an info line gives `reviewBlockedReason` ("Author account is suspended."). An article that is no longer pending (decided, or withdrawn) shows no buttons, only the reason.

## When the article changed

A 409 (the author withdrew or resubmitted, another administrator decided, the author was suspended meanwhile) opens a modal that cannot be dismissed without choosing: "This article has changed since you opened it (current status: X). Refresh to see the latest version." (X comes from the response's `currentStatus`). **Refresh** (`review-refresh`) reloads the article and its actions. No review is stored.

Other failures stay in the dialog with the correlation id. A 400 on the reason is shown on the field.

Tests: `ReviewPage.test.tsx`; API `ReviewDecisionTests.cs`.
