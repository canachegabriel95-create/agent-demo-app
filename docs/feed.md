# Feed (Author Portal)

Published articles of all authors, read-only. Data: `GET /api/feed` and `GET /api/feed/{id}` (see [api.md](api.md#feed-author)).

## `/feed` (nav "Feed")

Source `frontend/author-portal/src/pages/FeedPage.tsx`. Cards show the title (link to the reader), summary, category, author display name and publication date. Filters live in the address:

| Control | Parameter | `data-testid` |
|---|---|---|
| Search (sent 300 ms after typing stops) | `search` | `feed-search` |
| Category | `categoryId` | `feed-category` |
| Pagination | `page` (a filter change goes back to page 1) | |

Each card is `feed-card-<id>`. States: loading, error with "Try again", "No published articles yet" (nothing published), "No articles match your search" with Clear filters. The feed is read when the page opens, so an article an administrator approved shows after a refresh.

## `/feed/:id`

Source `FeedArticlePage.tsx`. Title, category, author display name, date, summary and body, read-only: **no edit or delete controls, not even on the author's own article** (a published article cannot be changed). Draft, pending, rejected and unknown ids all show "Article not found". Loading and error (with retry) states exist.

## Safe rendering

Titles, summaries and bodies are React text children, never HTML (`react/no-danger` is an ESLint error). Summary and body keep their line breaks with `white-space: pre-wrap`.

Tests: `pages/FeedPage.test.tsx`.
