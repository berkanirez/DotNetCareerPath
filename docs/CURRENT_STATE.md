# CURRENT_STATE.md — Source of Truth for Current Progress

This file reflects the actual current state of the learning journey. It must always match reality. Update it only after a day's Definition of Done is confirmed satisfied (see `CLAUDE.md`).

## Status snapshot

* **Setup phase:** Complete
* **Roadmap phase:** Phase 1 — RoadmapOS
* **Week:** 1
* **Day:** 8 (complete)
* **Active project:** RoadmapOS
* **Status:** Day 8 complete — `/Dashboard` shows overall and category progress via LINQ (`GroupBy`/`Select`), verified against real data
* **Available study time:** 2 hours/day
* **Progress:** ~7% (Day 8 of 110 total study days across the 22-week roadmap)

## Completed items

* `DotNetCareerPath` folder created.
* Root folder opened in Visual Studio Code.
* Claude Code installed.
* Planning documents created (`CLAUDE.md`, `README.md`, `.gitignore`, `docs/ROADMAP.md`, `docs/CURRENT_STATE.md`, `docs/REQUIREMENTS_MATRIX.md`, `docs/LEARNING_LOG.md`).
* Git repository initialized (`git init`), no commit yet.
* `RoadmapOS.slnx` solution created.
* `src/RoadmapOS.Web` ASP.NET Core MVC project scaffolded (`dotnet new mvc`) and added to the solution.
* Build verified: 0 errors, 0 warnings.
* App run and verified manually: `/` returns the default MVC homepage (HTTP 200).
* Independent task completed and verified: `HomeController.Status()` action added, returns plain text via `Content(...)`, confirmed at `/Home/Status` (HTTP 200).
* SDK/runtime/solution/project, middleware pipeline ordering, and MVC view-resolution convention explained and understood.
* `SkillLevel` enum, `Skill` class (`IComparable<Skill>`, nullable `Notes`, computed `IsAtTarget`), and `SkillSnapshot` record added under `src/RoadmapOS.Web/Domain/`.
* Temporary in-`Program.cs` console verification proved: sorting via `IComparable<Skill>`, nullable-safe access (both non-null and genuinely-null cases), and record value equality (`with` expression).
* Independent task completed and verified: added a fourth `Skill` (`SQL Server`) with `Notes` left unset, confirmed `Notes?.Length ?? 0` correctly prints `0` for a genuinely null value.
* `docs/daily-code-notes/day-02.md` created (Turkish) — line-by-line code walkthrough covering purpose and syntax for each piece added today. This is now a standing daily practice (see `CLAUDE.md` "Daily code notes" section).
* Day 2 changes committed by Berkan (`f062f7a`).
* `ISkillCatalog` interface and `InMemorySkillCatalog` implementation added under `src/RoadmapOS.Web/Domain/`, registered via `builder.Services.AddSingleton<ISkillCatalog, InMemorySkillCatalog>()`.
* Day 2's temporary `Program.cs` console verification block removed — replaced by a real vertical slice.
* `SkillsController` (constructor-injected `ISkillCatalog`) and `Views/Skills/Index.cshtml` added; `/Skills` verified end to end (HTTP 200, correct sort order, correct count).
* Independent task completed and verified: skill count (`Model.Count`) displayed above the table.
* `docs/daily-code-notes/day-03.md` created (Turkish).
* Day 3 changes committed and pushed by Berkan (`34eebf5`, includes the pending Day 2 doc updates).
* SQL Server 2022 Express installed and verified running locally (`localhost\SQLEXPRESS`), after `winget`'s bootstrapper failed twice (Turkish-locale language prompt in unattended mode) and was worked around with a manual installer download.
* EF Core packages added (`Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`); `RoadmapOSDbContext` and `EfSkillCatalog` added under `src/RoadmapOS.Web/Data/`.
* `InitialCreate` migration created and applied — `RoadmapOS` database and `Skills` table now exist in real SQL Server, verified independently via direct SQL query (not just through the app).
* DI registration switched from `AddSingleton<ISkillCatalog, InMemorySkillCatalog>()` to `AddScoped<ISkillCatalog, EfSkillCatalog>()`; `SkillsController` required no changes.
* Temporary Development-only seed block added to `Program.cs` (placeholder until Day 9's formal seed strategy).
* `docs/daily-code-notes/day-04.md` created (Turkish).
* Independent task completed and verified: a 5th skill (`Git`) inserted directly via SSMS/raw SQL, confirmed it appears correctly sorted (tie-break with `C#`) at `/Skills`.
* Day 4 changes committed and pushed by Berkan (`4fec28a`).
* `ISkillCatalog` extended with `GetById`, `Add`, `Update`; implemented in both `EfSkillCatalog` and `InMemorySkillCatalog`.
* `Models/SkillFormModel.cs` added (validation-attributed DTO, decoupled from the `Skill` domain entity to avoid over-posting).
* `SkillsController` gained `Create` (GET/POST) and `Edit` (GET/POST) actions, both POSTs protected with `[ValidateAntiForgeryToken]`.
* `Views/Skills/Create.cshtml` and `Edit.cshtml` added (tag helpers, client+server validation); `Index.cshtml` linked to both.
* Verified end to end via curl: invalid submission → HTTP 200 with validation error, no redirect; valid submission → HTTP 302 + persisted row; missing anti-forgery token → HTTP 400 (confirmed live).
* `docs/daily-code-notes/day-05.md` created (Turkish).
* Independent task completed and verified: edited `Git`'s `CurrentLevel` to match its `TargetLevel`; confirmed `/Skills` now shows "Yes" under "At Target?" for that row.
* Day 5 changes committed and pushed by Berkan (`50482af`).
* `RoadmapPhase`, `Project`, `Milestone` entities added (`src/RoadmapOS.Web/Domain/`) with a `RoadmapPhase (1) → Project (*) → Milestone (*)` relationship chain.
* `RoadmapOSDbContext.OnModelCreating` added: `HasMaxLength`/`IsRequired` on all string properties (including the previously-deferred `Skill.Name`/`Category`/`Notes`), and explicit `HasOne`/`WithMany`/`HasForeignKey`/`OnDelete(Cascade)` for both new relationships.
* `AddPhaseProjectMilestone` migration created and applied — new tables, foreign keys, and auto-generated indexes confirmed in SQL Server.
* Seed block extended with real roadmap data (Phase 1 → RoadmapOS project → 3 milestones reflecting actual Day 1–10 progress).
* Constraints verified live, independent of the app: invalid FK insert rejected, over-length `Name` insert rejected (`String or binary data would be truncated`), and cascade delete confirmed (deleting a phase removed its project and milestones automatically).
* `docs/daily-code-notes/day-06.md` created (Turkish).
* Independent task completed and verified: added a second `Project` under the existing phase via SSMS, then confirmed SQL Server rejects a `Milestone` insert with an invalid `ProjectId`.
* Day 6 changes committed and pushed by Berkan (`66445ed`).
* `tests/RoadmapOS.Web.Tests` xUnit project created, added to the solution, referencing `RoadmapOS.Web`.
* `Domain/ProgressCalculator.cs` added (no interface — no real need for one yet, unlike `ISkillCatalog`), built test-first: watched 2 tests genuinely fail (`NotImplementedException`) before implementing, then pass.
* 4 more edge-case tests added (multi-skill weighted sum, over-target clamping via `Math.Min`, all-targets-zero divide-by-zero guard) — 6 tests total, all passing (~30ms, no DB/HTTP involved).
* `docs/daily-code-notes/day-07.md` created (Turkish), including the actual Red→Green transcript.
* Independent task completed and verified: wrote a new test for a skill with `TargetLevel=NotStudied` mixed with a normal skill; correctly hand-calculated the expected result (100) before running it, confirming the formula's non-obvious behavior (a targetless skill's `CurrentLevel` inflates the overall percentage without being checked against anything).
* `Domain/CategoryProgress.cs` added; `ProgressCalculator.CalculateCategoryProgress` built test-first (`GroupBy`/`Select`), 2 more tests added (8 total, all passing).
* `ProgressCalculator` registered in DI for the first time (`AddSingleton`), now that a real consumer (`DashboardController`) exists.
* `Models/DashboardViewModel.cs`, `Controllers/DashboardController.cs`, `Views/Dashboard/Index.cshtml` added; `/Dashboard` shows overall + per-category progress bars.
* Real bug caught and fixed live: the server's Turkish (`tr-TR`) culture made `double.ToString()` use a comma decimal separator, producing invalid CSS (`width: 16,67%`); fixed with `CultureInfo.InvariantCulture`.
* `_Layout.cshtml` nav updated with "Dashboard" and (previously missing) "Skills" links.
* `docs/daily-code-notes/day-08.md` created (Turkish).
* Independent task completed and verified: added a new `Skill` (`xUnit`, category `Testing`) via the Create form; `/Dashboard` automatically showed a new "Testing" row with the correctly hand-calculated percentage, with no code changes — confirming the dashboard is fully data-driven.

## Decisions on record

* IDE: Visual Studio Code
* C# tooling: C# Dev Kit
* AI environment: Claude Code
* Target framework: .NET 10 LTS
* Teaching language: Turkish
* Code and documentation language: English
* First project: RoadmapOS
* First application type: ASP.NET Core MVC
* Primary database: SQL Server
* Claude must plan and explain before editing application files.
* Claude must wait for the explicit keyword `UYGULA` before implementing.
* Future technologies (auth, Docker, messaging, microservices, etc.) must not be introduced ahead of their scheduled phase.
* .NET 10's `dotnet new sln` produces `.slnx` (new XML solution format) instead of the classic `.sln` — functionally equivalent, noted for future reference.
* `docs/daily-code-notes/` is a documented exception to the "all documentation stays English" rule: those files are written in Turkish (see `CLAUDE.md`).
* Local dev database engine: SQL Server 2022 Express, instance `localhost\SQLEXPRESS`, Windows Authentication (no SQL login/password in use).

## Next action

1. Read all five required documents (`CLAUDE.md`, `docs/ROADMAP.md`, `docs/CURRENT_STATE.md`, `docs/REQUIREMENTS_MATRIX.md`, `docs/LEARNING_LOG.md`).
2. Begin Phase 1, Week 2, Day 9: explain and plan requirement mapping, evidence records, logging, seed data, and basic error handling.
3. Wait for approval (`UYGULA`) before creating or editing any application files.

## Day 9 expected outcome

* A real seed data strategy replacing the temporary Development-only seed blocks in `Program.cs` (in place since Day 4/6).
* Requirement/evidence concept modeled in the app itself (linking `Skill`-like tracking to the manual `REQUIREMENTS_MATRIX.md` process), if in scope for today — to be confirmed in the day's plan.
* Structured logging introduced where meaningful.
* Basic error handling for realistic failure cases (not yet covered: e.g. a missing record on `Edit`).
* No authentication, Docker or future technology added.
