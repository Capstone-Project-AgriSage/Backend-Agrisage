# Backend Architecture Rules

Applies to backend source and tests.

- Preserve the four projects: Api, Application, Domain, Infrastructure.
- Domain references no other project.
- Application references Domain only.
- Infrastructure references Application + Domain.
- Api references Application and Infrastructure for composition/DI.
- Controller = HTTP boundary only.
- Application Service = use-case orchestration and transaction ownership.
- Domain = entities, invariants, state transitions, pure business behavior.
- Infrastructure = persistence/provider implementations, not business-policy ownership.
- Organize each layer by business feature.
- DTOs belong to Application contracts; Domain must not depend on DTOs.
- Never return Domain entities directly from controllers.
- External providers must be behind Application abstractions.
- Do not create a generic repository that only wraps EF Core CRUD.
- Cross-feature operations must have one clear use-case/transaction owner.
- Avoid God Services; split only when cohesion clearly improves.
