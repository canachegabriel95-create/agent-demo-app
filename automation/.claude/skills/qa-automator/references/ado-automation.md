# Azure DevOps: QA Automation task, Test Cases, tags

Only the `azure-devops` MCP server. Always pass `project: "DemoApp"`. Process: Agile (backlog items are
`User Story` = "PBI").

Tools:
- read: `wit_work_item` (`action`: `get`, `get_batch`, `get_type`)
- write: `wit_work_item_write` (`action`: `add_child`, `update`)
- comment: `wit_work_item_comment_write` (`action: "add"`)

## 1. Resolve the PBI and the QA Automation task

- `Automate PBI#<ID>` → the PBI is `<ID>`. A Task id → its parent (`System.LinkTypes.Hierarchy-Reverse`;
  the id is the last segment of the relation `url`). No story-level parent → stop and say so.
- `get` the PBI with `expand: "Relations"`. Keep `System.Title`, `System.AreaPath`, `System.IterationPath`.
- Children: relations with `rel: "System.LinkTypes.Hierarchy-Forward"`. Read them with `get_batch`
  (`fields`: `System.Title`, `System.WorkItemType`, `System.State`, `System.ChangedDate`).
- The QA Automation task is the child `Task` whose title starts with `QA Automation` (case-insensitive).
  More than one → the most recently changed; mention the others in the report.
- None → create it:
  `wit_work_item_write` `action: "add_child"`, `parentId: <PBI id>`, `workItemType: "Task"`,
  `items: [{ title: "QA Automation - <PBI title>", description: "Test automation in progress (qa-automator).", format: "Markdown", areaPath: <PBI area>, iterationPath: <PBI iteration> }]`.
- In progress: read the states with `get_type`, `workItemType: "Task"` (in progress = `Active` /
  `In Progress` / `Doing`; finished = `Closed` / `Done`), then
  `update` with `[{ op: "test", path: "/rev", value: <rev> }, { op: "add", path: "/fields/System.State", value: "<in-progress>" }]`.
  A finished task is reopened by this (a re-run); the new summary replaces the old Description.

## 2. Read the Test Cases to automate

- From the PBI relations, collect the ids of `Microsoft.VSTS.Common.TestedBy-Forward` links.
- `get_batch` them with `fields`: `System.Id`, `System.Title`, `System.WorkItemType`, `System.State`,
  `System.Tags`, `System.Description`, `Microsoft.VSTS.TCM.Steps`, `System.Rev`.
- Keep only `System.WorkItemType == "Test Case"` whose `System.Tags` (semicolon-separated) contains
  `Should be automated` (compare trimmed, case-insensitive). If the input named TC ids, keep only those.
- Already tagged `Automated` and not `Should be automated` → skip and list as "already automated".
- `Microsoft.VSTS.TCM.Steps` is XML: each `<step>` has two `<parameterizedString>` — the first is the
  Action, the second the Expected result (HTML-encoded). Read the Expected results: they are what the
  test asserts. Preconditions are usually in the Description.

## 3. Change the tag (only after pass + verified cleanup)

For each Test Case automated in this run, re-read it (`get`, for the current `System.Rev` and
`System.Tags`), build the new tag list — the old tags minus `Should be automated`, plus `Automated`,
every other tag kept as it was — and send one update:

```json
{
  "action": "update",
  "project": "DemoApp",
  "id": <Test Case id>,
  "updates": [
    { "op": "test", "path": "/rev", "value": <rev> },
    { "op": "add", "path": "/fields/System.Tags", "value": "Automated; Regression" }
  ]
}
```

- `System.Tags` is replaced as a whole: always send the complete list, never only the new tag.
- `rev` test fails → re-read and retry once; fails again → report, do not change the tag.
- A Test Case whose test is `fixme` or failing keeps `Should be automated`.

## 4. Summary and close

Description of the QA Automation task (Markdown), written at the end:

```markdown
## QA Automation for <PBI_ID> (<YYYY-MM-DD>)
Project: automation/fe_automation · Run: `npm --prefix fe_automation test -- --grep @PBI-<PBI_ID>`
### Test Cases
| Test Case | Title | Test | Result | Tag |
|---|---|---|---|---|
| 131 | Search by email | tests/admin/authors.spec.ts › Admin: search by email … | Pass (3/3) | Automated |
### Data and cleanup
Created per test: <records>. Leftover check: <table: 0 rows, …>.
### Not automated / needs a human
- TC <id>: <reason> (application defect, unclear expected result, missing locator, …)
### Files
- added/changed: <paths>
```

- One `update` after re-reading the task for its `rev`:
  `test /rev`, `add /multilineFieldsFormat/System.Description = "Markdown"`,
  `add /fields/System.Description = <summary>`, and `add /fields/System.State = <finished>` **only if
  every selected Test Case was automated and the leftover check was empty**.
- Markdown rejected → send the Description as simple HTML (drop the format operation).
- Comment: `QA automation by qa-automator: <n> automated / <n> fixme / <n> not automated, cleanup verified (<n> rows left).`
  When the task stays in progress, the comment says what blocks it.

## If the run cannot finish

Leave the task in progress and comment what stopped it (app down, login helper failed, cleanup left
rows — with the table and tag). Never close a task without a complete summary.

## Never

- Change the PBI, its Feature, the QA Plan / QA Exploration tasks, Bugs, or any Test Case field other
  than `System.Tags` of the ones automated in this run.
- Create Test Cases, Bugs, plans or suites. Delete or unlink anything.
