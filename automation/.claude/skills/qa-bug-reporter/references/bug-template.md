# Bug template (Azure DevOps)

One Bug work item per accepted bug file. Keep it simple: a developer should understand the problem
from the title and reproduce it from the steps without opening anything else.

## Fields

| Field | Value |
|---|---|
| `System.Title` | A short, readable title (see **Title** below). No `[PBI …] BUG-NN` prefix. |
| `Microsoft.VSTS.TCM.ReproSteps` | The body below, `format: "Markdown"`. |
| `Microsoft.VSTS.TCM.SystemInfo` | The System Info block below, `format: "Markdown"`. |
| `Microsoft.VSTS.Common.Severity` | From `severity.md`, for example `3 - Medium`. |
| `Microsoft.VSTS.Common.Priority` | From `severity.md`, `1`–`4`. |
| `System.AreaPath` | The PBI's Area Path. |
| `System.IterationPath` | The PBI's Iteration Path, copied exactly (required; the Bug lives in the same iteration as the PBI). |
| `System.Tags` | `qa-explorer; qa-bug-reporter; <plan_id>; qa-<PBI_ID>-BUG-<NN>` (for example `qa-explorer; qa-bug-reporter; FE3; qa-292-BUG-01`). |

Do not set `System.AssignedTo`, `System.State` or `System.Description`. The Agile Bug form shows
Repro Steps and System Info, not Description.

The `qa-<PBI_ID>-BUG-<NN>` tag is the key for the duplicate check, so always add it exactly like this.

## Title

A developer or PO scanning the backlog should understand what is broken from the title alone.

- Shape: `<Where>: <what goes wrong>[ when <trigger>]`.
- **Where** = the screen or API area in user words: `Author Portal revision editor`,
  `Admin Portal review queue`, `API revisions`.
- **What goes wrong** = the visible symptom, not the cause: "false conflict", "shows no status",
  "accepts a too-short title".
- Max **80 characters**. Sentence case. One problem per title.
- No backticks, ids, UUIDs, status codes, field names in code form, URLs, run ids, or chains of
  "so …" / "because …". Those belong in Repro Steps.
- The full one-line summary from the bug file stays in the Repro Steps `**Summary:**` line.
- A reviewer `Title:` override (`bug-file-format.md`) is used as written. If it is over 80
  characters, use it anyway and mention that in the report.

| Bad | Good |
|---|---|
| [PBI 292] BUG-01: Revision editor keeps a stale version after a submit that saved first and then failed validation, so the next save/submit shows a false "changed in another tab" conflict | Revision editor: false conflict when resubmitting after failed submit |
| API returns 200 instead of 400 for PUT /api/articles/{id} with title length 4 | API articles: draft save accepts a title shorter than allowed |

## Repro Steps body

```markdown
**Summary:** <one-line summary, as in the bug file>
**Where:** <area from the front matter> · **Tested PBI:** #<PBI_ID> · **Scenario:** <plan_id>

### Steps to reproduce
1. <copied from the bug file, one step per line>

### Actual result
<copied from the bug file>

### Expected result
<copied from the bug file, including the source: AC, CLAUDE.md rule, docs page, Swagger>

### Evidence
<copied from the bug file: statuses, short body excerpts, X-Correlation-Id values, UI text, SELECT results.
Keep it short. If the file's evidence is long, keep the lines that prove the problem.>

### Notes for developers
- **Likely layer:** <Frontend (Admin / Author Portal) | API | Database | Unclear>, because <one line from the evidence>
- **Look at first:** <route / endpoint / request shown in the evidence, e.g. `PUT /api/revisions/{id}` sent with the old `version`>
- **Correlation ids:** <the X-Correlation-Id values from the evidence, for the API logs; or "none recorded">
- **Related bugs:** <#ADO ids of related bugs from the same run, or "none">
- **How to verify the fix:** <re-run scenario <plan_id>: the expected result above, and that it still holds after <the edge case in the steps>>
- **Test data:** <run id and synthetic fixtures named in the bug file that can be reused, or "create fresh data">
- **Reviewer note:** <the human reviewer's note, minus the override lines; omit if empty>
```

### Rules for Notes for developers

- Every note comes from the bug file. If the evidence does not support a line, write `Unclear` (layer)
  or leave out the line (others). Never guess about code, classes or files you have not seen.
- **Likely layer**:
  - **Frontend**: the API answered correctly but the UI showed or sent the wrong thing (for example a
    stale value in the request body).
  - **API**: wrong status code, wrong ProblemDetails, wrong data in the response, or a rule not
    enforced on a direct call.
  - **Database**: the evidence includes a `SELECT` that shows wrong stored data while the response
    looked fine.
  - **Unclear**: anything else.
- Keep each note to one or two lines. The whole section stays under ~10 lines.

## System Info block

```markdown
- Found: <found_date> by qa-explorer, run <run id from Environment>
- Environment: <Environment line from the bug file: portal + URL, API URL, browser>
- Local evidence: `automation/archive/<PBI_ID>/bugs/<file name>` (screenshots/logs named there stay local)
- Accepted by human review; reported by qa-bug-reporter on <YYYY-MM-DD>
```

## Formatting fallback

If the server rejects `format: "Markdown"` for a field, send the same content as simple HTML (`<h3>`,
`<ol>`, `<ul>`, `<p>`, `<code>`, `<pre>`). The headings and order stay the same.
