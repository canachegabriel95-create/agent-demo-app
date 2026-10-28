# Traceability

Maps stories to the automated tests that cover them.

## F17 — Frontend E2E (Playwright)

| Story | Spec | Test |
|---|---|---|
| F17.1 | `automation/fe_automatio/tests/admin/login.smoke.spec.ts` | admin login page renders the sign-in form |
| F17.1 | `automation/fe_automatio/tests/author/login.smoke.spec.ts` | author login page renders the sign-in form |
| F17.2 | `automation/fe_automatio/tests/admin/login.spec.ts` | valid admin credentials open the dashboard and store the session token (AC1) |
| F17.2 | `automation/fe_automatio/tests/admin/login.spec.ts` | wrong password / unknown email / an author account show the generic error (AC2) |
| F17.2 | `automation/fe_automatio/tests/admin/login.spec.ts` | empty email and password show field validation messages (AC3) |
| F17.2 | `automation/fe_automatio/tests/admin/login.spec.ts` | a suspended admin cannot sign in (AC4) |
| F17.2 | `automation/fe_automatio/tests/admin/login.spec.ts` | opening `/`, `/users`, `/invitations` without a session redirects to /login (AC5) |
| F17.2 | `automation/fe_automatio/tests/admin/login.spec.ts` | submit button is disabled and shows progress while signing in (AC6) |
| F17.3 | `automation/fe_automatio/tests/admin/layout.spec.ts` | every navigation link opens its page and is marked as current (AC1) |
| F17.3 | `automation/fe_automatio/tests/admin/layout.spec.ts` | logging out clears the session, returns to /login and revokes the token (AC2) |
| F17.3 | `automation/fe_automatio/tests/admin/layout.spec.ts` | a 401 from an API call clears the session and redirects to /login (AC3) |
| F17.3 | `automation/fe_automatio/tests/admin/layout.spec.ts` | suspending the signed-in admin ends the session on the next request (AC4) |
| F17.3 | `automation/fe_automatio/tests/admin/layout.spec.ts` | an unknown route shows the not-found view (AC5) |
| F17.4 | `automation/fe_automatio/tests/admin/dashboard.spec.ts` | counters match the data created through the API (AC1) |
| F17.4 | `automation/fe_automatio/tests/admin/dashboard.spec.ts` | shows a loading state until the numbers arrive / shows an error state when the dashboard cannot be loaded (AC2) |
| F17.4 | `automation/fe_automatio/tests/admin/dashboard.spec.ts` | the dash-* cards open their list pages (AC3) |
| F17.5 | `automation/fe_automatio/tests/admin/invitations.spec.ts` | lists invitations with the status Available, Used, Expired and Revoked (AC1) |
| F17.5 | `automation/fe_automatio/tests/admin/invitations.spec.ts` | filters by status and searches by email / paginates 20 invitations per page (AC2) |
| F17.5 | `automation/fe_automatio/tests/admin/invitations.spec.ts` | revoking asks for confirmation and then marks the invitation Revoked (AC3) |
| F17.5 | `automation/fe_automatio/tests/admin/invitations.spec.ts` | only Available invitations can be revoked (AC4) |
| F17.5 | `automation/fe_automatio/tests/admin/invitations.spec.ts` | revoking an invitation that changed meanwhile shows the conflict message (AC5) |
| F17.5 | `automation/fe_automatio/tests/admin/invitations.spec.ts` | empty / loading / error states (AC6) |
| F17.6 | `automation/fe_automatio/tests/admin/create-invitation.spec.ts` | creating with the default 7 days shows the code once and lists the invitation (AC1) |
| F17.6 | `automation/fe_automatio/tests/admin/create-invitation.spec.ts` | 1 and 30 days accepted, 0 and 31 rejected (AC2) |
| F17.6 | `automation/fe_automatio/tests/admin/create-invitation.spec.ts` | missing, invalid and over-254-character emails are rejected (AC3) |
| F17.6 | `automation/fe_automatio/tests/admin/create-invitation.spec.ts` | a second active invitation for the same email is rejected (AC4) — **expected failure: known defect, the API accepts it** |
| F17.6 | `automation/fe_automatio/tests/admin/create-invitation.spec.ts` | cancel closes the dialog without creating an invitation (AC5) |
| F17.7 | `automation/fe_automatio/tests/admin/users-list.spec.ts` | lists authors with their name, email and status (AC1) |
| F17.7 | `automation/fe_automatio/tests/admin/users-list.spec.ts` | searches by name and by email / filters by status (AC1) |
| F17.7 | `automation/fe_automatio/tests/admin/users-list.spec.ts` | paginates 20 authors per page (AC2) |
| F17.7 | `automation/fe_automatio/tests/admin/users-list.spec.ts` | clicking an author opens the detail page (AC3) |
| F17.7 | `automation/fe_automatio/tests/admin/users-list.spec.ts` | empty / loading / error states (AC4) |
| F17.7 | `automation/fe_automatio/tests/admin/users-list.spec.ts` | passwords and hashes are never sent or shown (AC5) |
| F17.8 | `automation/fe_automatio/tests/admin/user-detail.spec.ts` | editing the name and biography saves them; email and role are read-only / the name is trimmed (AC1) |
| F17.8 | `automation/fe_automatio/tests/admin/user-detail.spec.ts` | a 1-character name, a name over 100 characters, a biography over 1000 characters are rejected (AC2) |
| F17.8 | `automation/fe_automatio/tests/admin/user-detail.spec.ts` | suspending needs a reason of at least 10 characters and then marks the author Suspended (AC3) |
| F17.8 | `automation/fe_automatio/tests/admin/user-detail.spec.ts` | a suspended author's existing session is rejected on the next request (AC4) |
| F17.8 | `automation/fe_automatio/tests/admin/user-detail.spec.ts` | reactivating restores the Active status (AC5) |
| F17.8 | `automation/fe_automatio/tests/admin/user-detail.spec.ts` | saving over a newer version shows the conflict message (AC6) |
| F17.8 | `automation/fe_automatio/tests/admin/user-detail.spec.ts` | an unknown author id shows the not-found state (AC7) |
| F17.9 | `automation/fe_automatio/tests/admin/review-queue.spec.ts` | lists only pending articles, with title and author (AC1) |
| F17.9 | `automation/fe_automatio/tests/admin/review-queue.spec.ts` | sorts by submission date / paginates 20 articles per page / an approved or rejected article leaves the queue after a refresh (AC2) |
| F17.9 | `automation/fe_automatio/tests/admin/review-queue.spec.ts` | a row opens the review detail page (AC3) |
| F17.9 | `automation/fe_automatio/tests/admin/review-queue.spec.ts` | empty / loading / error states (AC4) |
| F17.10 | `automation/fe_automatio/tests/admin/review-detail.spec.ts` | shows the title, summary and body as plain text (AC1) |
| F17.10 | `automation/fe_automatio/tests/admin/review-detail.spec.ts` | approving publishes the article and removes it from the queue (AC2) |
| F17.10 | `automation/fe_automatio/tests/admin/review-detail.spec.ts` | rejecting needs a reason of at least 10 characters and stores it (AC3) |
| F17.10 | `automation/fe_automatio/tests/admin/review-detail.spec.ts` | approving an article the author withdrew meanwhile shows the conflict message (AC4) |
| F17.10 | `automation/fe_automatio/tests/admin/review-detail.spec.ts` | review actions are blocked for a suspended author's article (AC5) |
| F17.10 | `automation/fe_automatio/tests/admin/review-detail.spec.ts` | earlier decisions stay in the history after the article is resubmitted (AC6) |
| F17.11 | `automation/fe_automatio/tests/admin/audit.spec.ts` | shows events for an invitation created, a user suspended and an article approved (AC1) |
| F17.11 | `automation/fe_automatio/tests/admin/audit.spec.ts` | filters by event type / filters by date / paginates 20 events per page (AC2) |
| F17.11 | `automation/fe_automatio/tests/admin/audit.spec.ts` | never shows a password, a token or a full invitation code (AC3) |
| F17.11 | `automation/fe_automatio/tests/admin/audit.spec.ts` | empty / loading / error states (AC4) |
