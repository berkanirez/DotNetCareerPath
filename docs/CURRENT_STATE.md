# CURRENT_STATE.md — Source of Truth for Current Progress

This file reflects the actual current state of the learning journey. It must always match reality. Update it only after a day's Definition of Done is confirmed satisfied (see `CLAUDE.md`).

## Status snapshot

* **Setup phase:** Complete
* **Roadmap phase:** Phase 1 — RoadmapOS
* **Week:** 1
* **Day:** 4 (complete)
* **Active project:** RoadmapOS
* **Status:** Day 4 complete — `Skill` persisted in real SQL Server via EF Core; `/Skills` reads from the database end to end
* **Available study time:** 2 hours/day
* **Progress:** ~4% (Day 4 of 110 total study days across the 22-week roadmap)

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
2. Begin Phase 1, Week 1, Day 5: explain and plan model binding, validation, and a `Skill` create/edit flow (forms, error display), with manual verification.
3. Wait for approval (`UYGULA`) before creating or editing any application files.

## Day 5 expected outcome

* Model binding and validation attributes explained.
* A create (and/or edit) flow for `Skill` built on top of the existing EF Core-backed `ISkillCatalog`/`RoadmapOSDbContext`.
* Validation errors displayed correctly in the view.
* Manually verified end to end (submit valid data, submit invalid data, confirm behavior in both cases).
* No authentication, Docker or future technology added.
