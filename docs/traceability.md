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
