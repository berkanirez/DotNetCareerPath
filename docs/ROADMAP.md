# ROADMAP.md — 22-Week .NET Career Path

This is the long-term plan for transferring Berkan's existing backend experience (Node.js, TypeScript, Express, Keystone.js, Apollo GraphQL, Prisma, REST/GraphQL APIs, MySQL/PostgreSQL, JWT, RBAC, logging, error handling, rate limiting, webhooks, cron jobs, third-party integrations, PM2, deployment, basic Docker/CI-CD) into the .NET ecosystem, without re-teaching backend concepts he already knows.

Total duration: 22 weeks. Total estimated study time: approximately 220 hours (2 hours/day, 5 days/week).

## Objective

Produce a junior/junior+ .NET backend developer who can:

* Read and write idiomatic C# and ASP.NET Core code.
* Design, implement and test production-minded backend systems with EF Core and SQL Server.
* Apply SOLID, clean code and practical design patterns with judgment, not by rote template.
* Explain request lifecycles, data flow, and trade-offs out loud, in English, in an interview setting.
* Progress from a single monolith, to a modular monolith, to a distributed system, to containerized/cloud deployment — understanding *why* each step is taken, not just *how*.

## Learning principles

* **Transfer, don't restart.** Berkan already understands REST, GraphQL, ORMs, JWT, RBAC, rate limiting, webhooks, cron jobs, and deployment conceptually. Each topic should be taught as "here is the .NET version of what you already know," not from zero.
* **Vertical slices over layers.** Every day produces one working, demonstrable slice of behavior — not a partially wired layer that does nothing on its own.
* **Explain before implement.** Concepts, mental models, and a plan come first. Code follows only after explicit approval (`UYGULA`).
* **Depth over coverage.** When in doubt, go deeper on fewer topics rather than touching more topics shallowly.
* **Evidence-driven.** Every claimed skill level 3 or 4 requires recorded evidence (tests, commits, working endpoints, explanations).
* **No premature complexity.** Authentication, Docker, messaging, and microservices are introduced only in their scheduled phase, never earlier "just in case."
* **Production mindset from day one**, even in a single-user demo app: explain what a demo simplifies and what production would require, every time.

## Career checkpoints

* **Week 2** — Basic C# and ASP.NET Core request flow understood and demonstrated.
* **Week 6** — Junior .NET job applications begin.
* **Week 12** — Production-minded junior/junior+ competence (modular monolith, multi-tenancy, caching, background jobs).
* **Week 17** — Distributed-systems competence (messaging, outbox/inbox, microservice boundaries, search, tracing).
* **Week 20** — Containers, Kubernetes and cloud deployment evidence.
* **Week 22** — Portfolio and interview readiness.

**Honesty rule:** Project work in this roadmap builds real, demonstrable skill and evidence, but it is not professional employment. It must never be described or implied as "3+ years" or "5+ years" of professional experience. Berkan's real professional experience is his Node.js/TypeScript backend work; that may be presented honestly as professional software-development experience. The .NET projects in this roadmap are personal/portfolio projects and must be labeled as such.

---

## Phase 1 — RoadmapOS

**Duration:** Weeks 1–2, approximately 20 hours.

**Purpose:** Build a single-user ASP.NET Core MVC application that tracks learning skills, job requirements, phases, projects, milestones, study sessions, evidence and weighted progress.

V1 explicitly excludes: authentication, Angular, Docker, and distributed systems.

### Week 1

**Day 1**
* Verify .NET and Git environment.
* Explain SDK, runtime, solution and project.
* Initialize Git.
* Create the minimum solution.
* Create and run an ASP.NET Core MVC application.
* Explain generated files at a high level.

**Day 2**
* C# type system.
* Classes.
* Records.
* Interfaces.
* Nullable reference types.
* First domain concepts without persistence.

**Day 3**
* ASP.NET Core request lifecycle.
* `Program.cs`.
* Middleware.
* Routing.
* Controllers.
* Views.
* Dependency injection.
* First read-only vertical slice.

**Day 4**
* SQL Server.
* Entity Framework Core.
* `DbContext`.
* Entities.
* Migrations.
* Persist and retrieve the first record.

**Day 5**
* Model binding.
* Validation.
* Skill create/edit flow.
* Error display.
* Manual verification.

### Week 2

**Day 6**
* Roadmap, phase, project and milestone relationships.
* EF Core relationships.
* Database constraints.

**Day 7**
* Progress-calculation rules.
* Domain service.
* First TDD workflow.
* xUnit.

**Day 8**
* LINQ.
* Dashboard queries.
* Overall and category progress.

**Day 9**
* Requirement mapping.
* Evidence records.
* Logging.
* Seed data.
* Basic error handling.

**Day 10**
* Refactoring.
* Build and test verification.
* English README.
* Demonstration.
* RoadmapOS V1 release.

**Completion gate:**
* Application builds and runs.
* Data persists in SQL Server.
* Skills and roadmap items can be managed.
* Progress calculation is tested.
* MVC request flow can be explained.
* Dependency injection and EF Core can be explained.
* Repository contains an English README.

---

## Phase 2 — StockPilot Inventory and Order API

**Duration:** Weeks 3–6, approximately 40 hours.

**Domain:** Products, warehouses, inventory movements, orders, stock reservations, order cancellation, admin and employee roles.

**Week 3** — Controller-based REST API, HTTP methods and status codes, DTOs, manual mapping, validation, Swagger/OpenAPI, ProblemDetails, global error handling, pagination, filtering and sorting.

**Week 4** — EF Core relationships, transactions, optimistic concurrency, constraints, SQL indexes, query analysis, async database operations, `CancellationToken`, stock-reservation rules.

**Week 5** — Authentication versus authorization, JWT access token, refresh token, refresh-token rotation, role-based authorization, policy-based authorization, security failure cases.

**Week 6** — xUnit, mocking, unit testing, integration testing, `WebApplicationFactory`, Testcontainers, test-database isolation, GitHub Actions, API documentation, portfolio polish.

**At the end of Week 6, Berkan begins applying to junior .NET roles.**

---

## Phase 3 — FieldOps SaaS Modular Monolith

**Duration:** Weeks 7–12, approximately 60 hours.

**Modules:** Identity, Organizations, Employees, Customers, Work Orders, Scheduling, Attachments, Notifications, Reporting, Audit Logs.

**Week 7** — Modular-monolith boundaries, application services, domain rules, dependency direction, SOLID, clean code, architecture decision records.

**Week 8** — Multi-tenancy, tenant identification, tenant isolation, membership, granular RBAC, authorization tests, cross-tenant attack scenarios.

**Week 9** — Work-order lifecycle, assignment, scheduling, status transitions, file evidence, customer approval, business-rule tests.

**Week 10** — Redis, cache-aside, cache invalidation, background services, scheduled jobs, notification abstraction, audit logs, rate limiting, idempotency.

**Week 11** — Structured logging, correlation ID, health checks, configuration, environment management, Docker, Docker Compose, continuous integration.

**Week 12** — Integration and authorization testing, failure scenarios, AI provider abstraction, work-order note summarization, fake AI provider tests, documentation and demonstration.

**Completion gate:**
* Tenant data is isolated.
* Important authorization rules are tested.
* Application runs through Docker Compose.
* Redis and background processing solve documented problems.
* Logs and health endpoints support debugging.
* Project can be explained as a modular monolith.
* Project is added to the CV.

---

## Phase 4 — Distributed FieldOps

**Duration:** Weeks 13–17, approximately 50 hours.

**Services:** Main FieldOps API, Notification Service, Reporting Service, Search Service.

**Week 13** — Synchronous versus asynchronous communication, RabbitMQ, exchanges, queues, routing keys, producers and consumers, domain events, integration events.

**Week 14** — Outbox pattern, inbox pattern, idempotent consumers, retry, exponential backoff, dead-letter queues, duplicate-message handling, eventual consistency.

**Week 15** — Microservice boundaries, data ownership, notification-service extraction, reporting-service extraction, REST versus messaging decisions, independent deployment concepts.

**Week 16** — Elasticsearch, search indexing, index synchronization, rebuild strategy, SOAP concepts, SOAP client integration, anti-corruption layer.

**Week 17** — OpenTelemetry, distributed tracing, metrics, health checks, timeouts, resilience, failure simulation, multi-service Docker Compose.

---

## Phase 5 — Angular, Kubernetes and Cloud

**Duration:** Weeks 18–20, approximately 30 hours.

**Week 18** — Angular components, services, routing, reactive forms, HTTP client, JWT interceptor, role-aware screens, work-order dashboard. The goal is backend-integration competence, not frontend specialization.

**Week 19** — Multi-stage Dockerfiles, container images, Kubernetes Deployment, Service, ConfigMap, Secret, Ingress, liveness probe, readiness probe, basic autoscaling, local Kubernetes deployment.

**Week 20** — GitHub Actions delivery pipeline, container registry, cloud configuration, Azure deployment, database migration strategy, secret management, rollback, deployment verification, cloud logs and metrics.

---

## Phase 6 — Production Hardening

**Duration:** Weeks 21–22, approximately 20 hours.

**Week 21** — SQL query optimization, N+1 problems, indexes, cache measurement, load testing, latency, throughput, rate limiting, authentication attacks, OWASP review, security checklist.

**Week 22** — Repository cleanup, English READMEs, architecture diagrams, ADRs, API examples, CV bullet points, LinkedIn summaries, five-minute English demonstrations, C# interview questions, ASP.NET Core questions, SQL and EF Core questions, distributed-system questions, mock interviews.

---

## Daily two-hour structure

* 15 minutes — recall without AI
* 20 minutes — concept and problem explanation
* 70 minutes — implementation
* 10 minutes — testing/refactoring
* 5 minutes — learning log and evidence

## Weekly rhythm

* **Monday** — problem, concepts and vertical-slice design
* **Tuesday** — happy-path implementation
* **Wednesday** — persistence or infrastructure
* **Thursday** — tests, failures and production considerations
* **Friday** — refactor, full verification, documentation, demonstration and evidence

## Project completion standards

A project is considered complete only when:

* It builds and runs from a clean checkout.
* Its relevant automated tests pass.
* Its README (English) explains purpose, architecture, and how to run it.
* Berkan can explain its runtime and data flow without notes.
* Its phase completion gate (above) is satisfied.
* Its requirement evidence is recorded in `REQUIREMENTS_MATRIX.md`.

## Career checkpoints (summary)

* Week 2: Basic C# and ASP.NET Core flow
* Week 6: Junior .NET applications begin
* Week 12: Production-minded junior/junior+ competence
* Week 17: Distributed-system competence
* Week 20: Containers, Kubernetes and cloud evidence
* Week 22: Portfolio and interview readiness

**Reminder:** project work in this roadmap cannot replace or falsely claim "3+ years" or "5+ years" of professional experience.
