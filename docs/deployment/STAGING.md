# Staging deployment (BM04 commitment R2-07)

Status 2026-11-19: container images are ready and were run end to end on a local machine. Three choices are still
open for the lead: the host, the database and the runtime mode (section 4). Nothing is deployed yet.

## 1. What runs

| Part | Image (built from) | Port | Notes |
|---|---|---|---|
| API | `Dockerfile` in this repo (default target) | 8080 | `GET /health` answers `Healthy` without touching the database |
| Migrations | `Dockerfile`, `--target migrations` | — | EF bundle: applies pending migrations, then exits |
| Management web | `Dockerfile` in Management-Webs | 8080 | nginx; forwards `/api` (SignalR included) when `API_UPSTREAM` is set |
| Database | Supabase PostgreSQL (or any PostgreSQL 16) | — | not in a container on staging |

Configuration comes only from environment variables. `appsettings.Local.json` never enters an image (`.dockerignore`).
TLS ends at the host's proxy: the API image sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so the real scheme and
client address (used by the rate limits) come from `X-Forwarded-*`.

## 2. Environment variables of the API

Fill a copy of [`staging.env.example`](staging.env.example) in the host's secret settings, never in git.

| Variable | Required | Purpose |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` (or `Development`, see section 4) |
| `Database__Host`, `__Port`, `__Database`, `__Username`, `__Password`, `__SslMode` | yes | All six: `appsettings.Development.json` points to agrisage-dev |
| `Jwt__SigningKey` | yes | Long random value; changing it signs every user out |
| `Cors__AllowedOrigins__0` (`__1`...) | when the web calls the API directly | The web's public origin, e.g. `https://agrisage-web.example.org` |
| `Storage__Url`, `__SecretKey`, `__Bucket`, `__DeliveryProofBucket`, `__DiagnosisImageBucket` | for photos | Supabase Storage; without them uploads fail |
| `PayOS__ClientId`, `__ApiKey`, `__ChecksumKey`, `__ReturnUrl`, `__CancelUrl` | for online payment | Real payOS: there is no sandbox. Register the webhook `https://<api>/api/payments/payos/webhook` |
| `MessageDelivery__*` | for e-mail / SMS codes | Same keys as `appsettings.Local.example.json` |
| `AiService__BaseUrl`, `__ApiKey` | for photo diagnosis | The Python AI service must also be hosted, otherwise diagnosis fails |
| `Redis__Url` (or `REDIS_URL`) | no | Catalog cache; without it the API reads the database |
| `Logging__LogLevel__Microsoft.EntityFrameworkCore` | recommended | `Warning`: at `Information` every SQL command is logged |

Only for the one-off seed (section 3): `Seed__Store__Code`, `__Name`, `__AddressLine`, `__Province` (the store) and
`AdminBootstrap__Email`, `__Password`, `__FullName`, `__PhoneNumber` (the first Admin).

## 3. Steps

```bash
# Build (repo roots)
docker build -t agrisage-api .
docker build --target migrations -t agrisage-migrations .
docker build -t agrisage-management-web ../Management-Webs      # path of your Management-Webs checkout

# 1. Schema: only for a new staging database. agrisage-dev already has every migration.
docker run --rm agrisage-migrations --connection "Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require"

# 2. Reference data and the first Admin: only once, on a new database.
docker run --rm --env-file staging.env agrisage-api --seed --create-admin

# 3. Run
docker run -d -p 8080:8080 --env-file staging.env agrisage-api
docker run -d -p 8081:8080 -e API_UPSTREAM=http://<api-host>:8080 agrisage-management-web
```

On a platform that builds from GitHub, point the API service at this repo's `Dockerfile` with health check path
`/health`, and the web service at Management-Webs' `Dockerfile` with `API_UPSTREAM` = the API's public URL.

**Verified locally 2026-11-19** (PostgreSQL 16 container, `Production`, minimal variables):
- the bundle applied every migration;
- the seed added 5 roles, 9 units, 5 diseases and 1 store, and created the Admin;
- `/health` answered 200;
- in Edge, through the nginx proxy, the Admin logged in, `/api/realtime/negotiate` and the dashboard reports answered
  200, with no page error.

## 4. Decisions for the lead

1. **Runtime mode.** Simulated payOS, SMS and AI are allowed only in `Development`.
   - `Production` needs the real providers: real payOS money (small amounts), a real SMS or e-mail sender, and a hosted
     AI service.
   - `Development` allows the simulated modes, but publishes Swagger and `/api/dev/sms-outbox/view`, which shows every
     login code to anyone who opens it.
   - Allowing simulated modes in a `Staging` environment would change a rule in code; it is not done.
2. **Database.**
   - Reuse agrisage-dev: demo data is already there, nothing to migrate. But the background jobs (reminders, alerts,
     outboxes) then run in two places when a developer's API is also running.
   - A separate free Supabase project for staging: clean, but needs steps 1 and 2 and its own demo data.
3. **Host.** Prices and free tiers change often: check the official page before choosing.

| Host (checked 2026-11-19) | Cost | Fit | Watch out |
|---|---|---|---|
| Render | Free web service, or $7/month to stay awake | Builds the Dockerfiles from GitHub; health check path | Free service sleeps after 15 min idle, about 1 min to wake: bad for a live demo. Filesystem is ephemeral (photos are in Supabase, so fine) |
| Azure for Students | $100 credit a year, no card (student verification) | Container Apps (has an always-free allowance) or App Service | Free allowances are small; setup takes longer; renew every year |
| Railway | One-time $5 trial, then the $5/month Hobby plan | Simple Docker deploys, no sleep | Small budget; trial expires after 30 days |
| Koyeb | Unclear | — | Free plan changed in February 2026 (company acquired); not recommended until checked |

Sources:
- [Render: Deploy for Free](https://render.com/docs/free)
- [Render: Platforms with a real free tier in 2026](https://render.com/articles/platforms-with-a-real-free-tier-for-developers-in-2026)
- [Azure for Students Starter](https://learn.microsoft.com/en-us/azure/education-hub/about-azure-for-students-starter)
- [Azure for Students offer page](https://azureforeducation.microsoft.com/Student)
- [Railway plans](https://docs.railway.com/pricing/plans)
- [Koyeb pricing FAQ](https://www.koyeb.com/docs/faqs/pricing)

**Suggestion:** Render, with the API on the $7 plan so it never sleeps during a demo and the web on the free plan, and
a separate Supabase project. Alternative: Azure for Students if the team prefers no cost and accepts a longer setup.
