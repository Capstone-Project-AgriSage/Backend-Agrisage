# AgriSage AI Agent Setup Guide

This repository kit is designed for teams using more than one coding agent.

## Why This Layout

The project uses a layered context strategy:

```text
Always loaded / very small
→ AGENTS.md

Always-on project rules
→ .agents/rules/
→ .claude/rules/ mirror

On-demand procedural knowledge
→ .agents/skills/
→ .claude/skills/ mirror
→ .codex/skills/ mirror

On-demand project knowledge
→ docs/agent/context/

Full authoritative specifications
→ docs/reference/
```

The goal is to avoid placing the entire database/architecture/business specification in every
agent session.

## Important Naming Correction

Use:

```text
AGENTS.md
.agents/rules/
.agents/skills/
```

not:

```text
AGENT.md
.agent/rules/
```

For Antigravity, Google's documented workspace hierarchy uses `.agents/` (plural).
Codex uses `AGENTS.md` (plural) as its repository instruction file.

## Tool Compatibility

### OpenAI Codex

Primary file:

```text
AGENTS.md
```

Codex automatically discovers `AGENTS.md` hierarchically.

Repo-scoped skill copies are provided under:

```text
.codex/skills/
```

### Claude Code

Adapter:

```text
CLAUDE.md
```

It imports the canonical root `AGENTS.md`.

Claude-specific auto-loaded rules:

```text
.claude/rules/
```

Claude skills:

```text
.claude/skills/<skill>/SKILL.md
```

### Gemini CLI

Adapter:

```text
GEMINI.md
```

It imports `AGENTS.md`.

Gemini CLI can also be configured to use `AGENTS.md` as a context filename, but this kit avoids
requiring per-developer configuration.

### Google Antigravity

Canonical always-on rules:

```text
.agents/rules/
```

Canonical on-demand skills:

```text
.agents/skills/
```

Antigravity uses `.agents` (plural).

## Canonical vs Mirrors

Canonical rule/skill sources are:

```text
.agents/rules/
.agents/skills/
```

Claude/Codex copies exist only for native discovery.

After editing canonical agent rules or skills on Windows, run:

```powershell
./scripts/sync-agent-assets.ps1
```

Commit the canonical files and the synchronized mirrors in the same PR.

## Context Strategy

Do not tell every agent:

> Read all architecture, coding rules, business rules, and the entire 67-table database file
> before every task.

That wastes context and often lowers focus.

Instead, `AGENTS.md` tells the agent what to load based on the task.

Examples:

```text
"Implement Product create endpoint"
→ AGENTS.md
→ BACKEND_MAP
→ relevant Product code
→ exact Product table section if persistence changes

"Configure InventoryLot"
→ AGENTS.md
→ DATABASE_MAP
→ exact inventory_lots section
→ inventory global rules
→ database-schema-change skill

"Review delivery fulfillment PR"
→ AGENTS.md
→ BUSINESS_RULES
→ WORKFLOW_MAP
→ relevant Delivery/Inventory/Debt code
→ review-backend-change skill
```

## What Belongs Where

### AGENTS.md

Keep only:
- repo map;
- stable non-obvious rules;
- source-of-truth precedence;
- key invariants;
- commands;
- pointers telling the agent where deeper information lives.

### Rules

Use for stable behavior that should apply broadly:
- architecture boundary;
- DB/migration safety;
- business invariants;
- test requirements;
- change-scope safety.

### Context

Use for project knowledge the agent may need sometimes:
- product scope;
- 67-table domain map;
- detailed business rules;
- workflow maps;
- current project phase;
- technical decisions.

### Skills

Use for repeatable procedures:
- implementing a backend feature;
- making/reviewing a schema change;
- reviewing a backend PR.

Do not create a skill merely because a topic has documentation.

### Reference Docs

Keep the full source-of-truth documents here.
Agent summaries must never silently override them.

## Team Workflow

For each Jira task:

1. Start the agent from repository root.
2. Give it the Jira Summary, Description and Acceptance Criteria.
3. Ask it to inspect before editing.
4. For substantial work, request a short plan first.
5. Let it load relevant context instead of attaching every document manually.
6. Review the diff.
7. Require build/test verification.
8. If the agent repeatedly makes the same project-specific mistake, propose a rule/context update.
9. Human-review every rule promotion; do not allow an agent to permanently rewrite governance
   based on one mistake.

## Prompt Template

```text
Implement Jira task: <KEY> <SUMMARY>

Acceptance criteria:
- ...
- ...

Work within the existing AgriSage architecture.
Inspect relevant code and load only the context required for this task.
Before editing, give a concise plan and identify any unresolved business/schema decision.
Then implement, run relevant verification, and summarize the diff.
```

## Maintenance

Review agent context periodically.

Remove:
- obsolete rules;
- decisions already obvious from code;
- duplicate instructions;
- task-specific history that no longer matters.

Promote a new permanent rule only when it is stable and repeatedly useful.

Update `CURRENT_STATE.md` when the team moves to a new implementation phase.

## Recommended Starting Point

At the current phase, use AI agents mainly for:
- solution/project scaffolding;
- Domain entity implementation;
- EF configuration;
- migration review;
- integration-test setup.

Avoid assigning one agent a vague task such as:

```text
"Build the whole backend."
```

Prefer Jira-sized changes with clear acceptance criteria and reviewable diffs.
