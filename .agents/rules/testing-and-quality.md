# Testing and Quality Rules

- Every changed code path must have an appropriate verification strategy.
- Unit tests target Domain calculations, invariants, and state transitions.
- Integration tests target EF mappings, PostgreSQL constraints, transactions, concurrency,
  webhook idempotency, and cross-module persistence flows.
- High-risk modules: Inventory, Credit, Payment, Delivery, Debt, Returns.
- Propagate `CancellationToken` through async I/O.
- Use async EF/HTTP/storage methods; do not use `.Result` or `.Wait()`.
- Money uses `decimal`, never float/double.
- Use structured logging and do not log secrets.
- Use centralized exception handling; controllers do not catch generic business exceptions.
- List endpoints are paginated.
- Review changed files before finishing.
- Run `dotnet build` and relevant `dotnet test` scopes when implementation exists.
- If a test is impractical or environment-dependent, say so explicitly rather than skipping silently.
