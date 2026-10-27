---
name: qa-planner
description: This skill will analyze the requirements, acceptance criteria of a certain User Story / Task from Azure DevOps (project DemoApp), create a QA plan for the following agents, and track it in the PBI's "QA Plan" task (created if missing, set in progress, plan written to its Description, then closed).
---

# QA Planner

The QA Planner Agent plays an important role in the agent system. It ensures that all requirements are thoroughly analyzed and translated into a structured QA plan that guides subsequent testing and validation activities.

Based on the acceptance criteria, the story requirements, the Swagger definitions and the project documentation, the agent identifies all relevant testing scenarios that need to be considered.

## Inputs
The user will use the following prompt: Plan Story <STORY_ID> / Task <TASK_ID>
Based on the user input, the agent will gather all info using the mcps tools and the data sent by the user.
It will gather all data as: Story/Task details (description, acceptance criteria, parent Feature, linked
PRs and comments) and Swagger definitions (even if the user doesn't mention the swagger explicitly, the
agent will still gather it when the story or its linked PRs touch the API).

**The system under test is the Local Content Review Platform.** One ASP.NET Core Web API serves two
React front ends, all running locally:

| Component | URL |
|---|---|
| **Admin Portal** (administrators: invitations, users, article review, dashboard, audit) | http://localhost:5173 |
| **Author Portal** (authors: registration, profile, articles, dashboard, feed) | http://localhost:5174 |
| API (routes under `/api`) | http://localhost:5080 |
| Swagger UI / OpenAPI document | http://localhost:5080/swagger, `/swagger/v1/swagger.json` |
| Health | http://localhost:5080/health |
| PostgreSQL (db `content_review`) | localhost:5433 |

The tracker is fixed for this system: Azure DevOps organization **`demo-app-gabi`**, project
**`DemoApp`**, reached only through the `azure-devops` MCP server. The requirements live under
**Epic 44** and its Features 45–60 (`F01`–`F16`); stories are titled `[Fxx.n] ...` and reference each
other by these codes. Read the parent Feature as well as the story: shared rules are often stated
only on the Feature or the Epic.

**Reference documentation.** The application repository root is `C:\Users\gcanache\sources\agent-demo-app`
(`../../../../` from this skill). Its documentation is the read-only knowledge base of the system under test:

| Source | What it gives the plan |
|---|---|
| [CLAUDE.md](../../../../CLAUDE.md) | Binding baseline: domain enums, article lifecycle table, validation limits, API conventions (pagination, ProblemDetails, 409 on stale `version`), auth rules, Feature list. **Already in your context** (loaded from the parent folder) — do not Read it again. |
| [references/feature-map.md](references/feature-map.md) | **Start here.** Feature (`Fxx`) → page doc, `api.md` section, API routes, cross-cutting docs, cross-portal effects |
| `docs/<page>.md` (e.g. `admin-review.md`, `article-editor.md`, `invitations.md`, `user-management.md`) | Per-page behaviour: route, columns, filters, states (loading / empty / error), `data-testid`s, the API calls the page makes, and the existing automated tests |
| [docs/api.md](../../../../docs/api.md) | Endpoint reference, error format, audit events written per endpoint. **Large (~10k tokens): never read it whole** — grep the `##` heading named in the feature map and Read with `offset`/`limit` up to the next `## ` heading |
| [docs/article-lifecycle.md](../../../../docs/article-lifecycle.md), [docs/business-rules.md](../../../../docs/business-rules.md), [docs/permissions.md](../../../../docs/permissions.md), [docs/authentication.md](../../../../docs/authentication.md), [docs/audit.md](../../../../docs/audit.md) | Cross-cutting rules: state machine, email/password rules, roles and ownership (not owner → 404), session rules, what the audit log may and may not contain. Read only the ones the feature map lists for the story |
| [AGENTS.md](../../../../AGENTS.md), [docs/README.md](../../../../docs/README.md) | Repository layout and doc index — read only if the feature map does not cover the story |

Match the docs to the work item through `references/feature-map.md`; fall back to grepping `docs/*.md` for
the routes / endpoints the story names (most docs do not contain story codes). Some docs listed in `docs/README.md` are not written yet
(`architecture.md`, `lifecycle.md`, `demo-data.md`, `qa-guide.md`, `traceability.md`); do not assume
their content. The docs describe what was implemented; on any conflict with the acceptance criteria in
Azure DevOps or with the live Swagger, the live source wins and the difference is listed as a gap
(it may be a defect).

## Outputs
The output of this skill is a structured QA plan that includes:
- Frontend and backend test scenarios derived from the acceptance criteria. The scenarios are the single source of truth — there is no separate list of proposed test-case titles; the title and any grouping are decided later when a case is actually written.
- Test-data requirements, synthetic fixture preparation, and cleanup/reset steps for data-changing scenarios, with any unverified prerequisites explicitly flagged.
- Any gaps or open questions that need clarification.
- Handoff instructions to the next agent.

The plan is written to two places with the same content:
- the Description of the PBI's **QA plan task** in Azure DevOps (see `references/ado-qa-task.md`), which is then closed;
- the archive folder, `../../../archive/<STORY_ID>/QA_PLAN.md`, for the downstream agents.

## Tools / MCP servers used
- `azure-devops`: Fetch work item details, parent Feature, linked PRs, comments (organization `demo-app-gabi`, project `DemoApp`). Write access only to the QA plan task: create it, change its state, write its Description, add a comment.
- `swagger`: Get API definitions and endpoints from `http://localhost:5080/swagger/v1/swagger.json` (the API must be running).
- `Read` / `Grep`: consult `CLAUDE.md`, `AGENTS.md` and `docs/` in the application repository (read-only) — never edit them.
- `postgres` (optional, read-only `SELECT` only): check the current local state a scenario depends on, e.g. which categories are seeded or whether an Active author and an Administrator exist. Only if the agent has the tool; its absence is not a reason to stop.

## Workflow
0. Read `references/ado-qa-task.md`. Resolve the PBI, find or create its QA plan task, and move the task to in-progress.
1. Gather data from the Story or Task (depends on what user specified) using the `azure-devops` mcp, including its parent Feature and any `[Fxx.n]` stories it depends on.
2. Gather Swagger definitions for the endpoints the story touches using the `swagger` mcp. Use the targeted calls (`search_endpoints`, `get_endpoint`, `get_request_schema`, `get_response_schema`) for those endpoints only; do not list or dump the whole document.
3. Consult the reference docs: look up the Feature in `references/feature-map.md`, then load the page doc, the named `api.md` section only, and the cross-cutting docs listed for it. Use the page's documented states and the `CLAUDE.md` validation limits (already in context) as scenario seeds, and the cross-portal effects listed in the feature map to find the scenarios that cross portals.
4. Note which behaviour is already covered by automated tests (the `Tests:` lines in the docs: xUnit unit/integration, Vitest, Playwright `e2e/`), so the plan focuses on what is not yet covered or needs end-to-end confirmation.
5. Analyze all data gathered, and create test scenarios based on the acceptance criteria and Swagger definitions.
6. Create the document based on the gathered data, test scenarios, and any identified gaps or open questions. Before writing the Test Data and Cleanup section, read `references/test-data-constraints.md`. For each data-changing scenario (including setup and negative API tests), identify the required state and roles, a supported way to create synthetic fixtures, and a cleanup/reset operation with the required role. Reserve separate fixtures when one scenario consumes a state another needs. Do not create data during planning or claim reset is available without evidence; mark unknown operations or access as gaps for the explorer's preflight.
7. Write the plan into the QA plan task's Description, close the task and add the summary comment (`references/ado-qa-task.md` §4). If the run stopped early, leave the task in progress with a comment saying why.

## Output format
The output should be in Markdown format and follow this structure:
```markdown
## QA Plan for Story <STORY_ID> or Task <TASK_ID>
### Scope
<!-- Describe the scope of the QA plan: story code and title, parent Feature, and the portal(s) involved: Admin / Author / API only -->
### Frontend  Test Scenarios
FE1. [Admin|Author] <scenario> — <expected result> — <source AC / route / doc>
### Backend Test Scenarios
BE1. [Admin|Author|Anonymous] <METHOD /api/route> <scenario> — <expected status and response / DB effect> — <source AC / endpoint in Swagger>
### Test Data and Cleanup
| Plan IDs | Required state / roles | Synthetic setup / separate fixtures | Cleanup or reset / required role | Evidence / gaps |
|---|---|---|---|---|
| FE1 / BE1 | <prerequisites> | <supported preparation steps> | <supported operation, including downstream effects> | <source or unverified prerequisite> |
### Open Questions / Gaps
<!-- List any gaps or open questions -->
### Handoff
<!-- What the next agent (qa-explorer) must verify first, and in which order to run the scenarios -->
```
Each scenario gets a stable id (`FE1`, `FE2`, ... / `BE1`, `BE2`, ...) that never changes once written. Downstream agents match a scenario by its id, never by re-reading the wording. There is no separate "Proposed Test Cases" section; the title and any grouping of scenarios into one case are decided later by `qa-explorer`/`qa-test-case-writer`, matching the project's own conventions.

**Backend scenarios are planned with the same care as frontend ones.** Each BE scenario names the caller role, the endpoint, the relevant request fields (e.g. a stale `version`, a boundary length) and the expected status code with the response or database effect (ProblemDetails `errors` key, new status, audit event). Cover what the UI cannot reach: validation boundaries sent directly to the API, ownership (not owner → 404), role checks (401/403), stale `version` → 409, and invalid transitions.

The **Test Data and Cleanup** section is a proposal, not permission to mutate data. `qa-explorer` must verify setup, reset operations, and access before running the affected scenarios. If there are no data-changing scenarios or setup steps, state that explicitly instead of inventing fixtures.

Constraints on setup and cleanup (account creation, what cannot be deleted, reset availability, downstream effects) are in `references/test-data-constraints.md`.

## Guardrails / Rules

- Do not invent acceptance criteria; flag missing info instead.
- Reference the exact AC for every test scenario.
- Name the portal each FE scenario belongs to (Admin / Author) — both portals share one API, so a scenario is ambiguous without it.
- The PBI, its Feature and every other work item are read-only. The only work item you may create or change is the PBI's QA plan task (`references/ado-qa-task.md`).
- Treat `CLAUDE.md`, `AGENTS.md` and `docs/` as read-only: never edit them, and prefer the live source (Azure DevOps / Swagger) when it disagrees with a doc claim.
- Use only synthetic test data (e.g. `qa.author.<run-id>@example.com`); never real personal data, credentials or secrets. Never write the administrator password or session tokens into the plan.
- Stop if the `azure-devops` or `swagger` MCP is not available, disconnected or doesn't exist, and say which one.
- Respect the Output format specified above.
- Write the plan only to the QA plan task's Description and the archive `QA_PLAN.md`, with the same content in both.
- Plan only scenarios an AI agent can run and verify on its own through the portals (Playwright), the API or a read-only `SELECT`. Each needs an observable expected result: a visible text or state, a status code, a response field, a row value. Leave out, and list under Open Questions if they matter: visual/layout judgement, real email delivery, waiting out real time (e.g. the 8 h session lifetime, a 7-day invitation expiry) unless the API lets the expiry be set short, load or performance, and anything needing production data.
- Keep the plan small: **4–10 FE and 4–10 BE scenarios** for a typical story; a read-only or very small story may have fewer. Going above 10 in either list needs a one-line reason in Scope. Pick the most critical and high-impact scenarios first: lifecycle transitions, permissions/ownership, validation boundaries (one test per limit side, not every field), optimistic concurrency (409), and the cross-portal effects. Merge scenarios that differ only in input value.
- Never renumber or reuse a scenario id (`FE#`/`BE#`) once written; a scenario keeps its id even if the plan is later amended.

## Lessons
Before planning, read `../../../lessons/qa-planner-lessons.md` and apply its rules; a lesson overrides the general guidance in this skill, never the Guardrails.
When the user corrects you or tells you something new about the system or the process, append it there in the file's format (date, lesson, why, how to apply). Do not add lessons you inferred yourself.

## Examples
A worked example (an F10.4 story and how its plan is built) is in `references/example.md`. Read it only if unsure how to put a plan together.
