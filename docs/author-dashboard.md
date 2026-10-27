# Author dashboard

Route `/` of the Author Portal (nav "Dashboard"), shown after login. Source: `frontend/author-portal/src/pages/HomePage.tsx`.

- Four stat cards, from `GET /api/me/articles/stats`: Draft, Pending review, Published, Rejected (`data-testid` `stat-draft`, `stat-pending-review`, `stat-published`, `stat-rejected`). Each is a link to `/articles?status=<Status>` (My Articles, F11.3).
- When Rejected > 0 the card is highlighted and a status message reads "N article(s) need your attention".
- Primary link "New article" goes to `/articles/new`.
- The counts are fetched every time the page opens, so after a submission, withdrawal or review decision a visit (or reload) shows the current state.
- Loading: a skeleton of four cards. Error: the shared error state with a "Try again" button that refetches.

Tests: `pages/HomePage.test.tsx`.
