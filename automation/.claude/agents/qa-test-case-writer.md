---
name: qa-test-case-writer
description: Reads the "Proposed test case" blocks qa-explorer wrote in archive/<PBI_ID>/TEST_SCENARIOS.md, writes only the ones a human accepted (Status CONFIRMED or ACCEPTED) as simple Test Case work items in Azure DevOps (short title, numbered Action / Expected steps), linked to the tested PBI with a Tests / Tested By link and in its iteration, and records the created Test Case id back in the block so nothing is written twice.
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
 - qa-test-case-writer
---
