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

### Database migrations

The schema is created by EF Core migrations (8 tables; see [docs/database.md](docs/database.md)). With the database running and `ConnectionStrings__Default` set (see below):

```bash
dotnet tool restore
dotnet ef database update -p backend/src/ContentReview.Infrastructure -s backend/src/ContentReview.Api
```

The API also applies migrations at startup when `Database__MigrateOnStartup=true` (the default in Development).

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

## Frontend (Admin Portal and Author Portal)

Two separate React 19 + TypeScript (strict) + Vite apps that call the same API. Prerequisite: Node.js 22 LTS.

| App | Folder | URL | Session storage key |
|---|---|---|---|
| Admin Portal | `frontend/admin-portal` | http://localhost:5173 | `cr.admin.session` |
| Author Portal | `frontend/author-portal` | http://localhost:5174 | `cr.author.session` |

The ports are fixed (`strictPort`): if a port is taken, the dev server fails instead of picking another one, because the API only allows these two origins (CORS).

```bash
cd frontend/admin-portal      # or frontend/author-portal
npm ci
npm run dev                   # http://localhost:5173 (admin) / http://localhost:5174 (author)
```

The home page of each portal shows a temporary **API status** panel that calls `GET /health`, so you can see at a glance whether the API and its database are reachable. Start the database and the API first (see above).

| Script | What it does |
|---|---|
| `npm run dev` | Dev server with hot reload on the fixed port |
| `npm run build` | Type-check and production build into `dist/` |
| `npm run preview` | Serve the production build on the fixed port |
| `npm run lint` | ESLint (typescript-eslint, react-hooks, jsx-a11y) and a Prettier check |
| `npm run format` | Format the code with Prettier |
| `npm run test -- --run` | Unit and component tests (Vitest, jsdom, React Testing Library) |

Configuration: copy `.env.example` to `.env.local` to change `VITE_API_BASE_URL` (default `http://localhost:5080`). Nothing secret belongs in these files: everything prefixed `VITE_` is shipped to the browser.

Conventions shared by both apps (`src/`):

- `api/client.ts`: `apiFetch<T>(path, options)` prefixes `VITE_API_BASE_URL`, sends and receives JSON, attaches the bearer token from `sessionStorage` and throws a typed `ApiError { status, title, detail, errors, correlationId }` parsed from the API's ProblemDetails. A `401` clears the session and redirects to `/login`.
- `components/`: `LoadingState`, `EmptyState`, `ErrorState` (shows the correlation id), `Pagination`, `FormField` (label, hint and error linked with `aria-describedby`). Use them for every data view so loading, empty and error states look the same.
- `app/router.tsx`: layout shell (header, main navigation, main region, skip link), `/login` placeholder and a not-found page.
- Styling is plain CSS modules. User content is always rendered as text; never use `dangerouslySetInnerHTML`.
