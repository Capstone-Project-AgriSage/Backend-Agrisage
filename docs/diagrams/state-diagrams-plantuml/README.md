# AgriSage PlantUML State Diagrams

- `puml/`: 30 independent PlantUML files; each can be copied directly into a PlantUML editor.
- `AGRISAGE_ALL_STATES.puml`: all 30 complete `@startuml` / `@enduml` blocks in one file.
- `AGRISAGE_ALL_STATES.md`: all code blocks, explanations and modelling notes.
- `manifest.json`: diagram index.
- `svg/`, when present: locally rendered previews.

Use one standalone file in editors supporting one diagram at a time. PlantUML CLI can render
the combined file as multiple diagrams. No external includes are required.

Rendering uses `hide empty description`, rounded states, thin borders, no shadows,
left-to-right layout and `skinparam linetype ortho` for right-angle connectors.
Exact placement and label routing remain layout-engine decisions.

Long-lived or reactivatable objects have no artificial final node. Every terminating
diagram uses one shared final pseudostate. Grouping containers are not persisted states.

Business references: `docs/reference/DATABASE_DESIGN.md` section 35 and the Domain entities.
The operational outbox/session conditions and representative administrative transitions
are distinguished from stored status enums in the full Markdown document.

Payment provider mapping: EXPIRED/FAILED -> FAILED; CANCELLED -> CANCELLED, as implemented
in `Application/Features/Payments/PayOsPaymentService.cs`.

Regenerate sources and package with `python build_diagrams.py`.
