# QA Exploration task in Azure DevOps

Every PBI (User Story / Product Backlog Item) has one child **Task** that tracks its exploration runs,
next to the `QA Plan - …` task of `qa-planner`. The explorer finds or creates it, moves it to
in-progress at the start, writes the run summary into its Description, and closes it at the end. This
is the **only** work item the explorer may change.

Tools (`azure-devops` MCP, always pass `project: "DemoApp"`):
- read: `wit_work_item` (`action`: `get`, `get_batch`, `get_type`)
- write: `wit_work_item_write` (`action`: `add_child`, `update`)
- comment: `wit_work_item_comment_write` (`action: "add"`)

## 1. Resolve the PBI
- `Explore Story <ID>` → the PBI is `<ID>`.
- `Explore Task <ID>` → read the Task (`get`, `expand: "Relations"`). If it is the QA Plan or the QA
  Exploration task, its parent is the PBI and the scope is the whole PBI. Otherwise the scope is that
  Task, and the PBI is its parent.
- No parent PBI → stop and say so; do not create orphan tasks.

## 2. Find or create the QA Exploration task
- Read the PBI with `get`, `expand: "Relations"`. Children are the relations with
  `rel: "System.LinkTypes.Hierarchy-Forward"`; the id is the last segment of their `url`. Read them with
  `get_batch` (`fields`: `System.Title`, `System.WorkItemType`, `System.State`, `System.ChangedDate`,
  `System.Description`). The same read gives you the QA Plan task for the Handoff section of the skill.
- The QA Exploration task is the child of type `Task` whose title starts with `QA Exploration`
  (case-insensitive). More than one → use the most recently changed one and mention the others in the report.
- None → create it with `wit_work_item_write`:
  `action: "add_child"`, `parentId: <PBI id>`, `workItemType: "Task"`,
  `items: [{ title: "QA Exploration - <PBI title>", description: "QA exploration in progress (qa-explorer).", format: "Markdown", areaPath: <PBI area>, iterationPath: <PBI iteration> }]`.

## 3. Move to in-progress
- Read the allowed states with `get_type`, `workItemType: "Task"` instead of guessing:
  in-progress is `In Progress` / `Active` / `Doing`; finished is `Closed` / `Done`.
- `update` with `updates: [{ op: "test", path: "/rev", value: <rev just read> }, { op: "add", path: "/fields/System.State", value: "<in-progress state>" }]`.
  A finished task is reopened by this: a re-run. Its current Description is the previous summary; the
  new one replaces it (history keeps the old one).

## 4. Write the summary and close
Description (Markdown), written after `TEST_SCENARIOS.md` is final:

```markdown
## QA Exploration for <PBI_ID> — run <run-id> (<YYYY-MM-DD>)
Plan: QA Plan task <id> / archive QA_PLAN.md / none (AC only) · Environment: localhost, admin via qa-login
### Results
<n> Pass · <n> Fail · <n> Blocked · <n> not executed
| Plan ID | Scenario | Result | Bug |
|---|---|---|---|
| FE1 | <short> | Pass | — |
### Bugs (awaiting human review)
- BUG-01 <summary> — <severity_suggested> — Status: PENDING
### Blocked / human action needed
- <Plan ID>: <reason> → <action>
### Residue left in the environment
- <record type> <id> (<state>)
### Proposed test cases
<n> proposed (Status: PENDING) in `automation/archive/<PBI_ID>/TEST_SCENARIOS.md`
### Out of scope observations
- <one line each, copied from TEST_SCENARIOS.md> (or "None")
```

- One `update` on the task, after re-reading it for the current `rev`:
  - `{ op: "test", path: "/rev", value: <rev> }`
  - `{ op: "add", path: "/multilineFieldsFormat/System.Description", value: "Markdown" }`
  - `{ op: "add", path: "/fields/System.Description", value: "<summary>" }`
  - `{ op: "add", path: "/fields/System.State", value: "<finished state>" }` — **only if no scenario is
    Blocked and cleanup was verified**; otherwise leave this operation out (task stays in progress).
- If the server rejects the Markdown format, send the Description as HTML (drop the
  `multilineFieldsFormat` operation); the tables must stay readable.
- If the `rev` test fails, someone changed the task meanwhile: re-read and retry once; if it fails
  again, stop and report.
- Add the comment: `QA exploration run <run-id> by qa-explorer: <n> pass / <n> fail / <n> blocked, <n> bug files pending review.`
  When the task stays in progress, add what blocks closing (blocked scenarios, failed cleanup).

## If the run cannot finish
Leave the task in progress and add a comment saying what stopped the run (e.g. API down, login helper
failed, cleanup failed with residue ids). Never close a task whose Description does not hold a complete summary.

## Never
- Change the PBI, its Feature, the QA Plan task or any other work item (fields, state, tags, comments, links).
- Create Bug work items or test cases — those wait for a human `CONFIRMED` and belong to later agents.
- Write real personal data or anything the credential rule forbids (SKILL.md → Guardrails) into the task.
