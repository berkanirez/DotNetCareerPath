# LEARNING_LOG.md — Session History

## Purpose

This log is the historical record of every completed learning session in the `DotNetCareerPath` roadmap. It exists so that progress, decisions, mistakes and understanding can be reviewed later — by Berkan, and by Claude at the start of future sessions.

## Rule

An entry is added **only after** a day's Definition of Done (see `CLAUDE.md`) has been fully satisfied. Do not log a session that is incomplete, abandoned, or only partially verified. If a session spans multiple days before completion, log it once, on the day it is actually completed.

## Entry template

Copy this template for each new entry:

```
### YYYY-MM-DD — Phase X, Week Y, Day Z

**Topic:**

**Problem solved:**

**What I learned:**

**What I implemented:**

**Runtime flow:**

**Verification:**

**Evidence:**

**Mistakes or difficulties:**

**Production considerations:**

**Understanding questions and answers:**

**Independent task:**

**Next session:**
```

## Entries

### 2026-08-25 — Phase 1, Week 1, Day 1

**Topic:** Environment verification; SDK vs runtime vs solution vs project; first ASP.NET Core MVC scaffold.

**Problem solved:** Establishing a working, verified .NET development baseline and an accurate mental model that maps .NET tooling concepts onto already-known Node.js/npm concepts, so later days build on correct foundations instead of guesswork.

**What I learned:**
* SDK contains the runtime plus build tooling (compiler, CLI, templates, NuGet client); the runtime alone can only execute already-built apps — parallel to a full Node.js+npm install vs. a bare `node` binary.
* A solution (`.slnx`) is an organizational shell for one or more projects, similar to an npm-workspaces root; a `.csproj` is the actual buildable unit, similar to `package.json`.
* The ASP.NET Core middleware pipeline runs in registration order and later middleware can depend on state set by earlier middleware — `UseRouting()` must precede `UseAuthorization()` because authorization needs the endpoint match that routing produces.
* MVC resolves `return View()` (no argument) by convention to `Views/{Controller}/{Action}.cshtml`, falling back to `Views/Shared/`.
* `Microsoft.NET.Sdk.Web` implicitly adds a FrameworkReference to the ASP.NET Core shared framework — not every .NET dependency is a downloadable NuGet package; some ship bundled with the installed runtime.

**What I implemented:**
* Initialized the Git repository (`git init`, no commit yet).
* Created the `RoadmapOS.slnx` solution.
* Scaffolded `src/RoadmapOS.Web` via `dotnet new mvc` and added it to the solution.
* Added `HomeController.Status()`, an action returning plain text via `Content(...)`.

**Runtime flow:** Browser request → Kestrel → middleware pipeline (exception handling in non-dev, HTTPS redirection, routing, authorization) → endpoint execution → controller action → for `Index()`, `View()` resolves and renders `Views/Home/Index.cshtml` through `_Layout.cshtml`; for `Status()`, `Content(...)` writes the response directly with no view involved.

**Verification:**
* `dotnet build` → 0 errors, 0 warnings.
* `dotnet run` + request to `/` → HTTP 200, correct page title.
* `dotnet run` + request to `/Home/Status` → HTTP 200, "RoadmapOS çalışıyor!".
* `git status` → `bin/` and `obj/` correctly excluded by `.gitignore`.

**Evidence:** Working endpoints (`/`, `/Home/Status`); English technical explanation (Q&A on middleware ordering, view-resolution convention, SDK FrameworkReference vs. NuGet PackageReference).

**Mistakes or difficulties:** None blocking. Noted that .NET 10's `dotnet new sln` defaults to the newer `.slnx` format instead of the classic `.sln`.

**Production considerations:** Verification used plain HTTP for simplicity — production requires enforced HTTPS with a real certificate; no secrets/config beyond framework defaults exist yet; no health-check endpoints yet; Bootstrap/jQuery are vendored directly into `wwwroot` by the template rather than through a real frontend build pipeline (out of scope until the Angular phase).

**Understanding questions and answers:**
1. Q: Why does `UseRouting()` have to come before `UseAuthorization()` and endpoint execution? A: `UseAuthorization()` inspects the endpoint that routing matched (e.g. its `[Authorize]` metadata); without a prior routing decision there is nothing to authorize against — same dependency-ordering logic as sequential Express middleware relying on state set by an earlier one.
2. Q: How does `return View()` find its `.cshtml` file with no name given? A: Convention over configuration — it looks for `Views/{CurrentController}/{CurrentAction}.cshtml`, then `Views/Shared/{CurrentAction}.cshtml`.
3. Q: How does the project build with zero `<PackageReference>` entries? A: `Microsoft.NET.Sdk.Web` implicitly adds a FrameworkReference to the ASP.NET Core shared framework, which ships with the installed runtime rather than being resolved as a NuGet package.

**Independent task:** Add a `Status()` action to `HomeController` returning plain text via `Content(...)`; verify manually at `/Home/Status`. Completed and independently verified by Berkan; re-verified by Claude (build clean, HTTP 200, correct body).

**Next session:** Phase 1, Week 1, Day 2 — C# type system (classes, records, interfaces, nullable reference types) and first domain concepts, modeled in memory only, no persistence yet.

### 2026-09-01 — Phase 1, Week 1, Day 2

**Topic:** C# type system — classes, records, interfaces, nullable reference types; first domain concepts, no persistence.

**Problem solved:** Turning the skill-tracking concept already used by hand in `docs/REQUIREMENTS_MATRIX.md` into real, correctly-behaving C# types, while building an accurate mental model of how C#'s class/record/interface/nullable features differ from the TypeScript equivalents already known from professional Node.js work.

**What I learned:** The identity-vs-value distinction that decides `class` vs `record` (an entity tracked by a stable `Id` through changes, vs. an immutable fact defined entirely by its content); that `record` gives structural equality, `ToString()`, and non-destructive copying (`with`) for free, none of which a `class` has by default; that `List<T>.Sort()` with no arguments requires `T` to implement `IComparable<T>`, and that the absence of it fails at runtime, not compile time, because `List<T>` carries no compile-time constraint on `T`; that nullable reference types (`string?`) are enforced by the compiler at the point of use (`CS8602`), not just at declaration.

**What I implemented:**
* `SkillLevel` enum (`src/RoadmapOS.Web/Domain/SkillLevel.cs`) — the five-level scale from `REQUIREMENTS_MATRIX.md` as a type-safe value.
* `Skill` class (`src/RoadmapOS.Web/Domain/Skill.cs`) — mutable entity with `Id`, `Name`, `Category`, `CurrentLevel`, `TargetLevel`, nullable `Notes`; implements `IComparable<Skill>` (sort by level, then by name); computed `IsAtTarget` property.
* `SkillSnapshot` record (`src/RoadmapOS.Web/Domain/SkillSnapshot.cs`) — immutable point-in-time value type.
* Temporary `Development`-only console verification block in `Program.cs`, deliberately used to trigger and then fix a `CS8602` nullable warning, and to prove sorting, computed properties, and record value equality all behave as designed.

**Runtime flow:** All in-memory, at application startup, before `app.Run()` — no HTTP request involved yet. See `docs/daily-code-notes/day-02.md` for the full line-by-line trace of the verification block against its console output.

**Verification:**
* `dotnet build` → 0 errors; 1 intentional `CS8602` warning observed, then 0 warnings after the fix.
* `dotnet run` → console output matched every predicted value: sort order (`EF Core` → `SQL Server` → `ASP.NET Core` → `C#`, level-then-name tie-break), `IsAtTarget=False` for all four, null-safe `Notes` length for both a populated (`15`) and a genuinely-null (`0`) case, and record equality (`False` for a modified copy, `True` for an unmodified `with { }` copy).

**Evidence:** Working, verified domain code; English/Turkish technical explanation (extensive Q&A on identity vs. value, `IComparable`, `with`, nullable reference types); independent task completed and re-verified.

**Mistakes or difficulties:** None blocking. Needed extra guidance to locate the remaining unexplained lines in `Skill.cs` (constructor, computed property, `CompareTo`) and to design the independent task around the fact that `Sort()` changes list order, so a dedicated variable (`sqlServerSkill`) was used instead of an index.

**Production considerations:** This is intentionally throwaway verification, not a substitute for real tests — it will be deleted on Day 3. Real domain behavior will be verified with xUnit starting Day 7. As of today, a new practice began: a per-day Turkish code walkthrough file (`docs/daily-code-notes/day-XX.md`), documented as a language-rule exception in `CLAUDE.md`.

**Understanding questions and answers:**
1. Q: Why does `SkillSnapshot` have no `Id`? A: It has no identity to track — it's an immutable fact, not a thing that changes over time; two snapshots with identical values *are* the same fact, so value equality is the correct default.
2. Q: What would happen if `Skill` didn't implement `IComparable<Skill>` and `Sort()` was still called? A: It compiles fine (no constraint on `List<T>.Sort()`'s type parameter), but throws `InvalidOperationException` at runtime the moment it needs to compare two elements.
3. Q: How would `snapshotToday with { }`'s effect be reproduced by hand on a `class`? A: Manually construct a new instance copying every field — and even then, `==` would return `False` by default (reference equality), unless `Equals`/`GetHashCode` were also manually overridden, which is exactly the boilerplate `record` generates automatically.

**Independent task:** Add a fourth `Skill` (`SQL Server`) with `Notes` left unset; print its null-safe `Notes` length separately from the existing line. Completed and independently verified by Berkan (console output confirmed `0`); re-verified by Claude by reading the final `Program.cs`.

**Next session:** Phase 1, Week 1, Day 3 — ASP.NET Core request lifecycle (`Program.cs`, middleware, routing, controllers, views, DI) and the first real read-only vertical slice, replacing today's temporary console block with a `SkillsController` + view over the in-memory `Skill` list.

### 2026-09-05 — Phase 1, Week 1, Day 3

**Topic:** ASP.NET Core request lifecycle, middleware, routing, controllers, views, dependency injection; first real read-only vertical slice.

**Problem solved:** Turning Day 2's throwaway console verification into a real, HTTP-reachable feature, while introducing dependency injection for a genuine reason: decoupling `SkillsController` from *where* skill data comes from, so that Day 4's move to SQL Server requires no controller changes at all.

**What I learned:** The distinction between controller lifetime (a new `SkillsController` per request, created by the framework's controller activator) and service lifetime (`AddSingleton` means one shared `InMemorySkillCatalog` instance across all requests, for the life of the app); why a method with "get" semantics (`GetAll()`) should return a defensive copy rather than mutate shared internal state in place — concretely, why sorting a singleton's backing list in place is a real concurrency hazard once multiple requests can run in parallel, not just a style preference; why depending on an interface (`ISkillCatalog`) rather than a concrete class (`InMemorySkillCatalog`) is what actually makes a future data-source swap (EF Core on Day 4) a one-line change in `Program.cs` instead of an edit to the controller.

**What I implemented:**
* `ISkillCatalog` interface (`GetAll()` contract) and `InMemorySkillCatalog` (`src/RoadmapOS.Web/Domain/`), registered as a singleton in `Program.cs`.
* Removed Day 2's temporary `Program.cs` console verification block entirely.
* `SkillsController` (`src/RoadmapOS.Web/Controllers/`) — constructor-injected `ISkillCatalog`, single `Index()` action.
* `Views/Skills/Index.cshtml` — strongly-typed view rendering a Bootstrap table of skills, plus a total-count line (`Model.Count`) added as the independent task.
* `Views/_ViewImports.cshtml` updated to import `RoadmapOS.Web.Domain` for all views.

**Runtime flow:** `GET /Skills` → Kestrel → middleware pipeline (routing, authorization) → convention-based routing matches `SkillsController.Index()` → the DI container constructs `SkillsController`, resolving `ISkillCatalog` to the registered singleton `InMemorySkillCatalog` → `Index()` calls `GetAll()` (returns a sorted copy) → `View(skills)` resolves `Views/Skills/Index.cshtml` by convention → the view renders the table inside `_Layout.cshtml` → HTML returned. Full trace with code in `docs/daily-code-notes/day-03.md`.

**Verification:**
* `dotnet build` → 0 errors, 0 warnings.
* `GET /` → HTTP 200 (no regression).
* `GET /Skills` → HTTP 200, table in the exact expected sort order (`EF Core → SQL Server → ASP.NET Core → C#`), matching Day 2's `IComparable<Skill>` behavior, now proven inside a real HTTP request.
* Independent task verified: "Toplam 4 skill." rendered correctly above the table.

**Evidence:** Working endpoint (`/Skills`); English/Turkish technical explanation (DI lifetime, defensive copying under a singleton, interface vs. concrete-type injection); independent task completed and re-verified.

**Mistakes or difficulties:** None blocking. Needed a deeper explanation of *why* `GetAll()` copies before sorting — the concurrency angle (shared singleton state under parallel requests) wasn't obvious from "don't mutate the source," and needed to be distinguished explicitly from the unrelated EF Core change-tracking concern that will come up on Day 4.

**Production considerations:** `InMemorySkillCatalog` and `AddSingleton` are today-only choices, tied to data being constant and held in memory. Day 4 replaces the singleton in-memory catalog with a scoped, EF Core-backed one — `SkillsController` will not change. No error handling exists yet for a missing/broken data source, since today's source can't fail.

**Understanding questions and answers:**
1. Q: Who creates `SkillsController`, and when? A: ASP.NET Core's controller activator, once per incoming HTTP request that routes to it — not us, and not once per app lifetime.
2. Q: Why copy `_skills` before sorting in `GetAll()`? A: `InMemorySkillCatalog` is a singleton, so `_skills` is shared across all concurrent requests; sorting it in place risks a race condition (concurrent read+mutate on a non-thread-safe `List<T>`) under parallel requests — copying makes each `GetAll()` call self-contained. This is unrelated to EF Core's change-tracking/`SaveChanges()` mechanism, which will be the relevant concern on Day 4 instead.
3. Q: What would injecting the concrete `InMemorySkillCatalog` directly (no interface) have cost us? A: Nothing today, but swapping to an EF Core-backed catalog on Day 4 would then require editing `SkillsController`'s constructor signature itself, instead of changing one registration line in `Program.cs` — the controller would be coupled to a specific data-access implementation instead of an abstraction.

**Independent task:** Add a total skill count line (`<p>Toplam @Model.Count skill.</p>`) above the table in `Views/Skills/Index.cshtml`. Completed and independently verified by Berkan; re-verified by Claude (`/Skills` shows "Toplam 4 skill.").

**Next session:** Phase 1, Week 1, Day 4 — SQL Server, EF Core, `DbContext`, entities, migrations; persist and retrieve the first `Skill` record, replacing `InMemorySkillCatalog` with an EF Core-backed `ISkillCatalog` implementation.

### 2026-09-05 — Phase 1, Week 1, Day 4

**Topic:** SQL Server, Entity Framework Core, `DbContext`, entities, migrations; persisting and retrieving the first real record.

**Problem solved:** Replacing Day 3's in-memory data with genuine, durable persistence in SQL Server, using the `ISkillCatalog` abstraction from Day 3 so the swap required zero changes to `SkillsController`.

**What I learned:** Why `AddDbContext`'s default `Scoped` lifetime is not just a style choice but a correctness requirement — `DbContext` holds a live connection and a mutable change tracker, neither of which is thread-safe, so sharing one instance across concurrent requests (as a `Singleton` would) risks both crashes (`InvalidOperationException` from concurrent operations on one context) and silent correctness bugs (one request's uncommitted in-memory changes leaking into another's read via the identity map) — a categorically bigger risk than Day 3's `List<T>.Sort()` concurrency concern, which is why `InMemorySkillCatalog` correctly stayed a `Singleton` while `EfSkillCatalog` needed `Scoped`; how EF Core derives `NOT NULL` vs `NULL` migration columns directly from the C# nullable reference type annotations already written in `Skill.cs` (via compiler-emitted metadata read through reflection during model building) rather than needing separate configuration; that a real environment blocker (no SQL Server engine installed, only client tooling) needs to be surfaced and resolved before continuing, not worked around silently with a different database.

**What I implemented:**
* Installed SQL Server 2022 Express locally (`localhost\SQLEXPRESS`) after two failed `winget` attempts (a Turkish-locale install prompt breaking unattended mode) — resolved via manual installer download.
* Added `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.EntityFrameworkCore.Design` packages.
* `RoadmapOSDbContext` (`src/RoadmapOS.Web/Data/`) with `DbSet<Skill> Skills`.
* `EfSkillCatalog : ISkillCatalog` (`src/RoadmapOS.Web/Data/`) — queries via `_context.Skills.ToList()`, then reuses the existing `IComparable<Skill>` sort.
* `InitialCreate` migration generated and applied (`dotnet ef migrations add` / `dotnet ef database update`); `RoadmapOS` database and `Skills` table created in real SQL Server.
* `Program.cs`: `AddDbContext<RoadmapOSDbContext>` (reading the connection string from `appsettings.Development.json`), DI registration switched to `AddScoped<ISkillCatalog, EfSkillCatalog>()`; temporary Development-only seed block (placeholder until Day 9).
* `InMemorySkillCatalog` kept in the codebase (unregistered) as a future test double for Day 7.

**Runtime flow:** `GET /Skills` → `SkillsController.Index()` (unchanged since Day 3) → DI now resolves `ISkillCatalog` to `EfSkillCatalog` → `_context.Skills.ToList()` translates to a real `SELECT` against SQL Server → results mapped back to `Skill` objects → sorted → same view as before. Full trace in `docs/daily-code-notes/day-04.md`.

**Verification:**
* `dotnet build` → 0 errors, 0 warnings.
* `dotnet ef migrations add` / `dotnet ef database update` → `RoadmapOS` database and `Skills` table created, confirmed via direct SQL query independent of the running app.
* `GET /Skills` → HTTP 200, 4 seeded skills in correct sort order, "Toplam 4 skill."
* App stopped entirely, then queried the database directly via raw SQL — all 4 rows still present, proving real persistence (unlike Day 2–3's in-memory data, which vanished on restart).
* Independent task: a 5th skill (`Git`) inserted directly via SSMS/raw SQL (bypassing the app entirely); `/Skills` correctly showed 5 skills with `Git` placed via the level-then-name tie-break rule, next to `C#`.

**Evidence:** Working endpoint (`/Skills`) now backed by a real database; commit (`4fec28a`); English/Turkish technical explanation (DbContext lifetime and thread-safety, nullable-to-NOT-NULL derivation, DI-enabled swap); independent task completed and re-verified.

**Mistakes or difficulties:** SQL Server had to be installed from scratch — no engine was present, only SSMS (a client tool, mistaken at first for the engine itself) and ODBC/OLEDB drivers. `winget`'s unattended install failed twice due to an unhandled Turkish-locale prompt; resolved by downloading the installer manually and answering the prompt interactively. This was treated as a real blocker and surfaced explicitly rather than silently substituting a different database, per `CLAUDE.md`'s engineering rules.

**Production considerations:** Connection string is plaintext in `appsettings.Development.json` — acceptable today only because it carries no secret (Windows Authentication, no password); production requires proper secret management. The seed block is an explicit placeholder, not a real seeding strategy (Day 9). `TrustServerCertificate=True` is a local-dev-only simplification.

**Understanding questions and answers:**
1. Q: Why does `DbContext` need `Scoped` where `InMemorySkillCatalog` was fine as `Singleton`? A: `DbContext` holds a live, non-thread-safe connection and change tracker that must not be shared across concurrent requests — sharing it risks both a documented runtime exception (`InvalidOperationException`) and silent cross-request data leakage via the identity map, a materially bigger and different risk than a plain list's sort-while-reading race.
2. Q: Where did EF Core get the `NOT NULL`/`NULL` decision per column? A: From the C# nullable reference type annotation (`string` vs `string?`) already on each `Skill` property, read via compiler-emitted metadata during EF Core's model-building step — changing the `?` would change the generated migration.
3. Q: What Day 3 decision made the `InMemorySkillCatalog` → `EfSkillCatalog` swap require zero changes to `SkillsController`? A: Injecting the `ISkillCatalog` interface into the controller's constructor rather than a concrete class.

**Independent task:** Insert a 5th skill (`Git`) directly into the `Skills` table via SSMS/raw SQL (bypassing the app entirely), then verify it appears correctly sorted at `/Skills`. Completed and independently verified by Berkan; re-verified by Claude via both a direct SQL query and the running app.

**Next session:** Phase 1, Week 1, Day 5 — model binding, validation, and a `Skill` create/edit flow with error display, manually verified.
