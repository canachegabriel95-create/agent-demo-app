# Local Content Review Platform

Two React portals (Admin and Author) sharing one ASP.NET Core Web API and a PostgreSQL database. Runs entirely locally. See [CLAUDE.md](CLAUDE.md) and [AGENTS.md](AGENTS.md) for conventions.

## Run locally

Everything runs on your machine: PostgreSQL and pgAdmin in Docker, the API with `dotnet`, and the two portals with Node. No cloud or paid service is needed.

### Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Docker | Docker Desktop or Docker Engine with **Compose v2** | Must be running |
| .NET SDK | **10** | `dotnet --version` |
| Node.js | **22 LTS** (with npm) | `node --version` |
| Shell | macOS / Linux: Bash and `curl`. Windows: PowerShell 5.1 or 7 | Windows users can also use WSL with the Bash scripts |

### First-time setup

From the repository root:

```bash
cp .env.example .env                  # Windows PowerShell: Copy-Item .env.example .env
# open .env and replace the placeholder passwords (it is git-ignored; never commit it)

(cd frontend/admin-portal && npm ci)
(cd frontend/author-portal && npm ci)
```

To create the first administrator (needed to sign in to the Admin Portal), set `ADMIN_EMAIL` and `ADMIN_NAME` in `.env`, start the stack and run `scripts/provision-admin.sh` (or `scripts/provision-admin.ps1`). It asks for the password in a hidden prompt. Step-by-step guide: [docs/admin-provisioning.md](docs/admin-provisioning.md).

### Start and stop

| | macOS / Linux | Windows (PowerShell) |
|---|---|---|
| Start | `./scripts/start.sh` | `.\scripts\start.ps1` |
| Stop | `./scripts/stop.sh` | `.\scripts\stop.ps1` |

If PowerShell refuses to run the scripts, use `powershell -ExecutionPolicy Bypass -File .\scripts\start.ps1`.

`start` checks the prerequisites and tells you everything that is missing before it does anything. Then it:

1. starts PostgreSQL and pgAdmin (`docker compose up -d`) and waits until PostgreSQL is healthy;
2. builds and starts the API, which applies the database migrations on startup, and waits until `/health` is `Healthy`;
3. starts the Admin Portal and the Author Portal and waits until they answer;
4. prints the URLs below.

The API and the portals run in the background; their output goes to `.run/*.log` (git-ignored). Running `start` again while everything is up is safe.

`stop` stops the API and the portals (including their child processes), then stops the containers with `docker compose stop`, so **your database data is kept**. It finishes by checking that ports 5080, 5173 and 5174 are free. To delete the data as well, run `docker compose down -v`.

### Local endpoints

| Component | URL |
|---|---|
| Admin Portal | http://localhost:5173 |
| Author Portal | http://localhost:5174 |
| API | http://localhost:5080 (routes under `/api`) |
| Health | http://localhost:5080/health |
| Swagger UI | http://localhost:5080/swagger (added by a later story) |
| PostgreSQL | localhost:5432 |
| pgAdmin | http://localhost:5050 |

### Alternative: everything in one terminal

For active development you can run the API and both portals in a single terminal with prefixed, colored output:

```bash
npm install                # once, at the repository root
npm run db:up              # PostgreSQL and pgAdmin
npm run dev:all            # API + Admin Portal + Author Portal; Ctrl+C stops all three
npm run db:stop            # when you are done
```

`dev:all` reads `.env` for you. `npm run install:all` runs `npm ci` in both portals.

### Troubleshooting

- **"Missing .env file"**: run `cp .env.example .env` (see first-time setup).
- **"Docker is installed but not running"**: start Docker Desktop (or the Docker daemon) and run the start script again.
- **"Port 5080 / 5173 / 5174 is already in use"**: another program owns the port. If it is a previous run, run the stop script. Otherwise find it with `lsof -i :5173` (macOS/Linux) or `netstat -ano | findstr :5173` (Windows) and stop it. The portals use fixed ports because the API only allows those two origins (CORS). If PostgreSQL or pgAdmin collide, change `POSTGRES_PORT` / `PGADMIN_PORT` in `.env`.
- **The API never becomes Healthy, or `/health` returns 503**: the API cannot reach the database. Check that `ConnectionStrings__Default` in `.env` uses the same user, password and port as `POSTGRES_USER`, `POSTGRES_PASSWORD` and `POSTGRES_PORT`, then read `.run/api.log`. The database password is fixed when the database is first created: if you changed `POSTGRES_PASSWORD` afterwards, run `docker compose down -v` (this deletes the data) and start again.
- **"Dependencies of admin-portal are not installed"**: run `npm ci` in that portal's folder.
- **".NET SDK 10 or newer is required" / "Node.js 22 LTS or newer is required"**: install the version shown; check with `dotnet --version` and `node --version`.
- **`Permission denied` on a `.sh` script**: run `chmod +x scripts/*.sh`.

The sections below describe each part in more detail.

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
