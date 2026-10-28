# Writing test cases in Azure DevOps

Only the `azure-devops` MCP server is used. Always pass `project: "DemoApp"`. The process is Agile:
backlog items are `User Story` (the "PBI"), and a new `Test Case` starts in state `Design`.

## 1. Resolve the PBI

- The PBI id is the archive folder name (`archive/<PBI_ID>/TEST_SCENARIOS.md`).
- `wit_work_item` `get`, `id: <PBI_ID>`, `expand: "Relations"`. Take `System.Title`,
  `System.WorkItemType`, `System.AreaPath`, `System.IterationPath`.
- If it is a `Task`, use its parent (`System.LinkTypes.Hierarchy-Reverse`; the id is the last segment
  of the relation `url`) as the PBI.
- Not found, or no story-level parent → stop and say so. Never create unlinked Test Cases.
- Optional, once per run: `get_type`, `workItemType: "Test Case"`, to confirm `System.Description`
  exists. Missing → leave it out and mention it in the report.

## 2. Duplicate check (before every create)

1. From the PBI relations, collect the ids of `Microsoft.VSTS.Common.TestedBy-Forward` links (Test Cases
   that test the PBI). Read them with `get_batch` (`fields`: `System.Id`, `System.Title`,
   `System.WorkItemType`, `System.State`, `System.Description`). A `Test Case` whose Description
   contains `Covers:` with `PBI #<PBI_ID>` and `scenario <PLAN_ID>` is the same test case.
2. If none is found that way, `search_workitem` with `searchText: "PBI #<PBI_ID> scenario <PLAN_ID>"`,
   `project: ["DemoApp"]`, `workItemType: ["Test Case"]`. Only a Description with that exact
   `Covers:` line counts.
3. Match found → do not create. Write its id back (`proposed-test-case-format.md` §4). If it does not
   test the PBI yet, add the link (§4). List it as "already in Azure DevOps".
4. A Test Case with a similar title but no matching `Covers:` line (written by a human) is **not** a
   duplicate: create yours and mention the similar one in the report so a human can merge them.

## 3. Create the Test Case

Create one at a time, and write each one back (step 6 of the skill) before the next, so an interrupted
run never leaves an unrecorded Test Case.

**a. Create with steps and the Tests link** — `testplan_test_case_write`:

```json
{
  "action": "create",
  "project": "DemoApp",
  "title": "Author: withdraw a pending revision makes it editable again",
  "steps": "1. Sign in to the Author Portal as the article's author|The dashboard opens\n2. Open the revision editor of the pending revision|Status \"Revision pending review\" is shown\n3. Click \"Withdraw submission\"|A confirmation dialog opens",
  "priority": 2,
  "areaPath": "<PBI area path>",
  "iterationPath": "<PBI iteration path>",
  "testsWorkItemId": <PBI_ID>
}
```

Take the new id from the response. A rejected step text (special characters) → simplify that step as
`test-case-template.md` → Steps says and retry once. Fails again → stop for this block and report the error.

**b. Description** — `wit_work_item_write` on the id just created (never set `System.Tags`):

```json
{
  "action": "update",
  "project": "DemoApp",
  "id": <Test Case id>,
  "updates": [
    { "op": "add", "path": "/fields/System.Description", "value": "<Description block, converted to HTML>" }
  ]
}
```

`update` has no Markdown format switch, so send the Description as simple HTML (`<p>`, `<b>`, `<ul><li>`).
If the update fails, the Test Case still exists: write back, and report "description missing" for
that id so a human or the next run can add it. Never create a second Test Case to fix it.

**c. Verify** — `wit_work_item` `get` the new id with `expand: "Relations"`:
- `System.IterationPath` equals the PBI's exactly; otherwise report the mismatch (do not move it).
- `System.Description` contains the `Covers: PBI #<PBI_ID> · scenario <PLAN_ID>` line.
- A relation `Microsoft.VSTS.Common.TestedBy-Reverse` pointing to the PBI exists. Missing → §4.

## 4. Link to the PBI (only if the create did not)

`wit_work_item_link_write`:

```json
{
  "action": "link",
  "project": "DemoApp",
  "updates": [ { "id": <Test Case id>, "linkToId": <PBI_ID>, "type": "tests", "comment": "Proposed by qa-explorer, written by qa-test-case-writer" } ]
}
```

- Use `tests` from the Test Case (the PBI then shows `tested by`). Do not use `parent`, `child` or
  `related` for the PBI link.
- An error that says the link already exists counts as success.

## 5. Errors

| Situation | Action |
|---|---|
| Auth / 401 / 403 | Stop. Ask the human to reconnect the MCP server (`/mcp`). Report what was already created. |
| Create succeeded, link failed | Write back with `link … FAILED`, continue, list it in the report. The next run retries only the link. |
| Create succeeded, description failed | Write back, continue, list it in the report. |
| PBI not found | Stop before creating anything. |
| Anything else | Stop for this block, continue with the next, report the error text. |

## Never

- Change fields, state, tags, links or comments of the PBI, its Feature, the QA Plan / QA Exploration
  tasks, Bugs, or any Test Case you did not create in this run.
- Create test plans or suites, or add Test Cases to a suite (a human does that).
- Create any work item type other than `Test Case`. Upload attachments.
- Delete or unlink anything.
