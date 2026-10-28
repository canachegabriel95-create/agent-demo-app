# Example

Read this only if unsure how a finished plan should be put together.

You have to plan the following story (illustrative; the real acceptance criteria always come from Azure DevOps):

Title: [F10.4] Admin Portal review page with approve / reject

Description:
Administrators open a submitted article from the review queue, read the submitted content, and approve
or reject it. A rejection needs a reason that the author can read.

Acceptance Criteria:
The review page shows the submitted snapshot (title, summary, body, category, submission number) read-only
Approve moves the article to Published
Reject requires a reason of 10–1000 characters and moves the article to Rejected
Earlier decisions are listed in the review history, newest submission first
Deciding on an article whose version changed (e.g. the author withdrew it) shows a conflict message instead of overwriting
Approve and Reject are not available when the author is suspended

Input from user: Plan Story <STORY_ID>.
Also, you will use the mcps servers to gather all needed data for the QA plan.

Output:
The agent reads the story and its parent Feature F10 through the `azure-devops` mcp, looks up F10 in
`references/feature-map.md`, fetches only the `/api/admin/reviews/...` and `/api/admin/articles/{id}`
endpoints in `swagger`, reads `admin-review.md` (review page, states, API calls; it ignores the `Tests:` lines), then
loads `article-lifecycle.md` for the transitions and only the "Review queue (administrator)" section of
`api.md` for the 409 and audit behaviour (`ArticleApproved` / `ArticleRejected`). It plans FE scenarios
in the Admin Portal plus the cross-portal check in the Author Portal (the author sees the rejection
reason), BE scenarios for the reason limits, the stale `version` 409 and the suspended-author block,
and a test-data table that needs one fresh PendingReview article per decision (approve and reject each
consume one). It then creates a document in `../../../archive/<STORY_ID>/QA_PLAN.md` containing the QA plan.
