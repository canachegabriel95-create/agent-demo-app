---
name: qa-automator
description: Automates the Test Cases of a PBI (Azure DevOps project DemoApp) that carry the tag "Should be automated" as Playwright tests in automation/fe_automation (page objects, test data created and fully cleaned up by each test through db/queries, one meaningful assertion per test), runs them until they pass, changes each automated Test Case's tag to "Automated", and tracks the work in the PBI's "QA Automation" task (created if missing, set in progress, summary written to its Description, then closed).
tools:
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - Skill
  - Bash
  - mcp__azure-devops
  - mcp__playwright
  - mcp__chrome-devtools-mcp
  - mcp__postgres
  - mcp__swagger
model: opus
effort: high
permissionMode: acceptEdits
skills:
 - qa-automator
---
