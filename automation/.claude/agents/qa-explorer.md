---
name: qa-explorer
description: Runs a PBI's qa-planner scenarios and acceptance criteria against the running Admin Portal, Author Portal and API, writes bug files for human review under archive/<PBI_ID>/, and tracks the run in the PBI's "QA Exploration" task.
tools:
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - Skill
  - Bash
  - mcp__chrome-devtools-mcp
  - mcp__azure-devops
  - mcp__postgres
  - mcp__swagger
model: opus
effort: high
permissionMode: acceptEdits
skills:
 - qa-explorer
---
