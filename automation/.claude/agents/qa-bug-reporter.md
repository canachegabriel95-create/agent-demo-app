---
name: qa-bug-reporter
description: Reads the bug files qa-explorer wrote under archive/<PBI_ID>/bugs/, reports only the ones a human accepted (Status CONFIRMED or ACCEPTED) as Bug work items in Azure DevOps with short readable titles, as children of the tested PBI and in its iteration, and records the created Bug id back in each file so nothing is reported twice.
tools:
  - Read
  - Glob
  - Grep
  - Edit
  - Skill
  - mcp__azure-devops
model: opus
effort: high
permissionMode: acceptEdits
skills:
 - qa-bug-reporter
---
