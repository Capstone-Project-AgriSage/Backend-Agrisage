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

## Run

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/AgriSage.Api --launch-profile https   # Swagger: /swagger (Development only)
```
