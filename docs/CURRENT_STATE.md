# CURRENT_STATE.md — Source of Truth for Current Progress

This file reflects the actual current state of the learning journey. It must always match reality. Update it only after a day's Definition of Done is confirmed satisfied (see `CLAUDE.md`).

## Status snapshot

* **Setup phase:** Complete
* **Roadmap phase:** Phase 1 — RoadmapOS
* **Week:** 1
* **Day:** 19 (complete)
* **Active project:** StockPilot Inventory and Order API
* **Status:** Day 18's 500→409 gap closed with a two-layer defense (proactive `SkuExistsAsync` check + reactive `DbUpdateException` catch); verified live and via 11/11 passing tests (2 new).
* **Available study time:** 2 hours/day
* **Progress:** ~18% (Day 19 of 110 total study days across the 22-week roadmap)

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
* Seed logic extracted from `Program.cs` into `Data/DbSeeder.cs`; deliberately kept as a runtime idempotent seeder rather than EF Core's `HasData()`, since real organic data already exists in the database (collision risk with fixed migration-baked IDs).
* `Evidence` entity added (`Domain/Evidence.cs`), linked to `Skill` via `EvidenceRecords` (one-to-many, same pattern as Day 6's Phase/Project/Milestone), migrated and seeded (2 example records tied to `Skill.Name` lookups, not fixed IDs).
* `EfSkillCatalog.GetAll()` updated with `.Include(s => s.EvidenceRecords)` — confirmed live, via the actual generated SQL (`LEFT JOIN`), that this is required for the navigation to populate at all.
* `Views/Skills/Index.cshtml` shows an Evidence count per skill.
* `ILogger<SkillsController>` introduced; `LogInformation` on successful Create/Edit, `LogWarning` on Edit-not-found.
* Existing (Day 5) `NotFound()` 404 behavior on `SkillsController.Edit` verified live for the first time (`curl` + console log), rather than just trusted from reading the code.
* `docs/daily-code-notes/day-09.md` created (Turkish).
* Independent task completed and verified: added a new `Evidence` row directly via SSMS (tied to `Git`); `/Skills` correctly showed the updated count with no code changes.
* Day 9 changes committed by Berkan (`5f3963d`).
* `Create.cshtml`/`Edit.cshtml` duplication (open since Day 5) refactored into a shared `Views/Skills/_SkillForm.cshtml` partial; behavior confirmed unchanged live (valid/invalid submission, pre-filled Edit form).
* Clean-build verification: `bin`/`obj` deleted across both projects, rebuilt from scratch — 0 errors, 0 warnings, 8/8 tests passing.
* Full end-to-end walkthrough: `/`, `/Home/Privacy`, `/Skills`, `/Skills/Create`, `/Skills/Edit/1`, `/Dashboard` all verified HTTP 200.
* `README.md` updated: real "how to run RoadmapOS" instructions, a "Known simplifications" section (Berkan added one entry independently), and a corrected "Current status".
* Phase 1 completion gate (`docs/ROADMAP.md`) checked item by item — all 7 confirmed, see `docs/daily-code-notes/day-10.md` for the table and evidence per item.
* **RoadmapOS V1 released.**
* `StockPilot.slnx` created — a separate solution from `RoadmapOS.slnx` (deliberate: different products shouldn't share a solution).
* `src/StockPilot.Api` scaffolded via `dotnet new webapi --use-controllers` (controller-based, not Minimal API, per `CLAUDE.md`).
* A known security advisory in the auto-generated `Microsoft.OpenApi` 2.0.0 dependency was caught from a build warning and fixed by upgrading `Microsoft.AspNetCore.OpenApi` to 10.0.11.
* `Models/ProductDto.cs` and `Controllers/ProductsController.cs` (`GetAll`, `GetById`) — first StockPilot vertical slice, in-memory data, verified via curl (200 for both actions, 404 for a missing ID).
* Discovered live: `[ApiController]` auto-formats `NotFound()`/similar results as RFC 9110 `ProblemDetails` JSON with no extra code — a preview of Week 3's later "ProblemDetails" topic.
* `docs/daily-code-notes/day-11.md` created (Turkish).
* Independent task completed and verified: `GetById(int id)` added, returns `Ok(product)` or `NotFound()` correctly for both a valid and an invalid ID.
* Day 11 changes committed and pushed by Berkan (`0f87acb`).
* `Models/Product.cs` (domain, mutable, server-assigned `Id`) and `Models/CreateProductRequest.cs` (input DTO) added, splitting what Day 11 had conflated into one `ProductDto`.
* `ProductsController.Create` added — `CreatedAtAction(nameof(GetById), ...)` returning 201 + `Location` header, verified live (`POST` → 201 + Location → `GET` on that exact Location → same resource, 200).
* A private `ToDto(Product)` mapping helper introduced; `GetAll`/`GetById` updated to use it — manual mapping, no AutoMapper.
* `_nextId++`'s non-thread-safety flagged explicitly as a known, deliberate simplification (superseded by SQL Server IDENTITY in Week 4), not silently ignored.
* A live compiler-error demonstration was used to answer a follow-up question: temporarily tried `Products.Select(ProductDto)` (a type name, not a method) to show `CS0119` directly, then reverted to `Select(ToDto)`.
* Independent task completed and verified: `Delete(int id)` added (`[HttpDelete("{id}")]`, `NoContent()`/`NotFound()`), tested live for both a valid and invalid ID — confirmed a DELETE request cannot be tested via a browser address bar (GET-only), curl (`-X DELETE`) used instead.
* `docs/daily-code-notes/day-12.md` created (Turkish).
* Day 12 changes committed and pushed by Berkan (`72fb9d2`).
* `CreateProductRequest` gained `[Required]`/`[StringLength]`/`[Range]` on positional record parameters; deliberately tested live *without* a `[property:]` target first (uncertain from memory whether needed) rather than assuming — confirmed it works as-is on .NET 10's validation pipeline.
* Confirmed live: invalid `Create` payloads (empty/too-long `Sku`, out-of-range `Price`) get an automatic 400 + `ValidationProblemDetails` from `[ApiController]`, with zero `if (!ModelState.IsValid)` code written.
* Noticed (not fixed): validation error messages carry the server's `tr-TR` culture too (e.g. "0,01" with a comma) — same class of issue as Day 8's CSS bug, out of scope today.
* `Program.cs`: `AddProblemDetails()` + `UseExceptionHandler()` added for global error handling.
* Verified live, twice: a temporary unhandled `throw` in `GetAll()` returns a clean `ProblemDetails` 500 with these enabled; with them deliberately disabled, the same throw returns a raw `text/plain` C# stack trace (file paths, internal ASP.NET Core call chain) — a genuine security-relevant difference, not just cosmetic.
* Full regression check after removing the temporary throw: `GetAll`/`GetById`/`Create`/`Delete`/invalid-`Create` all still correct.
* Independent task completed and verified: added `[MinLength(2)]` to `Sku`, confirmed a single-character SKU is rejected (400) and a two-character one is accepted (201).
* `docs/daily-code-notes/day-13.md` created (Turkish), including both the "with" and "without" global-error-handling transcripts.
* Discovered live: a naive test attempt against `ProductsController`'s old `static` product list produced an order-dependent failure (`Expected: 4, Actual: 5`) — direct proof that shared static state breaks test isolation.
* `Models/IProductStore.cs` + `InMemoryProductStore.cs` added — the same pattern as RoadmapOS's `ISkillCatalog`/`InMemorySkillCatalog`, this time motivated by a real, just-discovered testing problem rather than introduced speculatively.
* `ProductsController` refactored to constructor-inject `IProductStore` instead of holding a `static` list; `Program.cs` registers it `AddSingleton`.
* `tests/StockPilot.Api.Tests` created; `ProductsControllerTests.cs` — 7 tests (later 8 with the independent task), each building its own fresh `InMemoryProductStore`, confirmed fully order-independent.
* Full HTTP regression via curl after the refactor: `GetAll`/`GetById`/`Create`/`Delete`/invalid-`Create` all unchanged.
* `docs/daily-code-notes/day-14.md` created (Turkish), including the naive-attempt failure transcript.
* Independent task completed and verified: `Create_CalledTwiceOnSameStore_AssignsSequentialIds` — two `Create` calls on the *same* controller/store instance correctly get IDs 4 then 5 (a real typo, `created` vs `created1`, plus a path-resolution mixup running `dotnet build`/`dotnet test` from the wrong directory, were both debugged live before it passed).
* Day 14 changes committed and pushed by Berkan (`6464d64`).
* `Models/PagedResult.cs` added (generic paging envelope: `Items`, `Page`, `PageSize`, `TotalCount`).
* `ProductsController.GetAll` rewritten with `[FromQuery] search/sortBy/page/pageSize`, using LINQ `Where`/`OrderBy`/`Skip`/`Take` for the first time in this codebase.
* 3 existing tests updated for the new `PagedResult<ProductDto>` return shape (previously asserted a bare `List<ProductDto>`).
* Full regression: both `StockPilot.slnx` and `RoadmapOS.slnx` build and test clean independently (8/8 each).
* Live bug demonstration: temporarily reversed `Skip`/`Take` to `Take`/`Skip` — `page=2&pageSize=1` returned an empty result (`"items": []`) instead of the correct product, proving order matters; reverted immediately after, confirmed correct again.
* Noted in passing: editing a file open in the IDE via a terminal command (`sed`) can be silently overwritten by the IDE's own in-memory state — switched to the `Edit` tool for the live demo to avoid recurrence.
* Independent task completed and verified (found already done, ahead of being asked): `sortBy=sku` option added to the sort `switch`, confirmed live via `?sortBy=sku`.
* `docs/daily-code-notes/day-15.md` created (Turkish), including the Week 3 close-out summary.
* **Week 3 (StockPilot Inventory and Order API, first slice) complete.**
* EF Core packages added to `StockPilot.Api`; `StockPilotDb` connection string added (same `localhost\SQLEXPRESS` instance as RoadmapOS, separate `StockPilot` database).
* `Data/StockPilotDbContext.cs`, `Data/EfProductStore.cs`, `Data/DbSeeder.cs` added — mirroring RoadmapOS's `RoadmapOSDbContext`/`EfSkillCatalog`/`DbSeeder` pattern exactly.
* Real bug caught from an EF Core tooling warning (not silently ignored): no precision/scale specified for `Product.Price` risked silent truncation; fixed with `HasPrecision(18, 2)`, migration regenerated cleanly.
* `Program.cs`: DI registration for `IProductStore` switched from `AddSingleton<..., InMemoryProductStore>` to `AddScoped<..., EfProductStore>`; `InMemoryProductStore` kept (unregistered) for the existing tests, which still pass unchanged.
* Discussed and clarified: why `IProductStore`/`EfProductStore` pass the domain `Product` directly rather than a DTO — DTOs protect the boundary between the server and the outside world (HTTP request/response), not internal calls between a controller and its own storage abstraction; the DTO boundary is still fully intact at `Create`'s input (`CreateProductRequest`) and every action's output (`ProductDto`/`PagedResult<ProductDto>`).
* `docs/daily-code-notes/day-16.md` created (Turkish).
* Independent task completed and verified: a 5th product inserted directly via SSMS/raw SQL (`SKU-008`, "Headphones"); `/api/products` showed it automatically, with no code changes.
* Day 16 changes committed and pushed by Berkan (`e72936f`).
* `IProductStore` converted to async signatures (`Task<T>` + `CancellationToken`); `EfProductStore` uses real EF Core async methods (`ToListAsync`, `FindAsync`, `SaveChangesAsync`); `InMemoryProductStore` uses `Task.FromResult(...)` (no real I/O, satisfies the interface only).
* `ProductsController`'s four actions converted to `async Task<ActionResult<T>>`, each gaining a `CancellationToken` parameter (auto-populated by ASP.NET Core from `HttpContext.RequestAborted` — no attribute needed).
* All 8 existing tests converted to `async Task`; full HTTP regression confirmed identical behavior to the synchronous version.
* `docs/daily-code-notes/day-17.md` created (Turkish).
* Independent task completed and verified: a new test (`GetBySearch_ReturnsMatchingProducts`, IDE-suggested but explained line-by-line afterward) confirming the `search` filter returns exactly one matching product; 9/9 tests passing.
* Day 17 changes committed and pushed by Berkan (`d1bfff7`).
* Pre-check performed before adding the constraint: confirmed no existing duplicate SKUs in the live database (a real "verify before migrating" step).
* `StockPilotDbContext`: `entity.HasIndex(p => p.Sku).IsUnique();` added; `AddUniqueSkuIndex` migration created and applied.
* Live proof, two ways: a direct SQL `INSERT` of a duplicate SKU was rejected by SQL Server; a real `POST` with a duplicate SKU via the API was also rejected, but surfaced as an (incorrect) HTTP 500 rather than 409 Conflict — deliberately left unfixed today, mirroring RoadmapOS Day 6's "prove the constraint, defer graceful handling" pattern; database integrity confirmed intact after both attempts (still exactly one `SKU-001` row).
* `docs/daily-code-notes/day-18.md` created (Turkish).
* Independent task completed and verified: a new, non-conflicting SKU (`SKU-009`, id 10) added successfully, confirming the constraint only blocks duplicates, not normal inserts.
* Day 18 changes committed and pushed by Berkan (`38585b4`).
* `IProductStore.SkuExistsAsync` added (both implementations); `ProductsController.Create` now does a proactive existence check (Layer 1) plus a `try`/`catch (DbUpdateException)` safety net (Layer 2) for the rare concurrent-request race, returning `Conflict(...)` (409) from either layer instead of letting a 500 through.
* Honestly scoped: only Layer 1 is covered by an automated test (`InMemoryProductStore` never throws `DbUpdateException`, so Layer 2 can't be reached from a unit test) — Layer 2 is trusted based on Day 18's already-observed EF Core/SQL Server behavior, not independently verified today.
* Live verification: duplicate SKU via `POST` → HTTP 409 with a clear message; a fresh SKU → still HTTP 201.
* `docs/daily-code-notes/day-19.md` created (Turkish).
* Independent task completed and verified: a new regression test (`Create_NonDuplicateSku_StillSucceeds`) confirming the new checks don't affect normal, non-duplicate creates; 11/11 tests passing.
* Day 19 changes not yet committed (pending Berkan's confirmation).

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
* StockPilot lives in the same `DotNetCareerPath` workspace but its own solution (`StockPilot.slnx`), separate from `RoadmapOS.slnx` — different products shouldn't share a solution file.
* StockPilot uses controller-based Web API (`dotnet new webapi --use-controllers`), not Minimal API, per `CLAUDE.md`'s engineering rules.
* .NET 10's default `webapi` template ships Microsoft's built-in `AddOpenApi()`/`MapOpenApi()` (a raw OpenAPI JSON schema at `/openapi/v1.json`), not an interactive Swagger UI — that would require an additional package (Swashbuckle's UI layer or similar), not added yet since it wasn't needed for today's verification.

## Next action

1. Read all five required documents (`CLAUDE.md`, `docs/ROADMAP.md`, `docs/CURRENT_STATE.md`, `docs/REQUIREMENTS_MATRIX.md`, `docs/LEARNING_LOG.md`).
2. Begin Phase 2, Week 4, Day 19 (Thursday — tests, failures, production considerations): close the gap deliberately left open on Day 18 — `Create` should catch the duplicate-SKU failure and return `409 Conflict` instead of a raw 500, with a test proving it. Time permitting, this is also the natural day to touch transactions/optimistic concurrency (still unscoped from Week 4's list); exact scope to be finalized in the day's plan.
3. Wait for approval (`UYGULA`) before creating or editing any application files.

## Week 4 / Day 19 expected outcome

* `Create` returns `409 Conflict` (not 500) for a duplicate SKU, with a clear error body.
* A test proving this behavior (likely requiring a real EF Core exception to be triggered/caught, or an in-memory equivalent check — to be decided in the day's plan).
* Remaining Week 4 topics after this: transactions, optimistic concurrency, query analysis, stock-reservation rules (Friday close-out likely covers whichever is left).

