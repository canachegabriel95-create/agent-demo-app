# Admin dashboard

Route `/` of the Admin Portal (nav "Dashboard"), source `frontend/admin-portal/src/pages/HomePage.tsx`. Data: `GET /api/admin/dashboard` ([api.md](api.md#dashboard-and-audit-history-administrator)).

Five cards, each a link to the list it counts:

| Card (`data-testid`) | Number | Opens |
|---|---|---|
| Active authors (`dash-active-authors`) | active authors | `/users?status=Active` |
| Suspended authors (`dash-suspended-authors`) | suspended authors | `/users?status=Suspended` |
| Available invitations (`dash-available-invitations`) | not used, not revoked, not expired | `/invitations?status=Available` |
| Pending reviews (`dash-pending-reviews`) | articles waiting for a decision | `/reviews` |
| Published articles (`dash-published-articles`) | published articles | `/audit?action=ArticleApproved` |

There is no administrator article list, so the published articles lead to the approvals in the audit history (F13.4).

- The numbers load when the page opens and again whenever the window regains focus, so they are current after working elsewhere. A refresh that fails keeps the numbers already shown.
- Loading shows a skeleton of five cards; a first load that fails shows the error with "Try again".
- The API health indicator from the first setup story stays below the cards.

Tests: `pages/HomePage.test.tsx`.
