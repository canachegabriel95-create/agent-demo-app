# Severity and priority

**Severity** describes the impact of the bug. **Priority** describes how soon it should be fixed.
The reporter sets both. The team can change them during triage.

## Which value wins

1. `Severity:` / `Priority:` line in the Reviewer note (`bug-file-format.md` §3)
2. `severity_suggested` from the bug file (`Low` / `Medium` / `High`)
3. Missing or unknown → `3 - Medium`, and say so in the report

The explorer never suggests `Critical`. Only a human reviewer sets it.

## Levels

| Severity (ADO value) | Meaning in this platform | Examples |
|---|---|---|
| `1 - Critical` | A security or data-integrity failure, or a core flow is unusable for everyone with no workaround | auth bypass; session works after logout or suspension; credentials, tokens or full invitation codes in responses or audit; data lost or corrupted; API or a portal down |
| `2 - High` | A rule that protects data or permissions is broken, or a main flow fails with only a hard workaround | another user's resource readable or writable (should be 404); an unsupported lifecycle transition succeeds; a race lets both actions succeed; an admin can be created through registration |
| `3 - Medium` | An acceptance criterion is not met, but data and permissions stay safe | wrong status code or ProblemDetails shape; missing 409 on stale `version`; a validation limit not applied; a false conflict that the user must work around; wrong item in a list or filter |
| `4 - Low` | Cosmetic or support issue with no functional impact | wording; missing `data-testid` or accessible name; missing loading/empty/error state; layout issue |

Explorer → ADO: `High` → `2 - High`, `Medium` → `3 - Medium`, `Low` → `4 - Low`.

## Priority (derived from severity)

| Severity | Priority | Meaning |
|---|---|---|
| `1 - Critical` | `1` | Fix before the story is accepted |
| `2 - High` | `1` | Fix before the story is accepted |
| `3 - Medium` | `2` | Fix in the current sprint |
| `4 - Low` | `3` | Fix when convenient |

Priority `4` is used only when the reviewer writes `Priority: 4`.

## Sanity check (do not change the value, only flag it)

If the evidence clearly fits another row of the table than the chosen severity, report it as chosen
and add one line to the final report, for example: *"BUG-03 is reported as Low, but the evidence shows
another author's article returned with 200 (that is High). Please check during triage."* The human
decides; the reporter never re-rates on its own.
