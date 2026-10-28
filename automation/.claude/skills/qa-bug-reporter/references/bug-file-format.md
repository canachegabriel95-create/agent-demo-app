# Bug file format (input)

`qa-explorer` writes one file per bug: `automation/archive/<PBI_ID>/bugs/BUG-<NN>-<slug>.md`. Its
template is `../../qa-explorer/references/output-templates.md` → `bugs/BUG-<NN>-<slug>.md`. This page
is what the reporter needs from it.

## 1. Structure

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
## Steps to reproduce
## Actual result
## Expected result
## Evidence
## Environment

---

## Reviewer Decision
Status: PENDING | CONFIRMED | REJECTED
Reviewer note: <free text>
```

Required for reporting: `id`, the `# BUG-NN:` summary line, **Steps to reproduce**, **Actual result**,
**Expected result**. Missing or empty → the file is **Invalid** (not reported). `Scenario`, `Evidence`
and `Environment` are optional; if empty, leave them out of the Bug.

## 2. Reading the decision

- Take the **first** line under `## Reviewer Decision` that starts with `Status:`. Ignore the HTML
  comment and anything in the body above the decision block.
- Compare the value case-insensitively after trimming: `CONFIRMED` and `ACCEPTED` mean accepted.
  `PENDING`, `REJECTED`, anything else, or no `Status:` line → **Skipped**.
- Two `Status:` lines with different values, or a value like `CONFIRMED?` → **Skipped**, and say the
  decision is ambiguous.

## 3. Overrides in the Reviewer note

The reviewer may put these on their own lines in `Reviewer note:` (or the lines after it). They win
over the explorer's suggestion:

| Line | Effect |
|---|---|
| `Severity: Critical \| High \| Medium \| Low` | replaces `severity_suggested` |
| `Priority: 1 \| 2 \| 3 \| 4` | replaces the priority derived from severity |
| `Title: <text>` | replaces the summary used in the Bug title |

All other note text goes into the Bug under **Notes for developers → Reviewer note**.

## 4. Write-back after reporting

After a Bug is created (or found by the duplicate check), edit the file in exactly two places and
change nothing else:

Front matter — add two lines before the closing `---`:

```yaml
ado_bug: <Bug id>
reported_date: <YYYY-MM-DD>
```

Reviewer Decision — add one line right after the `Status:` line:

```markdown
Reported: Azure DevOps Bug <id>, child of PBI <PBI_ID> (qa-bug-reporter, <YYYY-MM-DD>)
```

If the PBI link failed, write `…, link to PBI <PBI_ID> FAILED` so the next run retries only the link.
A file with `ado_bug` is never created again. On a later run, read that Bug once (`get`) to confirm
it exists. If it was deleted, report this to the human and do not recreate it on your own.
