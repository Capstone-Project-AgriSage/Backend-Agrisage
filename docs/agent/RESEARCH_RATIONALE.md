# Research Notes — Why This Agent Layout

This starter kit follows a few recurring principles from official coding-agent documentation
and experienced community workflows.

## Adopted Principles

1. Keep the always-loaded root instruction file compact and high-signal.
2. Treat the root agent file as a map/table of contents, not the full knowledge base.
3. Load detailed project knowledge only when the task needs it.
4. Separate stable rules from detailed context and from repeatable procedures.
5. Use skills for repeated workflows, not for every piece of documentation.
6. Give agents deterministic verification paths: build, test, migration review.
7. Use native adapter files for each harness rather than maintaining separate contradictory
   project descriptions.
8. Keep one canonical source for shared rules/skills and synchronize tool-specific mirrors.
9. Human-review permanent rule changes so one bad session does not pollute project governance.
10. Scope agent tasks like Jira/GitHub issues rather than asking for an entire system at once.

## Provider Mapping

```text
Codex       → AGENTS.md
Claude Code → CLAUDE.md + .claude/rules + .claude/skills
Gemini CLI  → GEMINI.md
Antigravity → .agents/rules + .agents/skills
```

## Why Context Files Exist

AgriSage has:
- 67 database tables;
- complex inventory/credit/debt interactions;
- frozen business rules;
- three large authoritative backend documents.

Loading all of that into every conversation would be wasteful.
The context files are curated navigation summaries; exact implementation details remain in
`docs/reference/`.

## Why AGENTS.md Is Not the Full Coding Rules File

Large always-loaded instructions consume context on every task, including simple edits.
`AGENTS.md` therefore includes only critical boundaries and tells the agent when to open deeper docs.

## Why Skills Are Limited

The first three skills represent workflows the team is expected to repeat frequently:
- implement feature;
- schema change;
- backend review.

More skills should be added only after a workflow repeats enough that the team can define
a stable, testable procedure.
