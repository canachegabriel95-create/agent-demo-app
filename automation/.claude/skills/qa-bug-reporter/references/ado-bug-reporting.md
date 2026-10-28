# Reporting in Azure DevOps

Only the `azure-devops` MCP server is used. Always pass `project: "DemoApp"`. The process is Agile:
backlog items are `User Story` (the "PBI"), defects are `Bug`, and a new Bug starts in state `New`.

## 1. Resolve the PBI

- The PBI id is the archive folder name (`archive/<PBI_ID>/bugs/`). The front matter `work_item` may
  be the PBI or a Task under it.
- `wit_work_item` `get`, `id: <PBI_ID>`, `expand: "Relations"`. Take `System.Title`,
  `System.WorkItemType`, `System.AreaPath`, `System.IterationPath`.
- If it is a `Task`, use its parent (`System.LinkTypes.Hierarchy-Reverse`; the id is the last segment
  of the relation `url`) as the PBI.
- Not found, or no story-level parent → stop and say so. Never create unlinked Bugs.
- Optional, once per run: `get_type`, `workItemType: "Bug"`, to confirm the fields in
  `bug-template.md` exist. A missing field → leave it out and mention it in the report.

## 2. Duplicate check (before every create)

1. From the PBI relations, collect the ids of child links (`System.LinkTypes.Hierarchy-Forward`) and
   `System.LinkTypes.Related` links (older Bugs were linked as Related). Read them with `get_batch`
   (`fields`: `System.Id`, `System.Title`, `System.WorkItemType`, `System.State`, `System.Tags`).
   A `Bug` with the tag `qa-<PBI_ID>-BUG-<NN>` is the same bug. Older Bugs have no such tag; for those,
   a title starting with `[PBI <PBI_ID>] BUG-<NN>:` is the same bug.
2. If none is found that way, `search_workitem` with `searchText: "qa-<PBI_ID>-BUG-<NN>"`,
   `project: ["DemoApp"]`, `workItemType: ["Bug"]`. Only an exact tag match counts.
3. Match found → do not create. Write its id back (`bug-file-format.md` §4). If it is not a child of
   the PBI yet, add the parent link (§4). List it as "already in Azure DevOps".

## 3. Create the Bug

`wit_work_item_write`:

```json
{
  "action": "create",
  "project": "DemoApp",
  "workItemType": "Bug",
  "fields": [
    { "name": "System.Title", "value": "Revision editor: false conflict when resubmitting after failed submit" },
    { "name": "Microsoft.VSTS.TCM.ReproSteps", "value": "<Repro Steps body>", "format": "Markdown" },
    { "name": "Microsoft.VSTS.TCM.SystemInfo", "value": "<System Info block>", "format": "Markdown" },
    { "name": "Microsoft.VSTS.Common.Severity", "value": "3 - Medium" },
    { "name": "Microsoft.VSTS.Common.Priority", "value": "2" },
    { "name": "System.AreaPath", "value": "<PBI area path>" },
    { "name": "System.IterationPath", "value": "<PBI iteration path>" },
    { "name": "System.Tags", "value": "qa-explorer; qa-bug-reporter; FE3; qa-292-BUG-01" }
  ]
}
```

- Take the new id from the response.
- Check the response: `System.IterationPath` must equal the PBI's Iteration Path exactly. If it does
  not, report the mismatch for this file. Do not create the Bug in a different iteration on purpose.
- Markdown rejected → retry once with HTML (`bug-template.md` → Formatting fallback).
- A rejected field value (for example Severity) → read `get_type` `Bug` for the allowed values, fix
  the value, and retry once. If it fails again → stop for this file and report the error.
- Create Bugs one at a time and write each one back (step 6 of the skill) before the next, so an
  interrupted run never leaves an unrecorded Bug.

## 4. Link to the PBI

`wit_work_item_link_write`:

```json
{
  "action": "link",
  "project": "DemoApp",
  "updates": [ { "id": <Bug id>, "linkToId": <PBI id>, "type": "parent", "comment": "Found by qa-explorer, reported by qa-bug-reporter" } ]
}
```

- Use `parent`: the PBI becomes the Bug's parent, so the Bug shows up under the PBI. Do not use
  `related`, `tested by` or `tests` for the PBI link.
- A Bug has at most one parent. If the Bug already has a different parent, do not replace it; report it.
- Bug-to-bug links (front matter `related: [BUG-NN]`, both reported): same call,
  `{ "id": <Bug A>, "linkToId": <Bug B>, "type": "related" }`. Add each pair once.
- An error that says the link already exists counts as success.

## 5. Errors

| Situation | Action |
|---|---|
| Auth / 401 / 403 | Stop. Ask the human to reconnect the MCP server (`/mcp`). Report what was already created. |
| Create succeeded, link failed | Write back with `link … FAILED`, continue, list it in the report. The next run retries only the link. |
| PBI not found | Stop before creating anything. |
| Anything else | Stop for this file, continue with the next, report the error text. |

## Never

- Change fields, state, tags or comments of the PBI, its Feature, the QA Plan / QA Exploration tasks or
  any Bug you did not create in this run.
- Create Tasks, Test Cases or any type other than `Bug`. Upload attachments.
- Delete or unlink anything.
