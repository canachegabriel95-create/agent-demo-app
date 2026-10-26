# Local Content Review Platform

Two React portals (Admin and Author) sharing one ASP.NET Core Web API and a PostgreSQL database. Runs entirely locally. See [CLAUDE.md](CLAUDE.md) and [AGENTS.md](AGENTS.md) for conventions.

## Local endpoints

| Component | URL |
|---|---|
| API | http://localhost:5080 (routes under `/api`) |
| Swagger UI | http://localhost:5080/swagger |
| Health | http://localhost:5080/health |
| Admin Portal | http://localhost:5173 |
| Author Portal | http://localhost:5174 |
| PostgreSQL | localhost:5432 |
| pgAdmin | http://localhost:5050 |

> The API, portals, Swagger and health endpoint are added by later Feature 01 stories. This section documents the database tooling available so far.

## Database (PostgreSQL + pgAdmin)

Prerequisite: Docker with Docker Compose v2.

First-time setup:

```bash
cp .env.example .env
# edit .env and replace the placeholder passwords
```

`.env` is git-ignored. Never commit it or any real credential.

| Action | Command |
|---|---|
| Start PostgreSQL and pgAdmin | `docker compose up -d` |
| Check status (postgres should be `healthy`) | `docker compose ps` |
| Stop (data is kept in the `pgdata` volume) | `docker compose down` |
| Stop **and delete all data** | `docker compose down -v` |

### pgAdmin

Open http://localhost:5050 and sign in with `PGADMIN_DEFAULT_EMAIL` / `PGADMIN_DEFAULT_PASSWORD` from `.env`. The server **ContentReview Local** (host `postgres`, port 5432, database `content_review`) is pre-registered from `docker/pgadmin/servers.json`; enter `POSTGRES_PASSWORD` from `.env` when asked to connect.

If you change `POSTGRES_USER` in `.env`, update `Username` in `docker/pgadmin/servers.json` to match.

### API connection string

The API reads the connection string from the `ConnectionStrings__Default` environment variable (see `.env.example`). It is never stored in `appsettings*.json`.

```bash
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=content_review;Username=content_review;Password=<your POSTGRES_PASSWORD>"
```

### Troubleshooting

- **`Set POSTGRES_PASSWORD in .env`**: you have not created `.env` yet (`cp .env.example .env`).
- **Port 5432 or 5050 already in use**: change `POSTGRES_PORT` / `PGADMIN_PORT` in `.env`.
- **Password change has no effect**: the database is initialized only once. Run `docker compose down -v` to recreate it (this deletes all data).

## API

Prerequisites: .NET 10 SDK and the database running (see above).

```bash
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=content_review;Username=content_review;Password=<your POSTGRES_PASSWORD>"
dotnet run --project backend/src/ContentReview.Api
```

The API listens on http://localhost:5080. The connection string is required; the API refuses to start without it.

| Endpoint | Description |
|---|---|
| `GET /health` | Anonymous. `200` when healthy, `503` when the database is unreachable. Body: `{ "status": "Healthy", "checks": [{ "name": "api", "status": "Healthy" }, { "name": "database", "status": "Healthy" }] }`. No error details are exposed. |

Settings (appsettings or environment variables; see `.env.example`):

| Setting | Default | Purpose |
|---|---|---|
| `ConnectionStrings__Default` | none (required) | PostgreSQL connection string |
| `Database__MigrateOnStartup` | `true` in Development, otherwise `false` | Apply EF Core migrations at startup |
| `Cors__AllowedOrigins` | `http://localhost:5173`, `http://localhost:5174` | Origins allowed to call the API |
| `Auth__SessionLifetimeHours` | `8` | Session token lifetime |

Tests: `dotnet test backend/ContentReview.sln`. Integration tests start their own PostgreSQL through Testcontainers, so Docker must be running.
