# AgriSage Backend

ASP.NET Core (.NET 10) backend for AgriSage. Architecture and rules: `AGENTS.md`, `docs/reference/`.

## Prerequisites

- .NET 10 SDK (pinned in `global.json`)

## Structure

```text
AgriSage.sln
src/
├── AgriSage.Api             → HTTP boundary (Program.cs, auth, Swagger)
├── AgriSage.Application     → use cases, DTOs, validators, abstractions
├── AgriSage.Domain          → entities, invariants
└── AgriSage.Infrastructure  → EF Core/PostgreSQL, JWT, providers
tests/
├── AgriSage.UnitTests
└── AgriSage.IntegrationTests
```

NuGet versions are managed centrally in `Directory.Packages.props`; `PackageReference` items in `.csproj` files have no `Version`.

## Configuration & secrets

`appsettings*.json` contain only non-secret values. Never commit secrets
(`CommittedConfigurationTests` fails if a password, connection string or signing key is committed).

| Key | Required | Development (User Secrets) | Other environments (env var) |
|---|---|---|---|
| `Jwt:SigningKey` (≥ 32 chars) | Yes — app fails on start without it | `dotnet user-secrets set "Jwt:SigningKey" "<key>" --project src/AgriSage.Api` | `Jwt__SigningKey` |
| `Database:Password` | Yes, to reach the database | `dotnet user-secrets set "Database:Password" "<password>" --project src/AgriSage.Api` | `Database__Password` |

### Database (Supabase PostgreSQL)

The non-secret connection settings are committed per environment in the `Database` section
(`src/AgriSage.Api/appsettings.Development.json` → Supabase project `agrisage-dev`, Singapore):

```json
"Database": {
  "Host": "aws-0-ap-southeast-1.pooler.supabase.com",
  "Port": 5432,
  "Database": "postgres",
  "Username": "postgres.<project-ref>",
  "SslMode": "Require"
}
```

Infrastructure builds the Npgsql connection string from this section plus the secret `Database:Password`
(`DatabaseOptions`). A developer only runs, once:

```bash
dotnet user-secrets set "Database:Password" "<password>" --project src/AgriSage.Api
```

Other environments override any key with environment variables (`Database__Host`, `Database__Password`, …).

The committed default is the Supabase **Session Pooler** (port 5432, IPv4-compatible; the username is
`postgres.<project-ref>`). The **Direct connection** host (`db.<project-ref>.supabase.co`) is IPv6-only; a
developer on an IPv6 network may override `Database:Host` / `Database:Username` in their own User Secrets —
do not commit personal overrides.

### EF Core migrations

The EF CLI is pinned as a local tool (`dotnet-tools.json` at the repository root, same version as EF Core):

```bash
dotnet tool restore
dotnet ef migrations list --project src/AgriSage.Infrastructure --startup-project src/AgriSage.Api
```

Apply pending migrations to the configured database (explicit, reviewed step — never on app startup):

```bash
dotnet ef database update --project src/AgriSage.Infrastructure --startup-project src/AgriSage.Api
```

Tests against the real development database are opt-in and always roll back their transaction:

```bash
# PowerShell: $env:AGRISAGE_DB_TESTS = "1"      bash: export AGRISAGE_DB_TESTS=1
dotnet test tests/AgriSage.IntegrationTests --filter "FullyQualifiedName~RealDatabaseTests"
```

Migrations live in `src/AgriSage.Infrastructure/Persistence/Migrations` and are the schema source of truth.
Review every generated migration before applying it (coding rules #53–#54); applying to Supabase is a
separate, explicitly approved step.

### Reference seed (explicit command)

`--seed` creates only 5 roles, 9 units, 5 rice disease classes and one configured store, then exits
without starting HTTP. It does not run on ordinary application startup or apply any migration.
The command uses the existing `Database` settings and `Database:Password` secret; it prints added
row counts or safe error messages, never credentials or a connection string. Exit code is 0 on
success and 1 on failure/cancellation.

`Seed:Store` in `appsettings.Development.json` currently contains team-approved **temporary dev data**:
`AGRISAGE-DEV`, `AgriSage Dev Store`, `Dev address - to be replaced`, `Can Tho`; optional fields are null.
Replace these values with approved operational information before production. Missing fields,
`<ĐIỀN>` placeholders and values exceeding database column lengths are rejected before connecting.

**Running this command against `agrisage-dev` is still pending separate explicit approval.** After
approval, from the repository root with the Development secrets configured:

```bash
dotnet run --project src/AgriSage.Api --launch-profile https -- --seed
```

All four groups share one transaction and one `SaveChangesAsync`. Existing natural codes are
checked including soft-deleted rows: existing live records are preserved; a matching soft-deleted
record causes an error and rollback, never automatic restoration. A different ACTIVE store also
causes an error. A second successful invocation reports zero added rows. Changing Store configuration
does not update an existing store.

Offline tests keep `AGRISAGE_DB_TESTS` unset or set to `0`. To run the seed tests on PostgreSQL
after approval for rollback-only tests:

```powershell
$env:AGRISAGE_DB_TESTS = "1"
dotnet test tests/AgriSage.IntegrationTests --filter "FullyQualifiedName~ReferenceSeedDatabaseTests"
Remove-Item Env:AGRISAGE_DB_TESTS
```

These tests reuse `RealDb.Session` and its outer transaction; the seeder creates a savepoint instead
of a nested transaction and never commits that outer transaction. They are serialized against other
test collections because reference codes are fixed. Every session rolls back, including a test that
injects a failure after `SaveChangesAsync` to prove all four groups are reverted. On an empty database
the tests use rollback-only Store fixtures; after reference seed they reuse the existing Store inside
the rolled-back session, so they do not introduce a second operational store.

## Run

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/AgriSage.Api --launch-profile https   # Swagger: /swagger (Development only)
```
