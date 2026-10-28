# Test case template (Azure DevOps)

One Test Case work item per accepted proposed block. Keep it simple: a tester who has never seen the
PBI must be able to run it from the title, the preconditions and the steps alone, and tick each step
as passed or failed.

## Fields

| Field | Value |
|---|---|
| Title (`title` on create) | A short, readable title (see **Title**). |
| Steps (`steps` on create) | Numbered `Action|Expected result` lines (see **Steps**). |
| Tests link (`testsWorkItemId` on create) | The PBI id. The PBI then lists the Test Case under "Tested By". |
| Area / Iteration (`areaPath`, `iterationPath`) | The PBI's, copied exactly. |
| Priority (`priority`) | `regression yes` → `2`; otherwise `3`. Never `1` (reserved for humans). |
| `System.Description` (update, Markdown) | The Description block below. |
Do not set Assigned To, State or Tags. The Description's `**Covers:** PBI #<PBI_ID> · scenario <PLAN_ID>`
line is the key for the duplicate check: always write it exactly like this.

## Title

Someone scanning the "Tested By" list should know what is checked from the title alone.

- Shape: `<Portal>: <action> <result>` — for example `Author: withdraw a pending revision makes it editable again`.
- **Portal** = `Author` or `Admin` (from `Portal:`), or `Admin + Author` when both are used. Leave it
  out if the block has no portal.
- Rewrite the explorer's "As an … I want to check that …" into the action and its result. Present
  tense, sentence case, max **80 characters**, one behaviour per title.
- No backticks, routes, ids, status codes, run ids, bug ids, or "(currently fails)".

| Explorer title | Test Case title |
|---|---|
| As an author I want to check that revising a published article opens a pre-filled draft revision while the article stays published | Author: revise a published article opens a pre-filled draft |
| As an administrator I want to check that rejecting a revision needs a 10-character reason that the author then sees | Admin: reject a revision needs a reason the author then sees |

## Steps

Each step is one line, `<n>. <Action>|<Expected result>`, joined with a newline:

```
1. Open the article page of the published article|The article page opens with status "Published"
2. Click "Revise this article"|The revision editor opens
```

Rules:
- **One action per step**, starting with a verb: Open, Click, Enter, Select, Confirm, Sign in as, Reload.
- **Every step has an expected result.** Split the block's `Expected:` over the steps it belongs to
  (numbered `1: … 2: …` expected text maps directly). A pure navigation step gets a short visible result
  ("The review queue opens"). Never leave the expected part empty and never repeat the same expected on
  every step.
- One check per expected result where possible; at most three short checks joined with `;`.
- Name buttons, links and messages exactly as the block quotes them, in double quotes:
  `Click "Submit for review"`, `The message "Revision withdrawn. It is a draft again." is shown`.
- Routes (`/revisions/:id`) become page names ("the revision editor", "the review queue", "the feed").
  Add the route in brackets only when no page name exists.
- Role switches are their own step: `Sign in to the Author Portal as the article's author|The dashboard opens`.
- Plain text only: no Markdown (`**`, backticks), no HTML tags, no `|` inside an action or expected
  result (it is the delimiter), no line breaks inside a step. Markup the test types as text, such as
  `<b>bold?</b>`, is written in words: `a body containing the text <b>bold?</b>` is allowed only if the
  create call accepts it; if it is rejected, write `a body containing an HTML bold tag typed as text`.
- No test data values the tester cannot reproduce (UUIDs, run ids, generated emails). Lengths and counts
  are fine: "Enter a title of 4 characters".
- Aim for 3–8 steps. More than 10 → the block probably covers two behaviours; write it as-is and
  mention it in the report.

Expected results that the block marks as failing are written as the **correct** behaviour (the bug
goes in the Description). Expected results the block marks as open are left out.

## Description (Markdown)

```markdown
**Preconditions**
- <from Preconditions, one bullet per condition, in role/state words>

**Covers:** PBI #<PBI_ID> · scenario <PLAN_ID> · <source column of the scenario row, e.g. AC1, AC2>
**Portal:** <Admin | Author | Admin + Author>
**Known bug:** #<ado_bug> (<one-line summary>)   ← only if the block names a bug
**Not covered:** <expected part left out because it is open, with the bug id>   ← only if any

_Proposed by qa-explorer, accepted by a reviewer, written by qa-test-case-writer on <YYYY-MM-DD>._
```

## Worked example (FE4 of PBI 292)

Block:

```
Title: As an author I want to check that withdrawing a pending revision makes it an editable draft again
Portal: Author
Preconditions: PendingReview revision
Steps: 1. Open `/revisions/:id`. 2. Click **Withdraw submission**, confirm **Withdraw**. 3. Open the article in `/feed`.
Expected: "Revision withdrawn. It is a draft again."; fields editable with Save / Submit / Discard; feed still shows the published version
Flags (from plan): automate yes · regression yes
```

Test Case:

- Title: `Author: withdraw a pending revision makes it editable again`
- Priority `2`; no tags
- Steps:

```
1. Sign in to the Author Portal as the article's author|The dashboard opens
2. Open the revision editor of the pending revision|Status "Revision pending review" is shown; fields are read-only
3. Click "Withdraw submission"|A confirmation dialog opens
4. Click "Withdraw"|The message "Revision withdrawn. It is a draft again." is shown
5. Check the revision editor|Fields are editable; "Save", "Submit" and "Discard" are offered
6. Open the article in the feed|The feed still shows the published version, not the revision
```

- Description:

```markdown
**Preconditions**
- An author owns a Published article
- The article has a revision in status Pending review

**Covers:** PBI #292 · scenario FE4 · AC3
**Portal:** Author

_Proposed by qa-explorer, accepted by a reviewer, written by qa-test-case-writer on 2026-10-28._
```

Step 2's expected result ("read-only") comes from the scenario rows of the same PBI (FE3: "read-only,
only Withdraw submission offered"); it is allowed because it restates a checked behaviour, not a new one.
If no row supports it, write only `The revision editor opens`.

## "Simple" checklist (step 4 of the skill)

- [ ] Title ≤ 80 characters, starts with the portal, no ids or code formatting.
- [ ] Every step: one action, one verb, an expected result.
- [ ] No `|`, Markdown or line breaks inside a step.
- [ ] No UUIDs, emails, run ids, tokens, invitation codes.
- [ ] Every expected result traces to the block or its scenario row.
- [ ] Failing / open parts handled as in **Steps** and **Description**.
