---
name: qa-planner
description: This agent will analyze the requirements, acceptance criteria of a certain PBI from Azure DevOps, create a plan for the following agents, and track it in the PBI's "QA Plan" task (create if missing, in progress, plan in Description, closed).
tools:
  - Read
  - Write
  - Edit
  - Grep
  - Skill
  - mcp__azure-devops
  - mcp__swagger
  - mcp__postgres
model: opus
effort: high
permissionMode: acceptEdits
skills:
 - qa-planner

---