# DotNet Career Path

## Objective

A long-term, project-based learning workspace to transfer Berkan's existing professional backend experience (Node.js, TypeScript, Express, Keystone.js, Apollo GraphQL, Prisma, REST/GraphQL APIs, SQL databases, JWT, RBAC, and related backend fundamentals) into the .NET ecosystem, culminating in production-minded junior/junior+ .NET backend competence.

## Learning approach

* Project-based: every phase produces a real, working application, not isolated exercises.
* Explain-then-implement: concepts and a plan are presented first; application code is written only after explicit approval.
* Vertical slices: each session delivers one complete, demonstrable piece of behavior.
* Evidence-driven: skills are only credited with recorded evidence (tests, commits, working endpoints, explanations).
* Depth over breadth: when in doubt, fewer topics are covered more deeply rather than many topics superficially.
* Generated or compiling code alone does not count as completed learning. A task is only done when Berkan can explain the runtime and data flow, tests pass, and evidence is recorded — see the Definition of Done in `CLAUDE.md`.

## Project list

1. **RoadmapOS** — single-user ASP.NET Core MVC app tracking this very learning roadmap.
2. **StockPilot Inventory and Order API** — controller-based REST Web API with EF Core, auth, and testing.
3. **FieldOps SaaS Modular Monolith** — multi-tenant modular monolith with caching, background jobs, and Docker.
4. **Distributed FieldOps** — the FieldOps domain split into services with messaging, outbox/inbox, and search.
5. **Angular Operations Console** — Angular frontend integrating with the FieldOps backend.
6. **Cloud and Kubernetes Deployment** — containerized, Kubernetes-orchestrated, cloud-deployed delivery pipeline.

## Current status

* **Phase:** Phase 1 — RoadmapOS (V1 released); Phase 2 — StockPilot Inventory and Order API (complete); Phase 3 — FieldOps SaaS Modular Monolith (Weeks 7-12, nearly complete)
* **Project:** FieldOps SaaS Modular Monolith
* **Week:** 12 (final week of Phase 3)
* **Day:** 65
* **Progress:** ~59%

See [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md) for full detail.

## RoadmapOS (Phase 1 project)

RoadmapOS is a single-user ASP.NET Core MVC application that tracks this very learning roadmap: skills with target/current levels, roadmap phases/projects/milestones, evidence records, and an overall/category progress dashboard.

### Prerequisites

* .NET 10 SDK
* SQL Server (Express, Developer, or any local instance) — a connection string is configured in `src/RoadmapOS.Web/appsettings.Development.json` for a local `localhost\SQLEXPRESS` instance; adjust it if your instance name differs.

### Running it locally

```
cd src/RoadmapOS.Web
dotnet restore
dotnet ef database update
dotnet run
```

The app seeds starter data automatically on first run (in `Development` only — see `src/RoadmapOS.Web/Data/DbSeeder.cs`). Then visit:

* `/` — home page
* `/Skills` — list, create and edit skills, with an evidence count per skill
* `/Dashboard` — overall and per-category progress

### Running the tests

```
dotnet test
```

(Run from the repository root to pick up the `RoadmapOS.Web.Tests` project via the solution.)

### Known simplifications (V1)

* Single user, no authentication.
* Seed data is a runtime, idempotent seeder rather than EF Core's `HasData()` (see `Data/DbSeeder.cs` for why).
* `Evidence` records are tracked and displayed but not yet enforced by any business rule.
* No delete flow for skills.
* An advanced UI was deliberately not used.

## StockPilot Inventory and Order API (Phase 2 project)

StockPilot is a controller-based ASP.NET Core Web API for product inventory, with JWT authentication (access + refresh tokens, rotation), role-based and policy-based authorization, EF Core + SQL Server persistence, optimistic concurrency, transactions, and a full test suite (unit, mocked, and real HTTP integration tests running against a Testcontainers-managed, disposable SQL Server).

### Prerequisites

* .NET 10 SDK
* SQL Server (Express, Developer, or any local instance) — a connection string is configured in `src/StockPilot.Api/appsettings.Development.json` for a local `localhost\SQLEXPRESS` instance; adjust it if your instance name differs.
* Docker — required only to run the integration test suite (`tests/StockPilot.Api.Tests`), which spins up its own disposable SQL Server container via Testcontainers. Not needed to run the API itself.

### Running it locally

```
cd src/StockPilot.Api
dotnet restore
dotnet ef database update
dotnet run
```

The app seeds three starter products automatically on first run (in `Development` only — see `src/StockPilot.Api/Data/DbSeeder.cs`). Then visit:

* `/scalar/v1` — interactive API documentation (try requests directly from the browser)
* `/openapi/v1.json` — the raw OpenAPI schema Scalar renders
* `/api/products` — list products (search/sortBy/page/pageSize query parameters, no authentication required)

Two demo accounts exist for `POST /api/auth/login` (see `Controllers/AuthController.cs` — there is no real user store yet, see "Known simplifications" below):

| Username | Password | Role |
|---|---|---|
| `admin` | `Passw0rd!` | `Admin` |
| `employee` | `Employee123!` | `Employee` |

Deleting a product (`DELETE /api/products/{id}`) and bulk-creating products (`POST /api/products/bulk`) both require a valid access token from the `Admin` account; every other endpoint is open to anyone.

### Running the tests

```
dotnet test
```

(Run from the repository root, or `cd tests/StockPilot.Api.Tests` — either picks up all tests via the solution.) This includes real HTTP integration tests (`ProductsAuthorizationIntegrationTests.cs`) that spin up their own disposable SQL Server container — **Docker must be running** for these to pass; every other test (unit tests, mocked tests) runs with no external dependency at all.

### Known simplifications

* Two hardcoded demo accounts (`admin`/`employee`) — no real `Users` table, no registration flow, no password reset.
* The JWT signing key lives in `appsettings.Development.json`, committed to source control — explicitly named and documented as dev-only; a real deployment needs it in user-secrets/Key Vault/an environment variable instead.
* `InMemoryRefreshTokenStore` (currently the app's real, registered implementation) is lost on every app restart and never shared across multiple server instances — production needs a shared store (a database table or Redis).
* Only a `Product` domain exists — the "Order API" half of StockPilot's name (orders, warehouses, inventory movements, stock reservations) has not been built yet; policy-based authorization (`CanManageProducts`) is scoped only to what exists today.
* No rate limiting, no refresh-token-family revocation on detected reuse, no HTTPS certificate pinning — reasonable gaps for a learning project, not claimed as production-hardened.

## FieldOps SaaS Modular Monolith (Phase 3 project)

FieldOps is a multi-tenant field-service management backend built as a **modular monolith**: five independent modules (Organizations, Employees, Customers, Work Orders, Audit Logs), each its own class library with `internal` domain entities and its own physically separate SQL Server database, exposing only a public DTO and interface to the host API. Work orders move through a full lifecycle (Open → Assigned → InProgress → Completed, with reassignment, reopening, and customer approval), guarded by tenant-isolation and role/ownership-based authorization tested across every action. Redis backs a cache-aside status report with a background cache warmer; audit logs, per-organization rate limiting, and idempotency round out the production-minded concerns. A generic `IAiProvider` abstraction (with a deterministic fake implementation) powers an AI-generated summary of a work order's evidence notes, with its own failure-handling path. Structured logging (with correlation IDs) and `/health/live` + `/health/ready` endpoints support debugging; the whole stack (API, SQL Server, Redis) runs via Docker Compose and is exercised by CI on every push.

### Prerequisites

* .NET 10 SDK
* Docker — required either way: to run FieldOps itself via Docker Compose (recommended), or to run its integration test suite (`tests/FieldOps.Api.Tests`), which spins up its own disposable SQL Server container via Testcontainers regardless of how the app itself is run.

### Running it locally (Docker Compose — recommended)

```
cp .env.example .env
# edit .env and set a real SA_PASSWORD
docker compose up --build -d
```

Then, once the containers are up, apply migrations for all five modules against the containerized SQL Server (exposed on `localhost,14330`) — repeat for each module directory:

```
cd src/FieldOps.Modules.Organizations && dotnet ef database update --connection "Server=localhost,14330;Database=FieldOpsOrganizations;User Id=sa;Password=<your SA_PASSWORD>;TrustServerCertificate=True;" && cd ../..
# ...same pattern for FieldOps.Modules.Employees, FieldOps.Modules.WorkOrders, FieldOps.Modules.Customers, FieldOps.Modules.AuditLogs
```

Then visit:

* `http://localhost:5190/health/ready` — should report every dependency `Healthy`
* `http://localhost:5190/api/organizations` — seeded organizations
* `http://localhost:5190/api/workorders` — requires `X-Organization-Id` and `X-Employee-Id` headers (see below)

Tear down with `docker compose down`.

### Running it locally (without Docker Compose)

* A local SQL Server instance (`localhost\SQLEXPRESS`, Windows Authentication) and a local Redis instance (e.g. `docker run -d -p 6379:6379 redis:7-alpine`) are required — connection strings are in `src/FieldOps.Api/appsettings.Development.json`.
* Apply migrations the same way as above, once per module directory, but with plain `dotnet ef database update` (each module's own `*DbContextFactory.cs` already points at `localhost\SQLEXPRESS`).
* `cd src/FieldOps.Api && dotnet run`.

### Seeded demo identities

Every request needs an `X-Organization-Id` header, and (for employee-acting endpoints) an `X-Employee-Id` header, or (for the customer-approval endpoint) an `X-Customer-Id` header — there is no real authentication yet (see "Known simplifications" below).

| Organization | Employee (Admin) | Employee (Member) | Customer |
|---|---|---|---|
| 1 | id `1` | id `2` | id `1` |
| 2 | id `3` | id `4` | — |

Example: create a work order as Org 1's Admin, assign it (evidence can only be added by the assignee), add an evidence note, then read its AI-generated summary:

```
curl -X POST http://localhost:5190/api/workorders \
  -H "X-Organization-Id: 1" -H "X-Employee-Id: 1" -H "Content-Type: application/json" \
  -d '{"Title":"Fix the HVAC unit"}'

curl -X POST http://localhost:5190/api/workorders/1/assign \
  -H "X-Organization-Id: 1" -H "X-Employee-Id: 1" -H "Content-Type: application/json" \
  -d '{"EmployeeId":1}'

curl -X POST http://localhost:5190/api/workorders/1/evidence \
  -H "X-Organization-Id: 1" -H "X-Employee-Id: 1" -H "Content-Type: application/json" \
  -d '{"Note":"Checked the compressor, replaced the filter."}'

curl http://localhost:5190/api/workorders/1/summary -H "X-Organization-Id: 1" -H "X-Employee-Id: 1"
# => "[Fake AI summary] Summarize the following field service evidence notes in one or two sentences:\n1. Checked the compressor, replaced the filter."
```

### Running the tests

```
dotnet test FieldOps.slnx
```

Every test is a real HTTP integration test against a disposable, Testcontainers-managed SQL Server — **Docker must be running**. There is no separate unit-test-only subset that skips Docker, aside from `WorkOrderNoteSummaryServiceTests.cs`, which uses hand-written fakes and needs no infrastructure at all.

### Known simplifications

* `X-Organization-Id` / `X-Employee-Id` / `X-Customer-Id` are plain, unverified client-supplied headers — there is no real authentication (login, tokens) yet, unlike StockPilot's JWT-based auth.
* No real AI provider is integrated — `IAiProvider`'s only implementation (`FakeAiProvider`) is deterministic and offline, proving the abstraction and its failure-handling path work, not that it produces a genuinely useful summary.
* Evidence "attachments" are plain text notes — no real file/photo upload infrastructure exists.
* Each module's database-per-module design (ADR 0003) means there are no real cross-module foreign keys — cross-module references are validated in application code, not enforced by the database.
* Rate limiting and idempotency have no automated behavioral/threshold tests (Redis's unreliability in CI at the time) — verified live instead, a documented and deliberate trade-off, not an oversight.
* Migrations are applied by hand (or a CI retry loop) against the Compose stack — a real deployment would use a dedicated one-off migration job.

## Documentation

* [CLAUDE.md](CLAUDE.md) — persistent instructions and rules for how learning sessions are run.
* [docs/ROADMAP.md](docs/ROADMAP.md) — the full 22-week learning plan.
* [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md) — source of truth for current phase, week, day and next task.
* [docs/REQUIREMENTS_MATRIX.md](docs/REQUIREMENTS_MATRIX.md) — skill requirements, target levels and evidence tracking.
* [docs/LEARNING_LOG.md](docs/LEARNING_LOG.md) — history of completed learning sessions.

## Development environment

* Visual Studio Code
* C# Dev Kit
* Claude Code
* .NET 10 LTS
* Standard .NET CLI (`dotnet new`, `dotnet restore`, `dotnet build`, `dotnet run`, `dotnet test`, `dotnet ef`)

## Note on completion

Generated or compiling code alone does not count as completed learning. A task is only considered done when its Definition of Done is satisfied: intended behavior works, the project builds, relevant tests pass, Berkan can explain the runtime and data flow, failure cases have been considered, and evidence has been recorded.
