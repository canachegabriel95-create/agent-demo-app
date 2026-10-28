# Output templates

Read when writing the artifacts. All paths are under `automation/archive/<PBI_ID>/` — the same folder
`qa-planner` wrote `QA_PLAN.md` to.

## `TEST_SCENARIOS.md`

```markdown
# Executed Test Scenarios for <PBI_ID>

Run: <run-id> · <YYYY-MM-DD> · plan source: archive QA_PLAN.md | ADO task <id> | none (AC only)
Environment: API build/health <status>, portals <Admin|Author>, admin signed in via qa-login (API | form)

## Test Data and Cleanup

| Plan ID | Run fixture / starting state | Setup and cleanup / role | Preflight: Ready or Blocked (evidence) | Cleanup: Verified, Failed or Not run (evidence) |
|---|---|---|---|---|
| FE1 | <synthetic ids and initial state> | <operations and role> | <evidence or blocker + human action> | <final state> |

### Residue
| Record | Id | State left in | Why it cannot be removed |
|---|---|---|---|
| author | <uuid> | Active | no user delete |

## Scenario Results

| # | Plan ID | Portal / caller | Scenario | Source | Result | Bug |
|---|---|---|---|---|---|---|
| 1 | FE1 / BE2 / AH1 | Admin / Author / Anonymous | <scenario> | Plan / Suite / AC <n> | Pass / Fail / Blocked | BUG-01 or — |

### Proposed test case — FE1
Title: As an <administrator|author> I want to check that <behaviour>
Portal: Admin / Author
Preconditions: <fixture state>
Steps: <short numbered outline>
Expected: <observable result>
Flags (from plan): automate <yes|no> · regression <yes|no>
Status: PENDING
<!-- Change to CONFIRMED to have it created in Azure DevOps, or REJECTED to drop it. -->

## Out of scope observations
- <one line each: what was seen outside the plan and AC, where, and why it was not pursued> (or "None")
```

- Ad hoc scenarios (not in the plan) get ids `AH1`, `AH2`, … continuing across re-runs; never reuse a
  plan id. A re-run updates the existing row for an id instead of adding one.
- A proposed test case block only for an FE scenario the plan marked `testcase: yes`, or an ad hoc FE
  scenario you judge worth keeping (say why in one line). Never for BE scenarios.

## `bugs/BUG-<NN>-<slug>.md`

```markdown
---
id: BUG-<NN>
work_item: <PBI or Task id>
plan_id: <FE1 / BE2 / AH1>
found_date: <YYYY-MM-DD>
area: <portal page route / METHOD /api/route>
severity_suggested: <Low | Medium | High>
related: [<BUG-NN>, ...]
---

# BUG-<NN>: <one-line summary>

## Scenario
<scenario and source: QA_PLAN.md id / AC wording>

## Steps to reproduce
1. ...

## Actual result
<observed, with evidence>

## Expected result
<what should happen, and the source: AC, CLAUDE.md rule, docs page, Swagger>

## Evidence
<status + body excerpt + X-Correlation-Id, computed style, DOM snapshot excerpt, SELECT result, screenshot path — enough to verify without re-running. Credential rule: SKILL.md → Guardrails.>

## Environment
Portal <Admin|Author> (<url>), API http://localhost:5080, browser, run <run-id>, date

---

## Reviewer Decision
Status: PENDING
<!-- Change PENDING to CONFIRMED (real bug, ready to report) or REJECTED (not a bug / expected / duplicate), then save. -->
Reviewer note:
```

Severity guide: **High** — data loss or corruption, a permission/ownership leak, a lifecycle transition
that should be refused succeeds, secrets in responses or audit. **Medium** — an AC not met, wrong status
code or error shape, missing 409. **Low** — wording, missing `data-testid` / accessible name, a missing
loading/empty/error state with no functional impact.
