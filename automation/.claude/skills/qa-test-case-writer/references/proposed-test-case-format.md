# Proposed test case format (input)

`qa-explorer` adds one block per FE scenario worth keeping to
`automation/archive/<PBI_ID>/TEST_SCENARIOS.md`. Its template is
`../../qa-explorer/references/output-templates.md` → `TEST_SCENARIOS.md`. This page is what the writer
needs from it.

## 1. Structure

```markdown
### Proposed test case — FE1
Title: As an <administrator|author> I want to check that <behaviour>
Portal: Admin / Author
Preconditions: <fixture state>
Steps: 1. <action>. 2. <action>. 3. <action>.
Expected: <observable result, sometimes numbered "1: … 2: …" to match the steps>
Flags (from plan): automate <yes|no> · regression <yes|no>
Status: PENDING
<!-- Change to CONFIRMED to have it created in Azure DevOps, or REJECTED to drop it. -->
```

- A block starts at a `### Proposed test case — <PLAN_ID>` heading and ends at the next `###` / `##`
  heading or the end of the file. The plan id (`FE1`, `AH3`) is the key; never renumber it.
- Each field is one line starting with its label. `Steps:` is a numbered outline on one line
  (`1. … 2. …`); split it on the numbers.
- Required for writing: `Title`, `Steps`, `Expected`. Missing or empty → **Invalid**.
  `Portal`, `Preconditions` and `Flags` are optional (defaults: no portal prefix, no preconditions,
  `regression no`, `automate no`).
- The matching row in `## Scenario Results` (same Plan ID) may be read for the full wording of a button
  or message the block shortens. It never adds checks.

## 2. Reading the decision

- Take the **first** line in the block that starts with `Status:`. Ignore the HTML comment.
- Compare case-insensitively after trimming: `CONFIRMED` and `ACCEPTED` mean accepted. `PENDING`,
  `REJECTED`, anything else, or no `Status:` line → **Skipped**.
- Two `Status:` lines with different values, or a value like `CONFIRMED?` → **Skipped**, and say the
  decision is ambiguous.
- The human may have edited Title, Steps or Expected before accepting: use the block as it is now.

## 3. Markers inside Expected

| Marker in the block | Meaning | What to do |
|---|---|---|
| `(currently fails, BUG-NN)` | the scenario failed; the expected text is still the correct behaviour | keep the expected text, drop the marker, name the bug in the Description |
| `(… open, see BUG-NN)` / "whether … is open" | the expected behaviour is not decided | leave that check out of the steps, name it in the Description and in the report |
| `(see BUG-NN)` in Steps | same as above | same as above |

For a named bug, read `bugs/BUG-NN-*.md`: if its front matter has `ado_bug`, use that id
("Known bug: #<id>"); otherwise name the local file id only ("Known bug: BUG-NN, not yet reported").

## 4. Write-back after writing

After a Test Case is created (or found by the duplicate check), add exactly one line right after the
block's `Status:` line and change nothing else:

```markdown
Created: Azure DevOps Test Case <id>, tests PBI <PBI_ID> (qa-test-case-writer, <YYYY-MM-DD>)
```

If the Tests link failed, write `…, link to PBI <PBI_ID> FAILED (qa-test-case-writer, <date>)` so the next
run retries only the link. A block with a `Created:` line is never created again.
