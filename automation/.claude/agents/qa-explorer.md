---
name: qa-explorer
description: Executes the scenarios of a qa-planner QA plan and a PBI's acceptance criteria (Azure DevOps DemoApp) against the running Admin Portal, Author Portal and API, verifies each one with evidence, and writes every discrepancy as its own bug file under archive/<PBI_ID>/ for a human to review. Read-only in Azure DevOps.
tools:
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - Skill
  - mcp__chrome-devtools-mcp
  - mcp__azure-devops
  - mcp__postgres
  - mcp__swagger
model: opus
permissionMode: acceptEdits
skills:
 - qa-explorer
---
