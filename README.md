# RepairHub

Repair service management for laptop, computer, smartphone and printer repair shops.

**Status:** Phase 2 — customers, devices, repair orders with a controlled workflow and history,
on top of Phase 1 (workshops, staff accounts, authentication and authorization).
Inventory, payments, expenses, warranty, notifications and reports are still placeholders.

## Stack

| Area | Technology |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, EF Core 10, Npgsql, JWT bearer auth |
| Database | PostgreSQL 17 |
| Frontend | React 19, TypeScript (strict), Vite, Tailwind CSS 4, shadcn/ui, React Query, React Router, React Hook Form, Zod |
| Infrastructure | Docker, Docker Compose |

## Repository layout

```
backend/
  RepairHub.slnx
  Directory.Build.props          shared build settings (net10.0, nullable, warnings as errors)
  src/
    RepairHub.Api                HTTP host: controllers, JWT setup, cookies, error mapping, create-workshop command
    RepairHub.Application        use cases (auth, users, customers, devices, repair orders), DTOs, validation, permissions
    RepairHub.Domain             entities (Workshop, User, Customer, Device, RepairOrder, history) and the order workflow
    RepairHub.Infrastructure     EF Core context, tenant query filters, order numbering, migrations, password hashing
  tests/RepairHub.Tests          unit tests + API integration tests on real PostgreSQL (Testcontainers)
frontend/                        React application
docker-compose.yml               PostgreSQL + API
.env.example                     template for Docker Compose variables
```

Dependency direction: `Api → Application, Infrastructure`, `Infrastructure → Application, Domain`, `Application → Domain`.

## Prerequisites

- .NET SDK 10.0
- Node.js 22+ and npm
- Docker Desktop (or Docker Engine with Compose v2) — also needed to run the backend tests

## Quick start (Docker)

```bash
cp .env.example .env          # set POSTGRES_PASSWORD and a long random JWT_SIGNING_KEY
docker compose up -d --build  # migrations are applied on startup (APPLY_MIGRATIONS_ON_STARTUP=true)
```

Create the first workshop and its Owner (see [Creating the first workshop](#creating-the-first-workshop)):

```bash
docker compose run --rm api create-workshop \
  --workshop-name "My Repair Shop" --owner-name "Full Name" --owner-email owner@example.kz
```

Then start the frontend and sign in at http://localhost:5173:

```bash
cd frontend
npm install
npm run dev
```

The Vite dev server proxies `/api` and `/health` to `http://localhost:8080`
(override with `VITE_API_PROXY_TARGET` in `frontend/.env.local`), so the browser talks to a single
origin and the refresh-token cookie works without CORS.

| URL | What |
|---|---|
| http://localhost:8080/health/live | Liveness — API process is running |
| http://localhost:8080/health/ready | Readiness — API can reach PostgreSQL (503 if not) |
| http://localhost:8080/swagger | Swagger UI (Development only; use "Authorize" with an access token) |

Stop everything with `docker compose down` (add `-v` to also delete the database volume).

## Creating the first workshop

There is **no public registration and no HTTP endpoint** for creating workshops. The server-side
`create-workshop` command creates a workshop together with its first Owner — being able to run commands
on the server is the authorization.

```bash
# Interactive: prompts for the Owner password (hidden input, asked twice)
docker compose run --rm api create-workshop \
  --workshop-name "My Repair Shop" \
  --owner-name "Айгерим Сапарова" \
  --owner-email owner@example.kz \
  [--address "..."] [--phone "..."] [--currency KZT] [--timezone Asia/Almaty]

# Non-interactive (scripts): pass the password through the environment, never as an argument
RH_OWNER_PASSWORD='...' docker compose run --rm -e RH_OWNER_PASSWORD api create-workshop ...
```

Safety rules:

- By default the command **refuses to run if any workshop already exists**, so re-running the first-time
  setup cannot create a second tenant. Add `--allow-additional-workshop` deliberately for more workshops.
- Workshop names (case-insensitive) and Owner emails must be unique; nothing is written if a check fails.
- Concurrent runs are serialized with a PostgreSQL advisory lock.
- Exit codes: `0` created, `64` usage error, `65` invalid input, `75` refused (already exists / duplicate).

Without Docker: `dotnet run --project src/RepairHub.Api -- create-workshop ...` from `backend/`.

## Authentication and authorization

- **Login** with email + password (`POST /api/auth/login`). Emails are unique across all workshops.
- **Access token:** JWT (HS256), 15 minutes, kept **only in memory** by the frontend.
- **Refresh token:** 256-bit random value in an **HttpOnly, SameSite=Strict cookie limited to `/api/auth`**;
  stored in the database only as a SHA-256 hash. Each refresh rotates it; presenting an already-rotated
  token revokes the whole session and invalidates the user's access tokens (theft detection).
- `refresh` and `logout` additionally require the `X-Requested-With: RepairHub` header (CSRF defence).
- **Immediate revocation:** deactivation, role change and password change bump the user's token version,
  so existing access tokens stop working on the next request.
- **Passwords:** 10–128 characters, not equal to the email; hashed with PBKDF2-HMAC-SHA512 (210k iterations).
- **Rate limiting:** login 10/min and refresh 60/min per client IP (configurable under `Auth:RateLimits`).

Roles and permissions:

| Action | Owner | Admin | Technician / Receptionist |
|---|---|---|---|
| View workshop, own profile, change own password | ✓ | ✓ | ✓ |
| Edit workshop settings | ✓ | — | — |
| List and view staff | ✓ | ✓ | — |
| Create / edit / deactivate / reset password | any role | Technician & Receptionist only | — |

Nobody can change their own role or deactivate themselves. A workshop always keeps at least one active
Owner; concurrent changes to Owners are serialized with a row lock on the workshop.

**Tenant isolation:** every tenant-owned table has a fail-closed EF Core query filter — without an
authenticated workshop it matches no rows. Records of another workshop return 404. Reading across workshops
is possible only through `TenantFilterBypass.AcrossAllWorkshops()`, whose callers are allow-listed in
`TenantFilterGuardTests` (login/refresh, token validation, bootstrap).

## Customers, devices and repair orders

- **Customers** belong to one workshop and are archived, never deleted. Phones are stored as entered (any country
  format, 5–20 digits); duplicates are detected by a match key = the last 10 digits, so `+7 701…`, `8 701…` and
  `701…` match. Creating a customer with a phone that already exists returns `409 customer_phone_exists` with the
  matches unless the request sets `allowDuplicatePhone: true`. Customers are never merged automatically.
- **Devices** belong to a customer; serial number and IMEI are optional and not unique. A device is reused for
  every repair. Archived customers/devices cannot get new orders; archiving with open orders is refused.
- **Repair orders** keep a **snapshot** of the customer's name/phone and the device details taken at creation.
  Editing the customer or device never changes existing orders; a wrong snapshot is corrected on the order itself
  (`PUT /api/repair-orders/{id}/snapshot`) and the old/new values go to the history.
- **Creation is one transaction** (`POST /api/repair-orders`): optional new customer, optional new device, the order
  number, the order and its first history events — all or nothing.
- **Numbers** look like `RH-2026-000152`: per workshop, restarting every year by the workshop's local date. A counter
  row per (workshop, year) is incremented with an atomic `INSERT … ON CONFLICT DO UPDATE … RETURNING` inside the
  creation transaction: concurrent creations queue on that row, numbers are never duplicated and a rolled-back
  creation leaves no gap.
- **Optimistic concurrency:** every order change sends the `version` it read; a stale version returns
  `409 concurrency_conflict` (the UI reloads the order and asks to retry).
- **History** (`GET /api/repair-orders/{id}/history`) is append-only: status changes, outcome, assignment,
  diagnostics, intake/price/date changes, snapshot corrections and notes, each with old/new value, actor and UTC
  time. There is no API to edit or delete events, and the DbContext refuses to update or delete them.

### Status workflow

```
Received ─► Diagnostics ─► AwaitingApproval ─► InRepair ◄─► WaitingForParts
   │            │  ▲              │               │
   │            │  └──────────────┘               ▼
   └────────────┴──────────────────────────► ReadyForPickup ─► Issued
Cancelled: only before repair work started (never after InRepair)
```

| From | To | Who |
|---|---|---|
| Received | Diagnostics, InRepair | Owner/Admin, assigned Technician |
| Diagnostics | AwaitingApproval, InRepair, WaitingForParts, ReadyForPickup (NotRepaired/Declined) | Owner/Admin, assigned Technician |
| AwaitingApproval | InRepair, WaitingForParts, ReadyForPickup (NotRepaired/Declined) | Owner/Admin, Receptionist, assigned Technician |
| AwaitingApproval | Diagnostics | Owner/Admin, assigned Technician |
| WaitingForParts | InRepair, ReadyForPickup (NotRepaired/Declined) | Owner/Admin, assigned Technician |
| WaitingForParts | AwaitingApproval | Owner/Admin, Receptionist, assigned Technician |
| InRepair | WaitingForParts, AwaitingApproval, ReadyForPickup (any outcome) | Owner/Admin, assigned Technician |
| ReadyForPickup | InRepair (rework) | Owner/Admin, Receptionist, assigned Technician |
| ReadyForPickup | Issued | Owner/Admin, Receptionist |
| Received, Diagnostics, AwaitingApproval, WaitingForParts | Cancelled (reason required, only if never InRepair) | Owner/Admin, Receptionist |

`ReadyForPickup` requires an outcome: `Repaired` (only from InRepair), `NotRepaired` or `Declined`. When work has
started and the customer declines, the order goes to ReadyForPickup → Issued, so the handover is recorded.
`Issued` and `Cancelled` are final; a returning device gets a new order. The rules live in one table
(`RepairOrderWorkflow`); the API returns `allowedTransitions` for the current user, and the UI only shows those.

### Permissions

| Action | Owner / Admin | Receptionist | Technician |
|---|---|---|---|
| Customers and devices: view, create, edit | ✓ | ✓ | — (sees contacts inside orders) |
| Archive / restore customers and devices | ✓ | — | — |
| View all orders of the workshop, history | ✓ | ✓ | ✓ |
| Create orders, edit intake, correct snapshots | ✓ | ✓ | — |
| Prices and expected completion date | ✓ | ✓ | — |
| Assign / reassign technician | ✓ | ✓ | — |
| Take an unassigned order ("Take order", atomic) | ✓ | — | ✓ |
| Diagnostics | ✓ | — | assigned orders only |
| Status changes | see the table above | | |
| Notes (also on closed orders) | ✓ | ✓ | ✓ |

Only active Technicians, Admins and Owners can be assigned. "Take order" is one conditional `UPDATE … WHERE
assigned_technician_id IS NULL`, so when two technicians take the same order at once exactly one succeeds and the
other gets `409 concurrency_conflict`.

**Tenant isolation** for these tables uses the same fail-closed query filter as Phase 1. In addition, PostgreSQL
foreign keys on `(workshop_id, …)` pairs make it impossible to link a device, order, technician or history event to
another workshop's record, even if application code had a bug.

## Localization

All UI text lives in `frontend/src/locales/ru.ts` (reference) and `kk.ts` (Kazakh); the compiler checks that both
have the same keys. The language switch (login page and user menu) is saved in the browser and applied with a page
reload; the session survives it. Statuses, device types, outcomes, validation messages and API error codes are
translated on the client — the API returns language-independent codes. Dates are shown in the workshop's time zone
(numeric in the Kazakh UI, because browsers' Kazakh month names are often missing) and money in the workshop
currency. Kazakh texts should be reviewed by a native speaker from the workshop before going live.

## Configuration

| Setting | Source | Notes |
|---|---|---|
| `ConnectionStrings__Default` | env / user secrets | required |
| `Jwt__SigningKey` | env (`JWT_SIGNING_KEY` in `.env`) / user secrets | required, ≥ 32 bytes |
| `Database__ApplyMigrationsOnStartup` | env | `true` in Docker Compose; production should migrate explicitly |
| `Auth__RefreshCookie__Secure` | appsettings | `true` by default and **enforced outside Development**; `false` only in Development for plain-HTTP local use |

Secrets are never stored in `appsettings*.json`. The API refuses to start without a connection string or
a sufficiently long signing key, or with an insecure cookie outside Development.

## Running the API without Docker

```bash
docker compose up -d postgres

cd backend
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=repairhub;Username=repairhub;Password=<your password>" \
  --project src/RepairHub.Api
dotnet user-secrets set "Jwt:SigningKey" "<long random value>" --project src/RepairHub.Api
dotnet tool restore
dotnet ef database update --project src/RepairHub.Infrastructure --startup-project src/RepairHub.Api
dotnet run --project src/RepairHub.Api     # http://localhost:5080/swagger
```

Point the frontend at it with `VITE_API_PROXY_TARGET=http://localhost:5080`.

## Tests and builds

```bash
cd backend
dotnet build
dotnet test            # needs Docker: integration tests start a PostgreSQL container

cd frontend
npm run lint
npm run build

docker compose config --quiet
```

## Database migrations

EF Core migrations are the only way to change the schema (`backend/src/RepairHub.Infrastructure/Persistence/Migrations`).

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> --project src/RepairHub.Infrastructure --startup-project src/RepairHub.Api --output-dir Persistence/Migrations
dotnet ef database update      --project src/RepairHub.Infrastructure --startup-project src/RepairHub.Api
```

Tables and columns use `snake_case`; timestamps are `timestamptz` in UTC.

## Adding shadcn/ui components

```bash
cd frontend
npx shadcn@latest add <component>
```

## Conventions

- Timestamps are stored in UTC and displayed in the workshop's time zone (default `Asia/Almaty`).
- Money uses `decimal` (backend); the workshop currency defaults to KZT.
- UI copy lives in `frontend/src/locales/` (Russian and Kazakh); never hard-code text in components.
- API errors are RFC 7807 problem details with a stable `code` (e.g. `email_taken`, `last_owner`,
  `invalid_status_transition`, `concurrency_conflict`).
- Calendar dates without time (expected completion date) are `date` columns / `"YYYY-MM-DD"` in the workshop's time zone.
