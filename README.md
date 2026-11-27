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
    RepairHub.Api                HTTP host: controllers, JWT setup, cookies, error mapping, migrate/create-workshop commands
    RepairHub.Application        use cases (auth, users, customers, devices, repair orders), DTOs, validation, permissions
    RepairHub.Domain             entities (Workshop, User, Customer, Device, RepairOrder, history) and the order workflow
    RepairHub.Infrastructure     EF Core context, tenant query filters, order numbering, migrations, password hashing
  tests/RepairHub.Tests          unit tests + API integration tests on real PostgreSQL (Testcontainers)
frontend/                        React application (+ Dockerfile: production build served by Caddy)
docker-compose.yml               DEVELOPMENT: PostgreSQL + API (frontend via `npm run dev`)
.env.example                     template for the development compose variables
deploy/                          PRODUCTION: docker-compose.yml, Caddyfile, .env.example, backup/ (backup service)
```

Dependency direction: `Api → Application, Infrastructure`, `Infrastructure → Application, Domain`, `Application → Domain`.

## Prerequisites

- .NET SDK 10.0
- Node.js 22+ and npm
- Docker Desktop (or Docker Engine with Compose v2) — also needed to run the backend tests

## Quick start (Docker, development)

The root `docker-compose.yml` is for development only (Development environment, Swagger, API on
`localhost:8080`, Vite dev server for the UI). For a real workshop use [Production deployment](#production-deployment).

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

## Production deployment

Everything for production lives in `deploy/`. It is a separate Compose project (`repairhub-prod`) with its own
volumes, so it never touches the development database.

```
browser ──HTTPS 443──► web (Caddy) ──┬─ /api/*, /health/* ──► api:8080 ──► postgres:5432
           HTTP 80 → redirect        └─ everything else  ──► built SPA (index.html fallback)
```

| Service | What | Published |
|---|---|---|
| `web` | Caddy 2: serves the built frontend, reverse proxy to the API, obtains and renews HTTPS certificates, security headers | 80, 443 |
| `api` | ASP.NET Core in `Production` (no Swagger, JSON logs, no SQL logging, Secure cookies enforced) | — |
| `migrate` | one-shot `migrate` command; runs before the API starts, then exits | — |
| `postgres` | PostgreSQL 17, data in the `repairhub-prod_postgres-data` volume | — |
| `backup` | daily `pg_dump` backups into `BACKUP_DIR` on the host, verified, with retention | — |

Only Caddy is reachable from outside; the API and the database are on an internal Docker network.

### Prerequisites

- A Linux server (or a Windows/macOS machine with Docker Desktop for a small shop) with Docker Engine and
  Compose v2, 2 GB RAM, and this repository checked out (`git clone …`).
- Ports **80 and 443** free on the server (and open in its firewall).
- **With a domain (recommended):** a DNS `A` record (e.g. `repair.example.kz`) pointing to the server's public
  IP, and ports 80/443 reachable from the internet (router port forwarding if the server is in the shop).
- **Local network only:** a fixed LAN IP for the server (DHCP reservation in the router).

### Configuration (`deploy/.env`)

```bash
cd deploy
cp .env.example .env      # then edit .env; it is git-ignored
```

| Variable | Example | Meaning |
|---|---|---|
| `RH_SITE_ADDRESS` | `repair.example.kz` or `192.168.1.10` | Address users type in the browser, without `https://` |
| `RH_TLS` | `admin@example.kz` or `internal` | E-mail → Let's Encrypt certificate; `internal` → Caddy's local CA |
| `HTTP_PORT` / `HTTPS_PORT` | `80` / `443` | Keep the defaults (Let's Encrypt and redirects expect them) |
| `RH_VERSION` | `v1.2.0` | Tag of the images built for this deployment (used for rollback) |
| `POSTGRES_DB` / `POSTGRES_USER` | `repairhub` | Database name and user |
| `POSTGRES_PASSWORD` | random, 32+ chars | **Secret.** `openssl rand -base64 32` |
| `JWT_SIGNING_KEY` | random, 48+ chars | **Secret.** `openssl rand -base64 48`; changing it signs everyone out |
| `BACKUP_SCHEDULE` | `03:00` or `03:00,13:30` | Daily backup time(s), 24-hour, in `BACKUP_TZ` |
| `BACKUP_TZ` | `Asia/Almaty` | Time zone of `BACKUP_SCHEDULE` and of the backup log timestamps |
| `BACKUP_KEEP_DAYS` | `7` | Backups older than this are deleted… |
| `BACKUP_KEEP_MIN` | `3` | …except the newest N, which are always kept |
| `BACKUP_MAX_AGE_HOURS` | `26` | `backup` turns *unhealthy* when the newest backup is older |
| `BACKUP_DIR` | `./backups` or `/var/backups/repairhub` | Host directory for the backup files (relative = relative to `deploy/`) |

Compose refuses to start if a required variable is empty. Store a copy of `deploy/.env` in a password
manager: without `POSTGRES_PASSWORD` a backup of the volume is hard to use.

Fixed internal addresses: the Docker network uses `172.30.99.0/24` and Caddy has `172.30.99.10`. The API
trusts `X-Forwarded-For`/`X-Forwarded-Proto` **only** from that address. If the subnet clashes with
an existing network on the server, change it in `deploy/docker-compose.yml` (three places).

### HTTPS

- **Domain + `RH_TLS=<e-mail>`:** Caddy obtains a Let's Encrypt certificate on first start and renews it
  automatically. Check with `docker compose logs web | grep -i certificate`. If it fails, the DNS record or
  port 80/443 forwarding is wrong (see Troubleshooting).
- **HTTPS in a local network (`RH_TLS=internal`):** Caddy issues the certificate from its own local CA (no
  internet needed). Each device must trust that CA **once**, otherwise the browser shows a certificate warning:

  ```bash
  docker compose cp web:/data/caddy/pki/authorities/local/root.crt ./repairhub-root.crt
  ```

  Install `repairhub-root.crt` as a trusted root: Windows: double-click → Install Certificate → Local
  Machine → "Trusted Root Certification Authorities"; macOS: Keychain Access → System → trust "Always";
  Android: Settings → Security → Install certificate → CA certificate; iOS: open the file, install the
  profile, then Settings → General → About → Certificate Trust Settings → enable. The CA lives in the
  `repairhub-prod_caddy-data` volume; deleting that volume creates a new CA and every device must trust it again.

Certificates and the local CA are kept in the `caddy-data` volume, so restarts do not re-issue them. Caddy
redirects HTTP to HTTPS and adds HSTS, CSP, `X-Frame-Options: DENY`, `nosniff` and `Referrer-Policy` headers
(`deploy/Caddyfile`).

### First deployment

```bash
cd deploy
docker compose up -d --build           # builds images, migrates the database, starts everything
docker compose ps                      # api and web "healthy", migrate "Exited (0)"

# create the workshop and its Owner (prompts for the password; or pass RH_OWNER_PASSWORD with -e)
docker compose run --rm api create-workshop \
  --workshop-name "My Repair Shop" --owner-name "Full Name" --owner-email owner@example.kz
```

Open `https://<RH_SITE_ADDRESS>` from any device in the network and sign in.

### Migrations

The `migrate` service runs `dotnet RepairHub.Api.dll migrate` once per `docker compose up`, **before** the API
starts (`depends_on: service_completed_successfully`); if it fails, the API is not started with a mismatched
schema. API containers never migrate themselves (`Database__ApplyMigrationsOnStartup=false`), so several API
instances cannot race on the schema. To run it by hand: `docker compose run --rm migrate`. On a brand-new
database EF Core logs one "Failed executing DbCommand … __EFMigrationsHistory" error before creating that
table; this is expected. Migrations are forward-only.

### Backups

The `backup` service takes a backup every day at `BACKUP_SCHEDULE`, and also right after it starts when the
newest backup is older than `BACKUP_MAX_AGE_HOURS` (first deployment, server was off at backup time). It never
restores anything by itself.

- **What:** `pg_dump` custom format (compressed, restored with `pg_restore`). It reads one consistent snapshot
  of the database while the application keeps working: reads and writes are not blocked. A migration that
  needs an exclusive lock waits for the running dump (seconds for a workshop database), so do not schedule
  backups at the time of an update.
- **Where:** `BACKUP_DIR` on the host (default `deploy/backups/`, git-ignored), outside the database volume:
  `repairhub-20261127T220000Z.dump` (time in UTC) plus `….dump.sha256`. Files are created with mode `600`,
  owner root: they contain customer data; on Linux read them with `sudo`.
- **Verification:** a new archive must be non-empty, have a readable table of contents, decompress completely
  and contain table data; only then does it get its final name and checksum. A failed attempt leaves no file.
- **Retention:** after every *successful* backup, files older than `BACKUP_KEEP_DAYS` are deleted, but the newest
  `BACKUP_KEEP_MIN` are always kept. A failing backup never deletes anything, so the last good backup survives
  any number of failures.
- **Monitoring:** every run logs `backup OK …` or `ERROR: backup FAILED …` (never the password). The container
  is *unhealthy* in `docker compose ps` when the last attempt failed or the newest backup is too old: check it
  regularly or connect it to your monitoring.

```bash
cd deploy
docker compose exec backup rh-backup run          # backup now (also before every update)
docker compose exec backup rh-backup list         # backups, newest first, and the last result
docker compose exec backup rh-backup verify repairhub-20261127T220000Z.dump
docker compose logs backup                        # history of runs
docker compose ps backup                          # healthy / unhealthy
```

**Copy backups off the server.** A backup on the same disk does not survive a disk failure, theft or
ransomware. At least weekly (better daily, automated) copy `BACKUP_DIR` somewhere else, for example:

```bash
# Linux, to another machine (e.g. from root's crontab: 0 4 * * * rsync …)
sudo rsync -a /var/backups/repairhub/ admin@nas.local:/backups/repairhub/
# Linux, to a USB disk
sudo rsync -a /var/backups/repairhub/ /media/usb/repairhub/
```

```powershell
# Windows / Docker Desktop, to a network share or a USB disk
robocopy C:\RepairHub\deploy\backups \\NAS\backups\repairhub *.dump *.sha256 /XO
```

Keep the copies in an encrypted or locked place, and keep `deploy/.env` (database password) separately. A copied
file can be checked anywhere with `sha256sum -c repairhub-….dump.sha256`.

### Restore

A restore is always a manual command and needs `--confirm`; without it, it only prints what it would do. The
file is verified first. Data is restored into a new database that is swapped in only when the restore succeeded,
so a failed restore leaves the current database unchanged; after a successful one the previous database is
**kept** as `repairhub_before_restore_<time>` until you drop it.

To use a file from external storage, copy it (and its `.sha256`) into `BACKUP_DIR` first.

**1. Look at old data without touching production** (for example, to find deleted records and enter them again):

```bash
docker compose exec backup rh-backup restore repairhub-20261127T220000Z.dump --target repairhub_restore --confirm
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d repairhub_restore'
# when done:
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d postgres -c "DROP DATABASE repairhub_restore"'
```

**2. Replace production with a selected backup** (accidental deletion, failed update). Everything entered after
that backup is lost from the live database (it stays in the kept copy):

```bash
docker compose exec backup rh-backup run          # safety copy of the current state
docker compose exec backup rh-backup list         # choose the file
docker compose stop web api
docker compose exec backup rh-backup restore repairhub-20261127T220000Z.dump --target repairhub          # dry run
docker compose exec backup rh-backup restore repairhub-20261127T220000Z.dump --target repairhub --confirm
docker compose up -d                              # migrate runs first, then the application starts
```

The restore refuses to replace the database while the application is connected to it.

**3. Complete database loss** (volume deleted, new server). The backups must be in `BACKUP_DIR` (copy them back
from external storage) and `deploy/.env` must have the previous `POSTGRES_*` values:

```bash
docker compose up -d postgres                     # starts an empty database
docker compose run --rm --no-deps backup restore repairhub-20261127T220000Z.dump --target repairhub --confirm
docker compose up -d
```

After checking the application, drop the kept copy:

```bash
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d postgres -c "\l"'
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d postgres -c "DROP DATABASE repairhub_before_restore_20261127230000"'
```

### Update procedure

```bash
cd deploy
docker compose exec backup rh-backup run         # note the file name: it is the rollback point
git pull                               # or: git checkout v1.3.0
# set RH_VERSION in .env to the new version, e.g. v1.3.0 (keeps the previous images for rollback)
docker compose up -d --build           # migrate runs first, then api and web are replaced
docker compose ps && docker compose logs --tail 50 api
```

Expect a few seconds of downtime while the API container is replaced. Signed-in users stay signed in
(refresh tokens are in the database).

### Rollback

1. Code only (the update had no new migration): set `RH_VERSION` back to the previous tag and
   `docker compose up -d --no-build` — the previous images are still on the server.
2. The update included a migration: stop `web api`, restore the backup taken before the update with
   `--target repairhub --confirm` (see Restore, case 2), then do step 1.
   Do not run old code against a newer schema.

### Logs

```bash
docker compose logs -f api             # JSON: requests (method, path, status, duration), sign-ins, errors
docker compose logs -f web             # Caddy access log (JSON) and certificate events
docker compose logs migrate            # last migration run
docker compose logs backup             # backup runs, retention, failures
```

Logs never contain passwords, tokens or cookies (Caddy redacts `Cookie`/`Authorization`; the API logs only
ids). Docker's default `json-file` log driver keeps logs until the container is removed; on a long-running
server configure rotation in `/etc/docker/daemon.json` (`"log-opts": {"max-size": "10m", "max-file": "5"}`).

Health: `https://<site>/health/ready` returns 200 when the API can reach the database. Container health
is visible in `docker compose ps`.

### Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `required variable … is missing a value` | Fill in `deploy/.env` (run compose from `deploy/`) |
| Browser: "certificate not trusted" (`RH_TLS=internal`) | Install `repairhub-root.crt` on that device (see HTTPS) |
| Let's Encrypt fails in `logs web` | DNS `A` record does not point to the server, or ports 80/443 are not forwarded/open |
| Site does not open from other devices | Server firewall blocks 80/443 (Windows: allow inbound TCP 80, 443), or wrong `RH_SITE_ADDRESS` |
| `api` unhealthy / `web` not starting | `docker compose logs api migrate` — usually the database password or a failed migration |
| `Address already in use` | Another web server (IIS, nginx) uses port 80/443 |
| Signed out after every reload | The site is opened over plain HTTP or another address than `RH_SITE_ADDRESS`; use `https://<RH_SITE_ADDRESS>` |
| `backup` unhealthy | `docker compose logs backup`: database down, disk full, or `BACKUP_DIR` not writable (owner must be root, e.g. created by Docker) |
| `Pool overlaps with other one on this address space` | Change the `172.30.99.0/24` subnet in `deploy/docker-compose.yml` |

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
| `Database__ApplyMigrationsOnStartup` | env | `true` in the development compose; `false` in production (the `migrate` service runs instead) |
| `ReverseProxy__TrustedProxies__N` | env | IPs/CIDRs allowed to set `X-Forwarded-For`/`-Proto` (production: the Caddy container); empty = loopback only |
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
(cd deploy && docker compose config --quiet)   # needs deploy/.env
sh -n deploy/backup/rh-backup.sh               # backup script syntax
```

## Database migrations

EF Core migrations are the only way to change the schema (`backend/src/RepairHub.Infrastructure/Persistence/Migrations`).

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> --project src/RepairHub.Infrastructure --startup-project src/RepairHub.Api --output-dir Persistence/Migrations
dotnet ef database update      --project src/RepairHub.Infrastructure --startup-project src/RepairHub.Api
```

Applying migrations without the EF tools (e.g. on a server): `dotnet RepairHub.Api.dll migrate` — in
production this is the `migrate` service (see [Production deployment](#migrations)).

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
