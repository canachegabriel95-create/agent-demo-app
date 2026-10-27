# Traceability

Acceptance criteria mapped to automated tests. This file currently covers **Feature 17 – Article Revision and Reapproval** (stories F17.1 and F17.2); F16 adds the other features. Backend tests live in `backend/tests`, portal tests next to the code, end-to-end tests in `e2e/tests`. Behaviour is described in [article-revisions.md](article-revisions.md).

## F17.1 – Revision and reapproval

| Acceptance criterion | Tests |
|---|---|
| An owner can create a revision of a Published article; the published version stays unchanged and visible | `RevisionAuthorTests.Creating_a_revision_copies_the_published_content_into_a_draft_and_leaves_the_article_alone`, `RevisionAuthorTests.The_article_shows_the_open_revision_and_stops_offering_revise`, `RevisionDecisionTests.The_feed_shows_the_published_version_until_the_revision_is_approved`; e2e `a revision keeps the published version visible…` |
| Only a Published article can be revised | `RevisionAuthorTests.Only_a_published_article_can_be_revised`, `ArticleRevisionTests.CreateFrom_requires_a_published_article` |
| A revision is a separate version with its own status, submission timestamp and submission version; only one open revision per article | `ArticleRevisionTests`, `SchemaTests` (`article_revisions`, `Only_one_open_revision_per_article_is_allowed`), `RevisionAuthorTests.A_second_revision_cannot_be_started_while_one_is_open`, `RevisionAuthorTests.Racing_creates_start_exactly_one_revision` |
| The revision follows Draft → PendingReview → Approved/Rejected, with withdraw and resubmit; same validation as article submit | `RevisionLifecycleTests` (all 40 combinations), `ArticleRevisionTests`, `RevisionAuthorTests` (edit, submit rules, withdraw, resubmit, read-only while pending) |
| Approving replaces the published content atomically; the previous version is kept in history | `RevisionDecisionTests.Approving_replaces_the_published_content_and_keeps_the_replaced_version_in_history`, `RevisionDecisionTests.A_second_revision_can_follow_the_first_and_history_grows`, `ArticleRevisionTests.ApplyRevision_*` |
| Rejecting (reason 10-1000) leaves the published version untouched; the owner sees the reason | `RevisionDecisionTests.Rejecting_leaves_the_published_version_and_the_feed_unchanged_and_shows_the_author_the_reason`, `RevisionDecisionTests.Rejecting_needs_a_reason_of_ten_to_a_thousand_characters`, portal `RevisionPage.test.tsx` (after a rejection) |
| A rejected revision stays editable and can be resubmitted; earlier decisions are preserved | `RevisionDecisionTests.A_rejected_revision_can_be_reworked_resubmitted_and_approved_and_earlier_decisions_stay`; e2e `a revision keeps the published version visible…` |
| Simultaneous approve/reject/withdraw → exactly one succeeds, the others get 409; two revisions never published at once | `RevisionDecisionTests.Racing_approvals_by_several_administrators_publish_the_revision_exactly_once`, `…A_racing_approve_and_reject_record_exactly_one_decision`, `…A_racing_withdraw_and_approve_succeed_exactly_once_between_them`, `…A_revision_is_decided_once`, `…A_stale_version_or_a_newer_submission_is_a_conflict_that_says_what_is_current`, `…Revisions_of_different_articles_are_decided_independently`; e2e `two administrators deciding on the same revision…` |
| The feed always shows the latest approved version, never a pending or rejected revision | `RevisionDecisionTests.The_feed_shows_the_published_version_until_the_revision_is_approved`, `…Rejecting_leaves_the_published_version_and_the_feed_unchanged…`, `FeedTests` |
| Review is blocked for revisions of suspended authors; non-owners get 404 | `RevisionDecisionTests.Review_is_blocked_for_a_suspended_author_and_nothing_changes`, `RevisionAuthorTests.Somebody_elses_article_is_a_404_and_administrators_and_anonymous_callers_are_refused`, `RevisionAuthorTests.A_suspended_author_cannot_start_a_revision` |
| The admin queue distinguishes revisions from new submissions and shows the published version next to the revision | `RevisionDecisionTests.The_queue_lists_revisions_next_to_new_submissions_and_tells_them_apart`, `…The_queue_filters_and_pages_over_both_kinds`, `…The_detail_shows_the_revision_next_to_the_published_version`; portal `RevisionReviewPage.test.tsx`; e2e |
| The author dashboard shows the revision status next to the published article | `RevisionAuthorTests.The_article_shows_the_open_revision…`; portal `RevisionPage.test.tsx` (open revision on the article page), `MyArticlesPage` row |
| Migration; Swagger documents endpoints, schemas and status codes | `SchemaTests`, `SwaggerTests.The_revision_endpoints_are_documented_with_their_status_codes` |
| Portals: loading, empty and error states, 409 handling, text rendered as text | `RevisionPage.test.tsx` (Author Portal), `RevisionReviewPage.test.tsx` (Admin Portal) |

## F17.2 – Audit history for revisions

| Acceptance criterion | Tests |
|---|---|
| An audit event per action (created, submitted, withdrawn, approved, rejected) in the same transaction, by the right actor | `RevisionAuditTests.Every_revision_action_writes_one_event_by_the_actor`, `RevisionAuditTests.A_lost_decision_race_writes_exactly_one_event` |
| No content, passwords or tokens in the events | `RevisionAuditTests.Events_never_contain_the_content_of_the_revision` |
| Refused or conflicting requests write nothing | `RevisionAuditTests.Refused_or_conflicting_requests_write_no_event` |
| Events appear in the admin audit history (action filter); authors cannot read it | `RevisionAuditTests.The_events_appear_in_the_admin_audit_history_and_authors_cannot_read_it`, `AuditHistoryTests.The_actions_endpoint_lists_every_action_name` |

## Running the tests

```bash
dotnet test backend/ContentReview.sln                       # unit + integration (Testcontainers needs Docker)
npm --prefix frontend/admin-portal run test -- --run
npm --prefix frontend/author-portal run test -- --run
# End to end, against a running stack (API 5080, portals 5173/5174, a provisioned administrator):
cd e2e && npm ci && E2E_ADMIN_EMAIL=… E2E_ADMIN_PASSWORD=… npx playwright test
```

The end-to-end tests create their own authors and articles with unique emails; they need no demo data.
