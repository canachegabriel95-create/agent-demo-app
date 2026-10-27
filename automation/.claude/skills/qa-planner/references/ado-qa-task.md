# QA plan task in Azure DevOps

Every PBI (User Story / Product Backlog Item) has exactly one child **Task** that tracks its QA plan.
The planner finds or creates it, moves it to in-progress at the start, writes the plan into its
Description, and closes it at the end. This is the **only** work item the planner may change.

Tools (`azure-devops` MCP, always pass `project: "DemoApp"`):
- read: `wit_work_item` (`action`: `get`, `get_batch`, `get_type`)
- write: `wit_work_item_write` (`action`: `add_child`, `update`)
- comment: `wit_work_item_comment_write` (`action: "add"`)

## 1. Resolve the PBI
- `Plan Story <ID>` → the PBI is `<ID>`.
- `Plan Task <ID>` → read the Task (`get`, `expand: "Relations"`); if it is the QA plan task (see below),
  its parent is the PBI. Otherwise plan that Task's scope, and the PBI is its parent.
- No parent PBI → stop and say so; do not create orphan tasks.

## 2. Find or create the QA plan task
- Read the PBI with `get`, `expand: "Relations"`. Children are the relations with
  `rel: "System.LinkTypes.Hierarchy-Forward"`; the id is the last segment of their `url`.
  Read them with `get_batch` (`fields`: `System.Title`, `System.WorkItemType`, `System.State`, `System.ChangedDate`).
- The QA plan task is the child of type `Task` whose title starts with `QA Plan` (case-insensitive).
  More than one → use the most recently changed one and list the others as a gap.
- None → create it with `wit_work_item_write`:
  `action: "add_child"`, `parentId: <PBI id>`, `workItemType: "Task"`,
  `items: [{ title: "QA Plan - <PBI title>", description: "QA plan in progress (qa-planner).", format: "Markdown", areaPath: <PBI area>, iterationPath: <PBI iteration> }]`.
  Example title: `QA Plan - [F10.4] Admin Portal review page with approve / reject`.

## 3. Move to in-progress
- Read the allowed states with `get_type`, `workItemType: "Task"` instead of guessing:
  in-progress is `In Progress` / `Active` / `Doing`; finished is `Closed` / `Done`.
- `update` with `updates: [{ op: "test", path: "/rev", value: <rev just read> }, { op: "add", path: "/fields/System.State", value: "<in-progress state>" }]`.
  If the task was already finished, this reopens it: a re-plan.
- Re-plan: the current Description is the previous plan. Keep every existing `FE#` / `BE#` id;
  add new ids after the highest one.

## 4. Write the plan and close
- One `update` on the task, after re-reading it for the current `rev`:
  - `{ op: "test", path: "/rev", value: <rev> }`
  - `{ op: "add", path: "/multilineFieldsFormat/System.Description", value: "Markdown" }`
  - `{ op: "add", path: "/fields/System.Description", value: "<full plan, Markdown>" }`
  - `{ op: "add", path: "/fields/System.State", value: "<finished state>" }`
- If the server rejects the Markdown format, send the Description as HTML (drop the
  `multilineFieldsFormat` operation); headings, lists and the Test Data table must stay readable.
- If the `rev` test fails, someone changed the task meanwhile: re-read it and retry once; if it fails
  again, stop and report.
- Add the comment: `QA plan written by qa-planner. <n> FE / <n> BE scenarios, <n> open questions.`

## If the run cannot finish
Leave the task in the in-progress state, do not close it, and add a comment saying what blocked the
run (e.g. Swagger unavailable). Never close a task whose Description does not hold a complete plan.

## Never
- Change the PBI, its Feature, or any other work item (fields, state, tags, comments, links).
- Write credentials, session tokens, the admin password or real personal data into the task.
