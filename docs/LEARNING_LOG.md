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

### 2026-09-05 — Phase 1, Week 1, Day 5

**Topic:** Model binding, validation, `Skill` create/edit flow, error display, manual verification.

**Problem solved:** Letting the app itself write data (not just SSMS or seed code), safely — turning raw HTTP form input into validated domain objects without letting the form dictate what the domain entity looks like or which of its fields can be set from the outside.

**What I learned:** Why binding a form directly to a domain entity (`Skill`) instead of a dedicated DTO (`SkillFormModel`) is a real over-posting risk, not a theoretical one — any public-settable property becomes fair game for a raw POST request even if the rendered form never exposed an input for it; the concrete mechanics of `[ValidateAntiForgeryToken]` — confirmed live that removing/lacking a valid token yields HTTP 400 when the attribute is present, versus silent, unprotected success if the attribute were absent entirely; why `EfSkillCatalog.Update()` still needs an explicit `SaveChanges()` call despite the entity already being tracked (there's a real, separate I/O step to the database still pending) while `InMemorySkillCatalog.Update()` needs literally nothing (the "storage" and the in-memory object are the same reference, so there is no separate step at all).

**What I implemented:**
* `ISkillCatalog` extended with `GetById(int id)`, `Add(Skill skill)`, `Update(Skill skill)`.
* `EfSkillCatalog` and `InMemorySkillCatalog` both updated to implement the extended interface (the latter kept purely for future Day 7 test-double use, still unregistered).
* `Models/SkillFormModel.cs` — a validation-attributed DTO (`[Required]`, `[StringLength]`) separate from `Skill`.
* `SkillsController.Create()` (GET/POST) and `Edit(int id)` (GET/POST), both POST actions carrying `[ValidateAntiForgeryToken]`.
* `Views/Skills/Create.cshtml` and `Edit.cshtml` — tag-helper-driven forms (`asp-for`, `asp-validation-for`, `asp-validation-summary`, `Html.GetEnumSelectList<SkillLevel>()`) with client + server validation wired via `_ValidationScriptsPartial`.
* `Views/Skills/Index.cshtml` linked to both new flows ("New Skill", per-row "Edit").

**Runtime flow:** Full trace (GET form → POST → model binding → `ModelState` validation → either re-render with errors or persist + redirect) documented in `docs/daily-code-notes/day-05.md`.

**Verification:**
* `dotnet build` → 0 errors, 0 warnings.
* Live curl test, invalid data (empty `Name`) → HTTP 200 (no redirect), "The Name field is required" rendered.
* Live curl test, valid data (`Docker`) → HTTP 302 → `/Skills`, new row present ("Toplam 6 skill.").
* Live curl test, Edit with a `Notes` change → HTTP 302; app stopped, direct SQL query confirmed the change persisted.
* Live curl test, POST with no anti-forgery token at all → HTTP 400, confirmed exactly as predicted.
* Test-only `Docker` row removed afterward to keep the database at the expected state.
* Independent task: edited `Git`'s `CurrentLevel` to equal its `TargetLevel` via the real Edit form; `/Skills` correctly showed "Yes" for that row, confirming Day 2's `IsAtTarget` computed property still works correctly end to end through a real form submission.

**Evidence:** Working endpoints (`/Skills/Create`, `/Skills/Edit/{id}`); commit (`50482af`); English/Turkish technical explanation (over-posting risk, CSRF mechanics, EF Core change tracking vs. plain in-memory mutation); independent task completed and re-verified.

**Mistakes or difficulties:** None blocking. The anti-forgery mechanics needed a live demonstration (not just an explanation) to land clearly — worth repeating this pattern (predict, then prove with a real request) for future security-adjacent topics.

**Production considerations:** `[StringLength(100)]` etc. are application-level checks only — no matching database constraint exists yet (deferred to Day 6 on purpose). No delete flow exists (out of today's scope). `Create.cshtml`/`Edit.cshtml` intentionally duplicate markup, to be refactored on Day 10.

**Understanding questions and answers:**
1. Q: Concrete over-posting scenario if `Skill` were bound directly? A: A raw POST (bypassing the rendered form) adding a field the UI never exposed — e.g. a future `VerifiedByMentor`-style flag with a public setter — would be silently accepted by the model binder purely because the field name matches a property, regardless of whether the real form ever rendered an input for it.
2. Q: HTTP status without a valid anti-forgery token? A: 400 Bad Request when `[ValidateAntiForgeryToken]` is present (confirmed live); if the attribute were removed entirely, the request would instead succeed normally with no protection at all.
3. Q: Why are both `Update()` methods "empty" but for different reasons? A: EF Core's version still has a real, pending I/O step to flush to SQL Server (`SaveChanges()`), even though the entity is already tracked; the in-memory version has no separate storage layer at all — the object returned by `GetById` *is* the stored object, so mutating it already *is* the update.

**Independent task:** Using the real Edit form, set a skill's `CurrentLevel` equal to its `TargetLevel` and confirm `/Skills` shows "Yes" under "At Target?" for that row. Completed and independently verified by Berkan (`Git`, both levels set to `CanImplementIndependently`); re-verified by Claude via direct SQL query and the running app.

**Next session:** Phase 1, Week 2, Day 6 — roadmap/phase/project/milestone relationships, EF Core relationships, database constraints.

### 2026-09-05 — Phase 1, Week 2, Day 6

**Topic:** Roadmap/phase/project/milestone relationships, EF Core relationships, database constraints.

**Problem solved:** Moving from a single, unrelated entity (`Skill`) to a genuinely related data model — `RoadmapPhase (1) → Project (*) → Milestone (*)`, mirroring the very roadmap this app is meant to track — and making sure referential integrity and length constraints are enforced by the database itself, not just by application code, closing the gap explicitly left open on Day 4/5.

**What I learned:** EF Core's foreign key naming convention (`<navigation>Id` or `<principal type>Id`) and why a differently-named property would require explicit Fluent API configuration; the precise mechanics of `DeleteBehavior.Restrict` vs `Cascade` — Restrict doesn't "warn," it makes the SQL Server `DELETE` statement itself fail outright with a foreign key violation (the same underlying mechanism as an invalid-FK `INSERT` failing, just triggered from the opposite direction); why DTO-level validation (`[StringLength]`) and EF Core/DB-level constraints (`HasMaxLength`) are not redundant but serve different, complementary purposes (UX/fail-fast vs. data-integrity-guaranteed-regardless-of-entry-point) — proven concretely by running a raw SQL insert that bypassed the DTO entirely and was still correctly rejected by the database.

**What I implemented:**
* `RoadmapPhase`, `Project`, `Milestone` entities (`src/RoadmapOS.Web/Domain/`) with collection/reference navigation properties.
* `RoadmapOSDbContext.OnModelCreating`: `HasMaxLength`/`IsRequired` for all string properties (including `Skill`'s previously-deferred constraints), explicit `HasOne`/`WithMany`/`HasForeignKey`/`OnDelete(Cascade)` for both relationships.
* `AddPhaseProjectMilestone` migration, applied to the real database.
* Seed block extended with real roadmap data reflecting actual progress (self-referential: the app now tracks its own Day 1–10 milestones).

**Runtime flow:** No HTTP-facing changes today — pure EF Core model configuration, migration, and startup-time seeding. Full trace in `docs/daily-code-notes/day-06.md`.

**Verification:**
* `dotnet build` → 0 errors, 0 warnings.
* Migration applied — three new tables, two foreign keys (with cascade), two auto-generated indexes confirmed via direct SQL query.
* Seed data verified correct and correctly linked (`RoadmapPhaseId`/`ProjectId` matching).
* Live constraint tests, run directly against SQL Server (bypassing the app entirely): invalid FK insert → rejected (`FOREIGN KEY constraint` violation); 200-character `Name` insert (limit 150) → rejected (`String or binary data would be truncated`); deleting a populated `RoadmapPhase` → cascade removed its `Project` and all 3 `Milestone` rows; app restarted → seed block detected the empty table and recreated the data automatically.
* Independent task: added a second `Project` under the existing phase via SSMS, then personally triggered and observed a rejected `Milestone` insert with an invalid `ProjectId`.

**Evidence:** Real, verified database constraints (working migration + live rejection tests); commit (`66445ed`); English/Turkish technical explanation (FK convention, Restrict vs. Cascade mechanics, DTO-vs-DB validation layering); independent task completed and re-verified.

**Mistakes or difficulties:** None blocking. Seeded rows' identity values are no longer `1`/`1`/`1,2,3` after repeated cascade-delete-and-reseed cycles during testing — expected SQL Server IDENTITY behavior (gaps after deletes), not a bug, and data remained fully consistent throughout.

**Production considerations:** `OnDelete(Cascade)` fits a single-user learning app; a multi-tenant production system would more often default to `Restrict` to prevent accidental bulk data loss. No controller/view exists yet for these three entities — deliberately out of today's scope (not requested by the roadmap for Day 6).

**Understanding questions and answers:**
1. Q: Would `RelatedProjectId` still be auto-detected as a foreign key by EF Core? A: No — it matches neither the `<navigation>Id` nor `<principal-type>Id` convention, so it would need explicit `HasForeignKey` configuration.
2. Q: What exactly happens with `Restrict` when deleting a populated `RoadmapPhase`? A: The `DELETE` statement itself is rejected by SQL Server with a foreign key violation — not a warning, an outright failure; nothing is deleted.
3. Q: Which live test proved DTO-only validation would be insufficient? A: The 200-character `Name` raw SQL insert — it never touched `SkillFormModel`/`ModelState` at all, yet was still correctly rejected, because the enforcement came from `HasMaxLength` at the database level.

**Independent task:** Add a second `Project` under the existing seeded `RoadmapPhase` via SSMS, then deliberately attempt a `Milestone` insert with an invalid `ProjectId` and observe the rejection firsthand. Completed and independently verified by Berkan; re-verified by Claude via direct SQL query (final state: 1 phase, 1 project, 3 milestones, correctly linked).

**Next session:** Phase 1, Week 2, Day 7 — progress-calculation rules, a domain service, and the first TDD workflow with xUnit.

### 2026-09-05 — Phase 1, Week 2, Day 7

**Topic:** Progress-calculation rules, a domain service, the first TDD workflow, xUnit.

**Problem solved:** Replacing slow, manual verification (curl, SSMS queries) with fast, automated, repeatable tests for a genuine piece of business logic (overall progress calculation), written test-first rather than tested after the fact.

**What I learned:** The mechanics of a unit test from first principles — Arrange/Act/Assert, what `[Fact]` actually does (marks a method for the xUnit runner to discover and execute), and precisely what `Assert.Equal(expected, actual)` does (a plain comparison that throws — and is caught by the runner — when the two don't match; note the argument order is reversed from Jest's `expect(actual).toBe(expected)`); why `ProgressCalculator` deliberately has no interface, unlike `ISkillCatalog` — no second implementation will ever exist, so an interface here would be an unjustified abstraction; that `dotnet test` must be run from the solution root (or the test project itself) — running it from `src/RoadmapOS.Web` silently builds but discovers zero tests, since that project carries no test SDK reference; that `Math.Min(100, x)` is a plain "clamp to a ceiling" idiom, not special syntax.

**What I implemented:**
* `tests/RoadmapOS.Web.Tests` xUnit project, added to the solution, referencing `RoadmapOS.Web`.
* `Domain/ProgressCalculator.cs` — genuinely built test-first: started as `throw new NotImplementedException()`, watched two tests fail for real, then implemented the real formula (sum of `CurrentLevel` over sum of `TargetLevel`, as a percentage), watched them pass.
* Four more tests covering edge cases: empty list (÷0 guard), multiple skills (weighted sum, not averaged per-skill), over-target clamping via `Math.Min`, and all-targets-zero (÷0 guard again, this one passed on first try since the guard already existed — a genuine "the code was already correct for a case I hadn't tested yet" moment).

**Runtime flow:** No HTTP/DB involved — `dotnet test` discovers and runs all `[Fact]`-marked methods in the test project directly against the compiled `RoadmapOS.Web` assembly. Full Red→Green transcript in `docs/daily-code-notes/day-07.md`.

**Verification:**
* `dotnet test` (from repo root) → 6/6 passing, ~30ms total, no server or database involved.
* `dotnet build` (full solution) → 0 errors, 0 warnings.
* Confirmed, live, that running `dotnet test` from the wrong directory (`src/RoadmapOS.Web`) produces no test results at all — just a build.
* Independent task: added a test for a skill with `TargetLevel=NotStudied` mixed with a normal skill; hand-calculated the expected result (100) before running, matched exactly — and surfaced a genuinely non-obvious rule: a targetless skill's `CurrentLevel` still inflates the overall percentage without being checked against anything, even though the other skill in the same list is only at ~33% of its own target.

**Evidence:** Automated, passing test suite (`dotnet test`); commit (pending — see below); English/Turkish technical explanation of Assert/Fact/TDD mechanics (needed a second, slower pass after initial confusion); independent task completed correctly and re-verified.

**Mistakes or difficulties:** The first explanation of xUnit/`Assert.Equal`/`Math.Min` assumed too much — needed a full first-principles re-explanation (what a test actually does, argument order, what `Math.Min` computes) before the independent task could be attempted meaningfully. Worth front-loading this level of detail earlier next time a brand-new syntax/library is introduced. Also hit real friction running `dotnet test` from the wrong project directory — a good, concrete lesson about solution vs. project scope for CLI commands.

**Production considerations:** These are real, permanent tests — not a "demo" pattern, this is exactly how it's done in production. No UI consumes `ProgressCalculator` yet (deliberately, Day 8's job).

**Understanding questions and answers:**
1. Q: Why no interface for `ProgressCalculator`, unlike `ISkillCatalog`? A: `ISkillCatalog` has two real, alternating implementations (in-memory/EF Core); `ProgressCalculator` has and will have exactly one — an interface would be an unjustified abstraction.
2. Q: What does seeing a test fail before the implementation exists actually prove? A: That the test is capable of failing at all — a test that has never been observed to fail could be silently useless (e.g., asserting something trivially true regardless of the code).
3. Q: Which test would catch removing `Math.Min(100, ...)`? A: `CalculateOverallProgress_SkillExceedsTarget_ClampsAtHundred`, which asserts `100` where the unclamped raw math produces `400`.

**Independent task:** Write a new test for a skill with `TargetLevel=NotStudied` mixed with a normal skill; predict the result by hand before running. Completed and independently verified by Berkan (predicted and got `100`, correctly identified the running location issue himself); re-verified by Claude (6/6 passing from repo root).

**Next session:** Phase 1, Week 2, Day 8 — LINQ, dashboard queries, overall and category progress, wiring `ProgressCalculator` into a real controller/view.

### 2026-09-12 — Phase 1, Week 2, Day 8

**Topic:** LINQ, dashboard queries, overall and category progress.

**Problem solved:** Giving `ProgressCalculator` (built test-first on Day 7, never actually shown to a user) a real, visible consumer — a dashboard displaying both an overall progress bar and a per-category breakdown — using LINQ's `GroupBy`/`Select` instead of hand-rolled grouping logic.

**What I learned:** That `GroupBy`'s `Key` is not something authored per-group — it's a real property on the `IGrouping<TKey, TSource>` type LINQ produces, populated automatically from evaluating the grouping lambda (`skill => skill.Category`) against the actual data; groups are discovered from whatever distinct values exist at runtime, so a brand-new category needs zero code changes to appear. Confirmed this concretely: adding a `Testing`-category skill made a new dashboard row appear automatically. Also hit and fixed a real, non-obvious production bug: the server's `tr-TR` culture made `double.ToString()` emit a comma decimal separator, which silently broke inline CSS `width` values (commas aren't valid in CSS numbers) — fixed via `CultureInfo.InvariantCulture`, a case where the *calculation* was correct but the *rendering* wasn't, and only became visible by inspecting real HTML output, not by reasoning about the code.

**What I implemented:**
* `Domain/CategoryProgress.cs` — a small record pairing a category name with its percentage.
* `ProgressCalculator.CalculateCategoryProgress`, built test-first (2 new tests, both genuinely red before green): groups skills by `Category`, reuses `CalculateOverallProgress` per group rather than duplicating its edge-case handling.
* `ProgressCalculator` registered in DI (`AddSingleton`) for the first time — justified today by `DashboardController` being a real consumer, not registered speculatively on Day 7.
* `Models/DashboardViewModel.cs`, `Controllers/DashboardController.cs`, `Views/Dashboard/Index.cshtml` — a new `/Dashboard` page with Bootstrap progress bars for overall and per-category progress.
* `_Layout.cshtml` nav updated with "Dashboard" and "Skills" links (the latter had been missing since Day 3).

**Runtime flow:** `GET /Dashboard` → `DashboardController.Index()` → `ISkillCatalog.GetAll()` → `ProgressCalculator.CalculateOverallProgress` and `CalculateCategoryProgress` (the latter internally reusing the former per LINQ-grouped subset) → `DashboardViewModel` → view renders two progress-bar sections. Full trace, including the culture bug and fix, in `docs/daily-code-notes/day-08.md`.

**Verification:**
* `dotnet test` → 8/8 passing.
* `dotnet build` → 0 errors, 0 warnings.
* Hand-calculated expected percentages from live `Skills` data, then confirmed `/Dashboard`'s actual rendered output matched exactly (both before and after the culture fix, catching the comma-vs-period bug directly in the HTML).
* `GET /`, `GET /Skills` → HTTP 200 (no regression).
* Independent task: added a new skill (`xUnit`, category `Testing`) via the Create form; `/Dashboard` automatically showed a correctly calculated "Testing" row with zero code changes.

**Evidence:** Working endpoint (`/Dashboard`); passing automated tests; commit (pending); English/Turkish technical explanation (needed a second, more concrete pass on `GroupBy`/`Key` mechanics using the app's actual data before it landed); independent task completed and re-verified.

**Mistakes or difficulties:** Initial explanation of `group.Key`/`GroupBy` was too abstract and didn't land — a concrete walkthrough using the app's real skill/category data (rather than generic examples) was needed. This reinforces a pattern from Day 7: front-load concrete, data-grounded examples for new LINQ/testing syntax rather than assuming the abstract explanation is sufficient the first time.

**Production considerations:** The `CultureInfo.InvariantCulture` fix is a genuinely permanent, production-correct pattern (never format machine-readable values like CSS/JSON/URLs using the server's ambient culture) — not a temporary simplification. `ProgressCalculator` still has no interface; still no real need for one.

**Understanding questions and answers:**
1. Q: Where does `group.Key` actually come from? A: It's a real property on the `IGrouping<TKey,TSource>` type LINQ produces, set automatically to the value produced by the grouping lambda (`skill.Category`, not `skill.Name`) for that group's members — groups are discovered from the data's actual distinct values, never predefined.
2. Q: Why does `CalculateCategoryProgress` call `CalculateOverallProgress` again per group instead of writing separate logic? A: A category's progress *is* the same overall-progress formula applied to a smaller subset — duplicating it would duplicate its edge-case handling (÷0 guard, 100% clamp) in two places that could silently drift apart.
3. Q: Why register `ProgressCalculator` in DI today but not on Day 7? A: Day 7 had no real application-code consumer (only tests, which don't need DI); today `DashboardController` genuinely needs one via constructor injection — the registration followed the real need rather than anticipating it.

**Independent task:** Add a new skill in a brand-new category via the real Create form, hand-calculate its expected dashboard percentage first, then confirm `/Dashboard` shows it correctly with no code changes. Completed and independently verified by Berkan (`xUnit`/`Testing`, 25%); re-verified by Claude via direct SQL query and the running app.

**Next session:** Phase 1, Week 2, Day 9 — requirement mapping, evidence records, logging, seed data, basic error handling.

### 2026-09-12 — Phase 1, Week 2, Day 9

**Topic:** Requirement mapping, evidence records, logging, seed data, basic error handling.

**Problem solved:** Cleaning up ad-hoc, inline seed logic scattered in `Program.cs`; modeling `REQUIREMENTS_MATRIX.md`'s manual "Level 3/4 requires evidence" rule as a real, queryable `Evidence` entity inside the app itself; adding real observability (logging) and verifying error-handling paths that had existed unverified since Day 5.

**What I learned:** The concrete, practical reason to prefer a runtime idempotent seeder over EF Core's migration-baked `HasData()` — not a style preference, but because this specific database already holds real, organically-created data (added/edited through the app by Berkan), and `HasData`'s fixed IDs risk colliding with it; that EF Core never populates a navigation property (`Skill.EvidenceRecords`) unless `.Include()` is used, confirmed directly by inspecting the actual generated SQL (with vs. without a `LEFT JOIN`); the practical difference between `LogInformation` (normal, expected events) and `LogWarning` (non-fatal but noteworthy, worth a human's attention); and — importantly — that a new domain concept (`Evidence`) can exist purely as a display/tracking mechanism today without yet being enforced by any business rule, and that distinction (modeled vs. enforced) is worth stating explicitly rather than leaving implied.

**What I implemented:**
* `Data/DbSeeder.cs` — seed logic extracted from `Program.cs` into one dedicated, documented class (`SeedSkills`, `SeedRoadmap`, `SeedEvidence`), each idempotent (`if (...Any()) return;`).
* `Domain/Evidence.cs` + `Skill.EvidenceRecords` navigation — same relationship pattern as Day 6's `Project`/`Milestone`, applied to a new concept.
* `RoadmapOSDbContext`: new `DbSet<Evidence>`, Fluent API config (`HasMaxLength`, `HasOne`/`WithMany`/`OnDelete(Cascade)`); `AddEvidence` migration created and applied.
* `EfSkillCatalog.GetAll()` updated with `.Include(s => s.EvidenceRecords)`.
* `Views/Skills/Index.cshtml` — new "Evidence" column (count).
* `SkillsController`: `ILogger<SkillsController>` injected; `LogInformation` on successful Create/Edit, `LogWarning` when `Edit` can't find the requested skill.

**Runtime flow:** `GET /Skills` now issues a single query with a `LEFT JOIN` (confirmed in console output) instead of a plain `SELECT`, thanks to `.Include()`. `Edit` on a missing ID logs a warning before returning 404. Full trace in `docs/daily-code-notes/day-09.md`.

**Verification:**
* `dotnet build` → 0 errors, 0 warnings. `dotnet test` → 8/8 passing (no regression from unrelated changes).
* `GET /Skills` → Evidence counts correct per seeded data (C#: 1, EF Core: 1, others: 0).
* `GET /Skills/Edit/9999` → HTTP 404, confirmed alongside the exact `LogWarning` line appearing in the console — the first time this Day-5-era code path was actually exercised rather than just read.
* Read (not triggered) the existing `UseExceptionHandler("/Home/Error")`/`UseHsts()` block from Day 1's scaffold and explained what it does in Production vs. Development, without forcing a live crash against the real database.
* Independent task: inserted a new `Evidence` row directly via SSMS against `Git`; `/Skills` immediately reflected the updated count with zero code changes.

**Evidence:** Working, verified feature (`Evidence` tracking, logging, 404 path); commit (`5f3963d`); English/Turkish technical explanation (seed strategy trade-off, `.Include()` mechanics, log-level semantics, and — after a follow-up question — the actual purpose of `Evidence` beyond its syntax); independent task completed and re-verified.

**Mistakes or difficulties:** The first pass explained *how* `Evidence` works but not *why it exists* — a good reminder that syntax fluency (which landed fine, per correct answers to the understanding questions) doesn't automatically carry the "real problem being solved" framing; that needs to be stated explicitly, not assumed to follow from the mechanics.

**Production considerations:** `Evidence` is display-only today — no rule yet enforces "Level 3/4 requires at least one Evidence record" (a plausible future enhancement, out of today's scope). The `HasData` vs. runtime-seeder decision is specific to this database's current state, not a universal rule — a brand-new project without organic data might reasonably choose `HasData` instead.

**Understanding questions and answers:**
1. Q: Why still a runtime seeder instead of `HasData`? A: `HasData` bakes fixed IDs into a migration; this database already holds real, organically-added data, so fixed IDs risk colliding with it.
2. Q: What would the Evidence count show without `.Include()`? A: Zero for every skill — related data doesn't populate unless explicitly included, confirmed via the actual SQL generated (no `LEFT JOIN` without it).
3. Q: `LogInformation` vs. `LogWarning`? A: Information records normal, successful events; Warning flags non-fatal but noteworthy situations meant to catch a human's attention.

**Independent task:** Insert a new `Evidence` row directly via SSMS for an existing skill (`Git`), then confirm `/Skills` shows the updated count with no code changes. Completed and independently verified by Berkan; re-verified by Claude via direct SQL query.

**Next session:** Phase 1, Week 2, Day 10 (final RoadmapOS day) — refactoring, full build/test verification, English README, demonstration, RoadmapOS V1 release.

### 2026-09-13 — Phase 1, Week 2, Day 10 (RoadmapOS V1 release)

**Topic:** Refactoring, build/test verification, English README, demonstration, RoadmapOS V1 release.

**Problem solved:** Closing out Phase 1 honestly — not just adding one more feature, but going back over 9 days of code to remove known debt (the Day-5 form duplication), prove the whole solution still builds and works from a clean checkout, give the repository a README someone else could actually follow, and check `docs/ROADMAP.md`'s Phase 1 completion gate item by item rather than assuming it was satisfied along the way.

**What I learned/reinforced:** That a refactor is verified by proving behavior is *unchanged* (re-ran the exact same valid/invalid submission and pre-filled-edit checks from Day 5, got identical results), not just by the code compiling; that "builds on my machine" and "builds from a clean checkout" are different claims, and only deleting `bin`/`obj` and rebuilding actually tests the second one; that two of the seven completion-gate items (explaining the MVC request lifecycle; explaining DI and EF Core) could only be satisfied by Berkan's own explanation, not by any file Claude could point to — and that exercise surfaced one genuine misconception worth correcting: DI does not remove a dependency, it redirects it (to an abstraction, supplied from outside) rather than eliminating it.

**What I implemented:**
* `Views/Skills/_SkillForm.cshtml` — the shared form fields extracted from `Create.cshtml`/`Edit.cshtml`; each of those keeps only its own `<form>` wrapper (differing action/hidden `Id`).
* Clean-build verification: deleted `bin`/`obj` in both projects, rebuilt from scratch (0 errors/warnings), reran the full test suite (8/8).
* Full end-to-end manual walkthrough of every route: `/`, `/Home/Privacy`, `/Skills`, `/Skills/Create`, `/Skills/Edit/1`, `/Dashboard`.
* `README.md`: corrected the long-stale "Current status" section (unchanged since Day 0), added a real "how to run RoadmapOS" section (prerequisites, `dotnet ef database update`, `dotnet run`, what each route does, how to run tests), and a "Known simplifications (V1)" section — Berkan added one entry to it independently.
* Walked the Phase 1 completion gate from `docs/ROADMAP.md` against reality, item by item (table in `docs/daily-code-notes/day-10.md`), including an honest partial-credit note on "Skills and roadmap items can be managed" (Skills has full CRUD; `RoadmapPhase`/`Project`/`Milestone` are relationally modeled but intentionally have no management UI — never asked for by the Day 6–9 plans).

**Runtime flow:** No behavior changes today — see Day 1–9 entries for the full request/data flow, all still valid. Full detail (including the corrected DI/EF Core explanation) in `docs/daily-code-notes/day-10.md`.

**Verification:**
* `dotnet build` (from a fully clean `bin`/`obj` state) → 0 errors, 0 warnings.
* `dotnet test` → 8/8 passing.
* All 6 major routes → HTTP 200.
* Refactor regression check: invalid `Create` submission → still HTTP 200 with "The Name field is required"; `Edit/1` still pre-fills correctly.
* Berkan explained the full MVC request lifecycle (`Program.cs` → middleware → DI-resolved controller → domain/EF Core → view rendering) and DI/EF Core in his own words; both were correct in substance, with one DI framing corrected live.

**Evidence:** All 7 Phase 1 completion-gate items satisfied, with concrete evidence for each (see the day-10 table); commit (pending); English README now serves as real onboarding documentation; independent task (README simplification entry) completed and verified.

**Mistakes or difficulties:** None blocking. The DI misconception ("no dependency" vs. "redirected dependency") is a common one worth watching for again in Phase 2, where DI usage will scale up significantly (more services, more interfaces).

**Production considerations:** The "Known simplifications" section in `README.md` is now the canonical, honest list of what V1 deliberately does not do — future phases should be checked against it rather than re-discovering the same gaps.

**Understanding questions and answers (today's questions doubled as the release-gate check):**
1. Q: Explain the full MVC request lifecycle for `GET /Skills`. A: Correct end-to-end, from `Program.cs` startup (DI registration, middleware pipeline order, `app.Run()`) through per-request middleware (`UseRouting` matching the endpoint, `UseAuthorization` passing through), DI resolving `SkillsController`'s dependencies within a per-request scope, `EfSkillCatalog.GetAll()` translating LINQ to a real SQL `LEFT JOIN` via `.Include()`, and convention-based view resolution wrapped in `_Layout.cshtml`.
2. Q: Explain DI and EF Core. A: EF Core explanation correct (ORM, class↔table mapping, CRUD without hand-written SQL) plus the added nuance that `DbContext` represents a unit of work, not just a mapping layer. DI explanation needed one correction: dependencies aren't removed, they're redirected — to an abstraction (interface) supplied from outside rather than self-constructed via `new`.
3. Q: Why doesn't `_SkillForm.cshtml` contain its own `<form>` tag? A: Correct — the `<form>` opening differs between Create and Edit (route/hidden `Id`), so only the genuinely shared content was extracted.

**Independent task:** Add one more entry to `README.md`'s "Known simplifications" list, based on Berkan's own observation of using the app. Completed and independently verified.

**RoadmapOS V1 is released.** Phase 1 is complete.

**Next session:** Phase 2, Week 3, Day 11 — begin StockPilot Inventory and Order API (project setup + first vertical slice, scoped from Week 3's weekly topic list since Phase 2 isn't broken into daily topics like Phase 1 was).

### 2026-09-13 — Phase 2, Week 3, Day 11 (StockPilot begins)

**Topic:** StockPilot Inventory and Order API kickoff — project setup, controller-based Web API, HTTP status codes, first vertical slice.

**Problem solved:** Starting a genuinely new product (not an extension of RoadmapOS) with its own solution, establishing the MVC-vs-Web-API distinction concretely (no views, JSON responses, explicit status codes), and catching two real discrepancies between plan and reality on the very first day rather than glossing over them.

**What I learned:** `dotnet new webapi` now defaults to Minimal API scaffolding in current .NET versions — `--use-controllers` is required to get the controller-based style `CLAUDE.md` mandates; attribute routing (`[Route]`, `[HttpGet("{id}")]`) exists specifically so multiple actions can share one clean, resource-oriented URL (`api/products/{id}`) distinguished only by HTTP verb — something convention routing's `{controller}/{action}` pattern can't do, since it always bakes the action name into the URL; `[ApiController]` automatically formats results like `NotFound()` as RFC 9110-compliant `ProblemDetails` JSON with zero extra code — discovered live, not read about; `ActionResult<T>` vs. `IReadOnlyList<T>` come from different namespaces (`Microsoft.AspNetCore.Mvc`, explicitly imported, vs. `System.Collections.Generic`, covered by `ImplicitUsings`) — a concrete instance of Day 1's implicit-usings concept.

**What I implemented:**
* `StockPilot.slnx` — a new, separate solution from `RoadmapOS.slnx`.
* `src/StockPilot.Api` scaffolded via `dotnet new webapi --use-controllers`; default `WeatherForecast` sample removed.
* Caught and fixed a real `NU1903` security advisory in the auto-generated `Microsoft.OpenApi` 2.0.0 dependency by upgrading `Microsoft.AspNetCore.OpenApi` to 10.0.11.
* `Models/ProductDto.cs`, `Controllers/ProductsController.cs` — `GetAll()` and (independent task) `GetById(int id)`, in-memory data, `Ok(...)`/`NotFound()`.

**Runtime flow:** `GET /api/products/{id}` → Kestrel → middleware → attribute-routed to `ProductsController.GetById` (no view, no `_Layout` — a genuine architectural difference from every RoadmapOS request) → `Ok(product)` or `NotFound()` → JSON serialized directly to the response body. Full trace in `docs/daily-code-notes/day-11.md`.

**Verification:**
* `dotnet build` → 0 errors, 0 warnings (including the resolved security advisory).
* `GET /api/products` → HTTP 200, correct JSON array.
* `GET /api/products/2` → HTTP 200, correct single product; `GET /api/products/999` → HTTP 404 with an auto-generated `ProblemDetails` body.
* `GET /openapi/v1.json` → HTTP 200 (raw schema, not an interactive UI — corrected from the day's original plan, which had assumed a Swagger UI would be present by default).

**Evidence:** Working, verified endpoints (`GetAll`, `GetById`); commit (`0f87acb`); English/Turkish technical explanation (routing philosophy, `ActionResult<T>`'s purpose, namespace origins); independent task completed and re-verified.

**Mistakes or difficulties:** Two assumptions in the day's plan didn't survive contact with the actual current .NET tooling: the default `webapi` template no longer includes Swagger UI (just raw OpenAPI JSON), and its default package reference carried a known vulnerability. Both were caught by actually running the commands and reading their output, rather than assuming template output matches older documentation/tutorials — a good reminder that "the template will just work" is not itself verification.

**Production considerations:** In-memory data only — Week 4 introduces EF Core for this project. No authentication yet (Week 5). No interactive API documentation UI yet (would need an additional package, not justified for today's single endpoint).

**Understanding questions and answers:**
1. Q: Why doesn't `ProductsController` need view support? A: It's a controller-based Web API returning JSON, not HTML — there's nothing for a view engine to render.
2. Q: How does attribute routing fundamentally differ from RoadmapOS's convention routing, not just mechanically? A: Convention routing always embeds the action name in the URL itself (`/Skills/Edit/5`); attribute routing lets multiple actions share one clean, resource-oriented URL (`api/products/{id}`), distinguished purely by HTTP verb — which is what REST's "URL identifies a resource, HTTP verb identifies the operation" model actually requires.
3. Q: Why `ActionResult<T>` instead of returning `T` directly? A: It lets one action return either real data (`Ok(product)`) or a non-`T` HTTP result (`NotFound()`) from the same method — a plain `T` return type couldn't compile a `NotFound()` return at all.

**Independent task:** Add `GetById(int id)` returning `Ok(product)` or `NotFound()`. Completed and independently verified by Berkan (curl-tested against both a valid and invalid ID); re-verified by Claude, which also surfaced the `ProblemDetails` auto-formatting behavior.

**Next session:** Phase 2, Week 3, Day 12 (Tuesday — happy-path implementation) — extend StockPilot with write operations (Create at minimum), manual DTO↔entity mapping, and correct status codes for writes.

### 2026-09-13 — Phase 2, Week 3, Day 12

**Topic:** `POST` write operations, 201 Created + Location header, domain/DTO separation, manual mapping.

**Problem solved:** Day 11 had conflated "the data itself" and "the API's response shape" into one `ProductDto`; today split them into `Product` (domain, server-assigned `Id`) and `CreateProductRequest` (input contract), then implemented `Create` correctly per REST convention (201 + Location, not just 200), plus a `Delete` independent task.

**What I learned:** Why `new ProductDto(product)` can't work as written — a record's primary constructor expects the exact parameter list declared (`int, string, string, decimal`), not a single `Product` object; C# has no automatic "unpack an object's properties into another constructor" behavior, which is exactly the gap `ToDto` fills; why `Products.Select(ProductDto)` fails (`CS0119`, confirmed live by deliberately triggering it) — `ProductDto` is a type name, not a callable method, and `.Select` needs an actual method/lambda; `CreatedAtAction`'s three jobs at once (201 status, `Location` header pointing at `GetById`, and the created resource in the body); that a DELETE request can't be tested via a browser address bar (GET-only) — curl with `-X DELETE` is required.

**What I implemented:**
* `Models/Product.cs` (domain) and `Models/CreateProductRequest.cs` (input DTO), splitting Day 11's single `ProductDto`.
* `ProductsController.Create` — `CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto)`.
* Private `ToDto(Product)` mapping helper; `GetAll`/`GetById` updated to use it.
* `_nextId++` explicitly flagged as a known, non-thread-safe simplification (comment in code), deferred to Week 4's EF Core/IDENTITY replacement rather than fixed today.
* Independent task: `Delete(int id)` (`[HttpDelete("{id}")]`), `NoContent()`/`NotFound()`.

**Runtime flow:** `POST /api/products` → model binder builds a `CreateProductRequest` → a new `Product` is constructed and assigned an in-memory ID → `ToDto` maps it → `CreatedAtAction` produces 201 + a real `Location` URL pointing at `GetById`. Full trace, including the deliberate `CS0119` demonstration, in `docs/daily-code-notes/day-12.md`.

**Verification:**
* `dotnet build` → 0 errors, 0 warnings.
* `POST /api/products` → HTTP 201, `Location: .../api/Products/4`, correct body; following that exact `Location` with `GET` → HTTP 200, same resource; `GET /api/products` → 4 items.
* Deliberately broke the build (`Select(ProductDto)`) to show `CS0119` live, then reverted — build clean again.
* `DELETE /api/products/2` → HTTP 204; `DELETE /api/products/999` → HTTP 404; follow-up `GET /api/products` confirmed product 2 actually gone.

**Evidence:** Working endpoints (`Create`, `Delete`); commit (`72fb9d2`); English/Turkish technical explanation (needed two passes on `ToDto`'s purpose — a concrete "fridge/sandwich" analogy plus a live compiler error landed it after a first, too-abstract explanation didn't); independent task completed and re-verified.

**Mistakes or difficulties:** The first explanation of why `ToDto` is needed (framed around "method group" terminology) didn't land at all — a live, concrete compiler error plus a plain-language analogy was needed instead. Worth defaulting to "show the actual error first, explain after" for this kind of type-system question going forward, rather than leading with abstract terminology.

**Production considerations:** `_nextId++`'s thread-safety gap is real but inert today (no concurrent traffic); explicitly documented rather than silently accepted, and will disappear naturally once Week 4 replaces the in-memory list with SQL Server.

**Understanding questions and answers:**
1. Q: What would the client have to do without a `Location` header? A: Guess/hard-code the URL pattern themselves from the `id` in the response body, instead of being told exactly where the new resource lives.
2. Q: Why keep `CreateProductRequest` and `ProductDto` separate despite looking nearly identical today? A: They'll diverge — a future server-computed field on `ProductDto` (e.g. a timestamp) would otherwise leak into what `Create` accepts from a client, the same over-posting risk from RoadmapOS Day 5.
3. Q: Why is `_nextId++`'s non-atomicity harmless today but a real risk in production? A: No concurrent requests are actually racing today (sequential curl calls); real concurrent traffic could interleave the read-increment-write sequence and hand two different creates the same ID.

**Independent task:** Add `Delete(int id)` returning `NoContent()`/`NotFound()`. Completed and independently verified by Berkan; re-verified by Claude via curl (valid ID → 204, invalid → 404, confirmed removal via a follow-up `GetAll`).

**Next session:** Phase 2, Week 3, Day 13 (Wednesday — persistence/infrastructure slot) — likely validation on `CreateProductRequest` and/or ProblemDetails/global error handling, since EF Core persistence itself is Week 4's topic.

### 2026-09-13 — Phase 2, Week 3, Day 13

**Topic:** Validation, `[ApiController]`'s automatic behavior, global error handling.

**Problem solved:** Adding real input validation to `CreateProductRequest` without writing any manual `if (!ModelState.IsValid)` checks, and closing a real gap: an unhandled exception had never been tested, so it was unknown whether the API leaked a raw stack trace or returned something safe.

**What I learned:** Rather than trusting uncertain memory about whether DataAnnotations need a `[property:]` target specifier on positional record parameters, tested it live first — confirmed plain `[Required]`/`[StringLength]`/`[Range]` (no explicit target) work correctly with .NET 10's model validation; `[ApiController]` automatically short-circuits an action and returns 400 + `ValidationProblemDetails` the moment model validation fails, with zero custom code; `AddProblemDetails()` + `UseExceptionHandler()` intercept *any* unhandled exception and return a clean, safe JSON body — confirmed by literally disabling both and observing the alternative: a raw `text/plain` C# stack trace including file paths and ASP.NET Core's internal call chain, a genuine information-disclosure risk, not just an ugly response.

**What I implemented:**
* `CreateProductRequest`: `[Required, StringLength(50)]` (Sku), `[Required, StringLength(200)]` (Name), `[Range(0.01, double.MaxValue)]` (Price).
* `Program.cs`: `builder.Services.AddProblemDetails()` + `app.UseExceptionHandler()`.
* Two deliberate, reverted experiments: a temporary throw in `GetAll()` (to observe the 500 response, with and without the two lines above), and a temporary `[property:]`-less vs. hypothetically-targeted comparison (resolved by direct testing rather than by adding speculative code).

**Runtime flow:** Invalid `POST` body → model binder populates `CreateProductRequest` → DataAnnotations run against the bound model → `[ApiController]` sees `ModelState` is invalid and short-circuits before the action method body runs at all → 400 + `ValidationProblemDetails`. Unhandled exception → `UseExceptionHandler()` middleware catches it → `AddProblemDetails()`-configured formatting → 500 + clean `ProblemDetails`. Full transcript (both the "with" and "without" versions) in `docs/daily-code-notes/day-13.md`.

**Verification:**
* `dotnet build` → 0 errors, 0 warnings.
* Empty `Sku`, over-length `Sku`, negative `Price` → each correctly HTTP 400 with a field-specific message (noticed the `Price` range message rendered with a Turkish decimal comma — flagged, not fixed).
* Temporary throw, handler enabled → HTTP 500, clean `ProblemDetails` JSON.
* Temporary throw, handler disabled → HTTP 500, raw stack trace (`text/plain`) — confirmed live, not assumed.
* Full regression after reverting both experiments: `GetAll`/`GetById`/`Create`/`Delete`/invalid-`Create` all unchanged.
* Independent task: `[MinLength(2)]` added to `Sku`; single-character SKU → 400, two-character → 201.

**Evidence:** Working, verified validation and error-handling behavior (both directions demonstrated live); commit (pending); English/Turkish technical explanation; independent task completed and re-verified.

**Mistakes or difficulties:** None blocking. Good discipline moment: rather than asserting from memory whether record positional-parameter attributes need `[property:]` targeting, the uncertainty was tested directly instead of guessed at — the roadmap's "don't claim behavior works without verification" rule applied to Claude's own uncertain recollection, not just to Berkan's code.

**Production considerations:** The Turkish-culture validation message issue (comma decimals) is now noticed twice (Day 8's CSS bug, today's error message) — a real candidate for a future "pin the server to a fixed culture" fix, not yet scheduled. Global error handling isn't yet paired with logging (Day 9's `ILogger` pattern would be the natural next step, not done today).

**Understanding questions and answers:**
1. Q: What does testing (vs. assuming) tell us in ambiguous cases? A: That we should verify uncertain behavior directly rather than trust possibly-outdated or fuzzy recollection — demonstrated concretely today.
2. Q: How does `[ApiController]`'s automatic validation differ from RoadmapOS's manual `if (!ModelState.IsValid)`? A: The same underlying check, but triggered automatically via an attribute — no code written, action body never even runs on an invalid model.
3. Q: What would an unhandled exception actually return without `AddProblemDetails()`/`UseExceptionHandler()`? A: A raw, unformatted `text/plain` stack trace — shown live, including file paths and internal framework call frames.

**Independent task:** Add one more validation rule to `CreateProductRequest` and verify it live. Completed and independently verified by Berkan (`[MinLength(2)]` on `Sku`); re-verified by Claude.

**Next session:** Phase 2, Week 3, Day 14 (Thursday — tests, failures, production considerations) — a `StockPilot.Api.Tests` project, first tests (mapping/validation), more failure-case coverage.

### 2026-09-13 — Phase 2, Week 3, Day 14

**Topic:** Test isolation, `IProductStore`/DI, first StockPilot tests.

**Problem solved:** A naive first attempt at testing `ProductsController` directly failed live — its `static` product list meant one test's `Create` call permanently polluted every other test's view of the data. Fixed by extracting storage into `IProductStore`/`InMemoryProductStore`, the exact same DI seam RoadmapOS introduced on Day 3, this time motivated by a concrete, just-observed failure rather than introduced up front.

**What I learned:** `static` fields live at the process level, not per-instance — xUnit's guarantee of "a fresh test class instance per test method" does nothing to reset them, so shared mutable static state silently breaks test isolation regardless of test framework; the fix (inject a per-instance store, register it `Singleton` in the real app) is identical in shape to RoadmapOS Day 3's `ISkillCatalog`, reinforcing that this is a general pattern, not a one-off; `Assert.IsType<T>()` both verifies AND casts in one call; `ActionResult<T>`'s `.Result` property needs an explicit cast (`(OkObjectResult)result.Result!`) before its `.Value` can be unwrapped, because `.Result`'s declared type is the non-generic base `ActionResult`; unit-testing a controller method directly bypasses the entire ASP.NET Core request pipeline, so `[ApiController]`'s automatic validation never runs in these tests — confirmed as a real, understood limitation, not silently assumed away.

**What I implemented:**
* A deliberate, reverted naive test (`NaiveAttempt.cs`) proving the static-list test-isolation bug live (`Expected: 4, Actual: 5`), then deleted.
* `Models/IProductStore.cs`, `InMemoryProductStore.cs` — non-static, instance-based storage.
* `ProductsController` refactored to constructor-inject `IProductStore`; `Program.cs` registers it `AddSingleton`.
* `tests/StockPilot.Api.Tests/ProductsControllerTests.cs` — 7 tests, each via a `CreateController()` helper building a fresh controller+store pair; later an 8th (independent task) verifying sequential ID assignment *within* one shared store instance.

**Runtime flow:** No production runtime change — `ProductsController` still resolves an `IProductStore` via DI exactly as before conceptually, just through an abstraction now. Tests call controller methods directly, bypassing Kestrel/routing/filters entirely. Full transcript, including the very detailed line-by-line walkthrough of `ActionResult<T>` unwrapping that was needed on a second pass, in `docs/daily-code-notes/day-14.md`.

**Verification:**
* Naive attempt: 1 passed, 1 failed as predicted, live.
* After refactor: `dotnet test` → 8/8 passing (7 written today, 1 independent).
* Full curl regression: `GetAll`/`GetById`/`Create`/`Delete`/invalid-`Create` unchanged after the refactor.
* Independent task: a real typo (`created` vs. `created1`) and a directory-relative-path mixup running `dotnet build`/`dotnet test` from inside `src/StockPilot.Api` were both hit and debugged live before the new test passed.

**Evidence:** A real, demonstrated bug (test-isolation failure) and its fix; passing, isolated test suite; commit (`6464d64`); English/Turkish technical explanation (needed a very detailed, line-by-line second pass on `ActionResult<T>`/`Assert.IsType` mechanics before it landed); independent task completed and re-verified, including debugging real mistakes along the way.

**Mistakes or difficulties:** The first explanation of `ProductsControllerTests.cs` was apparently too high-level — a full line-by-line walkthrough (what each cast, unwrap, and assertion actually does mechanically) was requested and given afterward. Worth defaulting to this level of detail for new C#/testing syntax going forward rather than waiting to be asked twice.

**Production considerations:** These are still pure unit tests (no HTTP, no validation pipeline) — an integration-test day (`WebApplicationFactory`, real HTTP requests) is the natural next step for testing `[ApiController]`'s validation behavior specifically, not scheduled yet.

**Understanding questions and answers:**
1. Q: Why did the static list break test isolation? A: Tests must not affect each other, but a static list is shared across every test — one test's mutation is visible to the next.
2. Q: Why `Singleton` for `IProductStore` in the real app? A: The app needs the same data shared across all requests (unlike tests, which each want a private copy) — same reasoning as RoadmapOS's `InMemorySkillCatalog`.
3. Q: Why doesn't `[ApiController]`'s automatic validation run when a test calls `controller.Create(request)` directly? A: That behavior is enforced by the MVC request pipeline (routing → filters → action), which a direct method call never enters — no HTTP request means no pipeline, no filters, no automatic validation.

**Independent task:** Write a test creating two products on the same controller/store instance, verifying sequential IDs (4, then 5). Completed and independently verified by Berkan, including live-debugging a real typo and a working-directory path issue; re-verified by Claude (8/8 passing).

**Next session:** Phase 2, Week 3, Day 15 (Friday — refactor/verification/documentation, closing out Week 3) — pagination/filtering/sorting and/or interactive API docs, plus a full verification pass.

### 2026-09-13 — Phase 2, Week 3, Day 15 (Week 3 closed)

**Topic:** Search/sort/pagination on `GetAll`, full regression across both solutions, Week 3 close-out.

**Problem solved:** `GetAll` always returned every product with no way to filter, order, or page through results — a real limitation for any API expected to scale past a handful of records. Closed out Week 3 by adding this and running a full verification pass across both StockPilot and RoadmapOS.

**What I learned:** `.Skip()`/`.Take()` order is not interchangeable — proved live by temporarily swapping them (`Take(1).Skip(1)` on a 3-item, single-page-size query returned an empty result instead of the correct second item), then explaining precisely why: `Take` first collapses the sequence down to `pageSize` items, so a subsequent `Skip` of any amount beyond that empties it out entirely. Also hit a real tooling gotcha: editing a file open in the IDE via a terminal command (`sed`) got silently overwritten by the IDE's own in-memory state once VS Code re-synced — switched to the `Edit` tool (which the IDE tracks properly) to redo the demonstration cleanly. `[FromQuery]` parameters are optional-by-default and orthogonal to route parameters (`{id}`) — the latter identify *which* resource, the former modify *how* the same resource is returned.

**What I implemented:**
* `Models/PagedResult.cs` — a generic `(Items, Page, PageSize, TotalCount)` envelope.
* `ProductsController.GetAll` rewritten with `[FromQuery] search/sortBy/page/pageSize`, using `Where`/`OrderBy`/`Skip`/`Take` (first real use of LINQ filtering/sorting/paging operators in either codebase; RoadmapOS had only used `GroupBy`/`Select`).
* 3 existing tests updated to assert against `PagedResult<ProductDto>` instead of a bare list.
* Independent task (found already completed ahead of being asked): a `"sku"` sort option added to the `sortBy` switch.

**Runtime flow:** `GET /api/products?search=...&sortBy=...&page=...&pageSize=...` → query string bound to four optional parameters → filter → sort → count → skip/take → wrap in `PagedResult<ProductDto>` → `Ok(...)`. Full trace, including the reversed-order bug demonstration, in `docs/daily-code-notes/day-15.md`.

**Verification:**
* `dotnet test` (StockPilot) → 8/8 passing after updating for the new return type.
* Live curl: default (`totalCount=3`), `?search=mouse` (1 match), `?sortBy=price` (ascending), `?page=2&pageSize=1` (correct single item + correct metadata), `?sortBy=sku` (independent task, correct SKU order).
* Deliberately reversed `Skip`/`Take` → `page=2&pageSize=1` returned `"items": []`; reverted → correct result restored.
* Full regression, both solutions: `dotnet build`/`dotnet test` on `StockPilot.slnx` (8/8) and `RoadmapOS.slnx` (8/8), both clean and independent of each other.

**Evidence:** Working, verified pagination/filtering/sorting; a real bug demonstrated and explained, not just described; passing test suites across two solutions; commit (pending); English/Turkish technical explanation; independent task found already completed, re-verified.

**Mistakes or difficulties:** A `sed`-based edit to a file open in the IDE was silently reverted by VS Code's own in-memory copy — worth remembering that terminal edits to IDE-open files are unreliable; the `Edit` tool (IDE-aware) is the safer choice for live demonstrations going forward.

**Production considerations:** Sort options remain intentionally narrow (`id`/`name`/`price`/`sku`) — extendable later without redesign. No maximum `pageSize` cap exists yet (a client could request `pageSize=1000000`) — a reasonable future hardening item, not addressed today.

**Understanding questions and answers:**
1. Q: `[FromQuery]` vs. a route parameter (`{id}`)? A: A route parameter identifies *which* resource (omitting it changes the URL to a different route entirely); a query parameter modifies how the *same* resource is returned, and is naturally optional.
2. Q: What would reversing `Skip`/`Take` do? A: Demonstrated live — `Take(pageSize)` first collapses the sequence to `pageSize` items, so any subsequent `Skip` beyond that count empties the result; confirmed with an actual empty response, then reverted.
3. Q: Why return `TotalCount` separately from `Items.Count`? A: `Items.Count` only ever reflects the current page's size (bounded by `pageSize`); a client can't tell "3 total" from "3000 total" without a separate, unpaginated count.

**Independent task:** Add a new `sortBy` option. Found already completed (`"sku"` case added) before being explicitly requested; re-verified live via `?sortBy=sku`.

**Week 3 is complete.** StockPilot now has a working, tested, documented read/write API with validation, error handling, and query capabilities — all in-memory, ready for Week 4's EF Core migration.

**Next session:** Phase 2, Week 4, Day 16 — EF Core for StockPilot (`StockPilotDbContext`, `Product` as a real entity, first migration, `EfProductStore` replacing `InMemoryProductStore`).

### 2026-09-13 — Phase 2, Week 4, Day 16

**Topic:** EF Core + SQL Server for StockPilot — the RoadmapOS Day 3→4 transition, repeated.

**Problem solved:** StockPilot's product data reset every time the app restarted. Gave it real persistence by swapping `IProductStore`'s registered implementation from `InMemoryProductStore` to a new `EfProductStore`, without touching `ProductsController` at all — the exact payoff Day 14's `IProductStore` abstraction was built for.

**What I learned:** A real EF Core tooling warning (`decimal` property with no precision/scale specified, risking silent truncation) was caught from `dotnet ef migrations add`'s own output rather than assumed away — fixed with `HasPrecision(18, 2)`, migration regenerated. Clarified an important, general distinction (prompted by a sharp question): DTOs exist to protect the boundary between the server and the *outside world* (HTTP request/response bodies) — not for calls between a controller and its own internal storage abstraction. `IProductStore`/`EfProductStore` correctly pass the domain `Product` directly; the DTO boundary is still fully respected at `Create`'s input (`CreateProductRequest`) and every action's output (`ProductDto`/`PagedResult<ProductDto>`) — the same pattern RoadmapOS's `ISkillCatalog` already used.

**What I implemented:**
* EF Core packages, `StockPilotDb` connection string (same SQL Server instance as RoadmapOS, separate database).
* `Data/StockPilotDbContext.cs`, `Data/EfProductStore.cs`, `Data/DbSeeder.cs` — direct mirrors of RoadmapOS's `RoadmapOSDbContext`/`EfSkillCatalog`/`DbSeeder`.
* `Program.cs`: `IProductStore` registration switched `AddSingleton<..., InMemoryProductStore>` → `AddScoped<..., EfProductStore>` (Scoped for the same DbContext-thread-safety reason as RoadmapOS Day 4); `InMemoryProductStore` kept, unregistered, still used directly by existing tests.
* `InitialCreate` migration (regenerated once, after the precision fix).

**Runtime flow:** `ProductsController` now resolves `EfProductStore` via DI; `_context.Products.ToList()` issues a real SQL query; filtering/sorting/pagination still happens in-memory in the controller (a noted, deliberate simplification — not pushed down to SQL — to avoid changing `IProductStore`'s signature). Full trace in `docs/daily-code-notes/day-16.md`.

**Verification:**
* `dotnet build`/`dotnet test` (StockPilot) → 0 errors/warnings, 8/8 passing (existing tests untouched by the DI swap, since they construct `InMemoryProductStore` directly).
* Migration applied — `StockPilot` database and `Products` table created with `decimal(18,2)` correctly in place.
* Live curl: `GetAll` (seeded data), `Create` (201 + Location), `GetById` (new product) all against real SQL Server.
* App fully stopped, then queried directly via SQL — all 4 rows (3 seed + 1 created) still present, proving real persistence.
* Independent task: a 5th product inserted directly via SSMS (`SKU-008`, "Headphones"); `/api/products` showed it automatically with zero code changes.

**Evidence:** Working, verified persistence; a real EF Core warning caught and fixed rather than ignored; commit (`e72936f`); English/Turkish technical explanation (Scoped rationale restated, DTO-vs-domain-object boundary clarified via a genuinely good question); independent task completed and re-verified.

**Mistakes or difficulties:** None blocking. The DTO-boundary question was a valuable moment — confirms the "DTOs protect the server/client boundary, not internal calls" distinction needed explicit statement rather than being assumed obvious from prior days' pattern-following.

**Production considerations:** `EfProductStore.GetAll()` pulls the entire table into memory before filtering/sorting/paging — fine at today's scale, but a real production concern if `Products` grows large; pushing `Where`/`OrderBy`/`Skip`/`Take` down to `IQueryable<Product>` (SQL-side) would be the eventual fix, deferred since it would require reshaping `IProductStore`'s interface.

**Understanding questions and answers:**
1. Q: Why `Scoped` (again)? A: Same as RoadmapOS Day 4 — `DbContext` holds a non-thread-safe connection and change tracker; sharing one across concurrent requests (as `Singleton` would) risks real bugs, so each request gets its own via `Scoped`.
2. Q: Why keep `InMemoryProductStore` around? A: For tests — `ProductsControllerTests.cs` constructs it directly as a fast, isolated test double, avoiding a real database in unit tests.
3. Q: What `Price` value could be silently truncated without `HasPrecision`? A: Something like `19.999` — a value with more decimal digits than the column's default scale allows.

**Independent task:** Insert a product directly via SSMS/raw SQL, confirm it appears via `/api/products` with no code changes. Completed and independently verified by Berkan; re-verified by Claude via direct SQL query and the running app.

**Next session:** Phase 2, Week 4, Day 17 — async database operations and `CancellationToken`, converting `IProductStore`/`EfProductStore`/`ProductsController` to `async`/`await` (a first for both codebases).

### 2026-09-13 — Phase 2, Week 4, Day 17

**Topic:** Async database operations, `CancellationToken` — a first for both RoadmapOS and StockPilot.

**Problem solved:** All EF Core calls so far (in either codebase) had been synchronous, blocking the handling thread for the duration of each database round-trip. Converted `IProductStore`, both its implementations, and `ProductsController` to `async`/`await`, threading a `CancellationToken` through to EF Core's own async methods.

**What I learned:** `CancellationToken` parameters on a controller action are special-cased by ASP.NET Core's model binding — no attribute needed, the framework auto-populates it from `HttpContext.RequestAborted`, which flips to cancelled if the client disconnects, a timeout fires, or the server is shutting down; that token, once passed all the way down into EF Core's async calls (`ToListAsync`, `FindAsync`, `SaveChangesAsync`), lets a long-running query abort early instead of wastefully finishing for a client no longer listening — conceptually the same idea as Node's `AbortSignal`. Confirmed a genuine design point via a follow-up question: `Task.FromResult(...)` in `InMemoryProductStore` satisfies `IProductStore`'s async contract without there being any real asynchronous work — a class can be forced to "look async" purely for interface consistency, distinct from actually benefiting from async I/O.

**What I implemented:**
* `IProductStore` — all four methods return `Task<T>` and accept an optional `CancellationToken`.
* `EfProductStore` — real async EF Core calls throughout.
* `InMemoryProductStore` — `Task.FromResult(...)` wrapping, no real I/O.
* `ProductsController` — all four actions `async Task<ActionResult<T>>`, each with a `CancellationToken` parameter.
* All 8 existing tests converted to `async Task`; independent task added a 9th (`GetBySearch_ReturnsMatchingProducts`), explained line-by-line after being IDE-suggested rather than hand-written.

**Runtime flow:** `CancellationToken` auto-bound from `HttpContext.RequestAborted` → passed into `_productStore.XAsync(cancellationToken)` → `EfProductStore` passes it into the matching EF Core async call. Full trace in `docs/daily-code-notes/day-17.md`.

**Verification:**
* `dotnet build`/`dotnet test` (StockPilot) → 0 errors/warnings, 9/9 passing.
* Full curl regression: `GetAll`/`GetById`/`Create`/`Delete`/invalid-`Create`/pagination — all identical to the synchronous version's behavior.
* Not verified live today: an actual mid-request cancellation (network-level simulation deemed unreliable to demonstrate) — the wiring is confirmed correct, the live-cancellation scenario is deferred.

**Evidence:** Working, verified async conversion with no behavior change; commit (pending); English/Turkish technical explanation (CancellationToken auto-binding mechanism, `Task.FromResult` as interface-satisfaction-without-real-async); independent task completed (IDE-suggested code, explained and understood afterward rather than accepted uncritically) and re-verified.

**Mistakes or difficulties:** None blocking. Worth noting explicitly: code suggested by an IDE/AI tool was treated the same as self-written code — verified it built and passed, then required a full explanation before accepting it as understood, consistent with the "no passive copy-paste" principle even when the source of the suggestion isn't the mentor.

**Production considerations:** `InMemoryProductStore`'s async is cosmetic (interface consistency only, no real concurrency benefit) — worth remembering when reasoning about actual performance later. `CancellationToken` plumbing is now in place for when StockPilot's queries eventually become long enough for early-abort to matter in practice.

**Understanding questions and answers:**
1. Q: Why does `InMemoryProductStore` still need `Task.FromResult(...)`? A: Because it implements `IProductStore`, whose contract requires `Task<T>` return types — omitting it would be a compile error, regardless of whether real async work exists.
2. Q: Where does a controller's `CancellationToken` parameter get its value from? A: Automatically from `HttpContext.RequestAborted`, via ASP.NET Core's special-cased model binding for that type — no attribute required.
3. Q: Is it a problem that async wasn't introduced until now? A: No — it was deliberately deferred until its scheduled topic (Week 4), consistent with `CLAUDE.md`'s "don't introduce a concept before it's needed" principle; RoadmapOS's Day 4 EF Core introduction was kept synchronous for the same reason (avoid stacking too many new concepts in one session).

**Independent task:** Write a test for the `search` filter. Completed (IDE-suggested, then explained and verified line-by-line by Berkan rather than accepted blindly); re-verified by Claude (9/9 passing).

**Next session:** Phase 2, Week 4, Day 18 (Wednesday — persistence/infrastructure) — database constraints and SQL indexes, most likely a unique index on `Product.Sku`.
