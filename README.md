# RepairHub

Repair service management for laptop, computer, smartphone and printer repair shops.

**Status:** Phase 0 — project foundation. Business modules (orders, customers, inventory, …) are placeholders.

## Stack

| Area | Technology |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, EF Core 10, Npgsql |
| Database | PostgreSQL 17 |
| Frontend | React 19, TypeScript (strict), Vite, Tailwind CSS 4, shadcn/ui, React Query, React Router |
| Infrastructure | Docker, Docker Compose |

## Repository layout

```
backend/
  RepairHub.slnx
  Directory.Build.props          shared build settings (net10.0, nullable, warnings as errors)
  src/
    RepairHub.Api                HTTP host: Program.cs, middleware, OpenAPI, health checks, logging
    RepairHub.Application        use cases, DTOs, validation (empty until the first module)
    RepairHub.Domain             entities and business rules, no dependencies (empty until the first module)
    RepairHub.Infrastructure     EF Core DbContext, PostgreSQL, health check, DI registration
  tests/RepairHub.Tests          xUnit unit + in-memory API integration tests
frontend/                        React application
docker-compose.yml               PostgreSQL + API
.env.example                     template for Docker Compose variables
```

Dependency direction: `Api → Application, Infrastructure`, `Infrastructure → Application, Domain`, `Application → Domain`.

## Prerequisites

- .NET SDK 10.0
- Node.js 22+ and npm
- Docker Desktop (or Docker Engine with Compose v2)

## Quick start (Docker)

```bash
cp .env.example .env          # then set a strong POSTGRES_PASSWORD
docker compose up -d --build
```

| URL | What |
|---|---|
| http://localhost:8080/health/live | Liveness — API process is running |
| http://localhost:8080/health/ready | Readiness — API can reach PostgreSQL (503 if not) |
| http://localhost:8080/swagger | Swagger UI (Development only) |
| http://localhost:8080/openapi/v1.json | OpenAPI document (Development only) |

Then start the frontend:

```bash
cd frontend
npm install
npm run dev                   # http://localhost:5173
```

The Vite dev server proxies `/api` and `/health` to `http://localhost:8080`
(override with `VITE_API_PROXY_TARGET` in `frontend/.env.local`).

Stop everything with `docker compose down` (add `-v` to also delete the database volume).

## Running the API without Docker

Start only PostgreSQL, then run the API from the SDK:

```bash
docker compose up -d postgres

cd backend
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=repairhub;Username=repairhub;Password=<your password>" \
  --project src/RepairHub.Api
dotnet run --project src/RepairHub.Api     # http://localhost:5080/swagger
```

When running the API this way, point the frontend at it: `VITE_API_PROXY_TARGET=http://localhost:5080`.

## Configuration

The connection string is read from `ConnectionStrings:Default`:

- **Docker / CI / production:** environment variable `ConnectionStrings__Default`
- **Local development:** `dotnet user-secrets` (see above)

It is never stored in `appsettings*.json`. The API refuses to start if it is missing.

Logging uses the built-in .NET console logger: JSON with UTC timestamps in
Production, readable single-line output in Development. Each request produces one
log entry (method, path, status, duration); headers and bodies are not logged.

## Tests and builds

```bash
cd backend
dotnet build
dotnet test

cd frontend
npm run lint
npm run build

docker compose config --quiet   # validates docker-compose.yml
```

The backend tests host the API in memory and do not need a database.

## Database migrations

EF Core migrations are the only way to change the schema. The `dotnet-ef` tool is
pinned in `backend/dotnet-tools.json`:

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> --project src/RepairHub.Infrastructure --startup-project src/RepairHub.Api
dotnet ef database update      --project src/RepairHub.Infrastructure --startup-project src/RepairHub.Api
```

No migrations exist yet — the schema starts with the first business module.

## Adding shadcn/ui components

```bash
cd frontend
npx shadcn@latest add <component>
```

Components are copied into `src/components/ui` and can be edited.

## Conventions

- Timestamps are stored in UTC and displayed in `Asia/Almaty`.
- Money uses `decimal` (backend) and is shown in KZT.
- UI copy lives in `frontend/src/locales/ru.ts` (Russian now, Kazakh planned).
