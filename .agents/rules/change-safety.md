# Change Safety and Team Collaboration

- Work from the current Jira/user acceptance criteria.
- Do not edit unrelated files to make code 'cleaner' unless requested.
- Do not modify migrations already applied/shared without explicit team decision.
- Do not force-push, rewrite shared Git history, or commit secrets.
- Before broad refactors, first identify call sites and affected features.
- Preserve public API contracts unless the task explicitly changes them.
- Preserve database history and auditability.
- Do not silently change a frozen business rule; surface the requested rule change first.
- If an implementation requires a schema/business decision absent from the source docs,
  stop at the decision boundary and ask or document the assumption for approval.
- When learning a repeated project-specific correction, propose updating the relevant rule/context
  rather than bloating `AGENTS.md`.
