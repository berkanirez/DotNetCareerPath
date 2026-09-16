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

* **Phase:** Phase 1 — RoadmapOS (V1 released); Phase 2 — StockPilot Inventory and Order API (Weeks 3-6 complete)
* **Project:** StockPilot Inventory and Order API
* **Week:** 6 (final week of Phase 2)
* **Day:** 31
* **Progress:** ~27%

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
