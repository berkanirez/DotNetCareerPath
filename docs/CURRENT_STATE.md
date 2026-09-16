# CURRENT_STATE.md — Source of Truth for Current Progress

This file reflects the actual current state of the learning journey. It must always match reality. Update it only after a day's Definition of Done is confirmed satisfied (see `CLAUDE.md`).

## Status snapshot

* **Setup phase:** Complete
* **Roadmap phase:** Phase 2 — StockPilot Inventory and Order API
* **Week:** 6 — in progress (final week of Phase 2)
* **Day:** 29 (complete)
* **Active project:** StockPilot Inventory and Order API
* **Status:** Mocking (Moq) introduced — a test for `ProductsController.Create`'s Layer 2 (`catch (DbUpdateException)`) closes a gap honestly flagged since Day 19 (previously provable only live, never by an automated test). Verified Red→Green by temporarily removing the `catch` block and watching the test genuinely fail. 27/27 tests passing.
* **Available study time:** 2 hours/day
* **Progress:** ~26% (Day 29 of 110 total study days across the 22-week roadmap)

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
* Clean-checkout verification: `bin`/`obj` deleted across all 4 projects, both solutions rebuilt from scratch — StockPilot 0 errors/warnings (11/11 tests), RoadmapOS 0 errors/warnings (8/8 tests).
* `ProductsController.Create` refactored: the duplicated `$"A product with SKU '{request.Sku}' already exists."` string extracted into a single `duplicateSkuMessage` local variable, reused by both `Conflict(...)` return paths (Layer 1 and Layer 2). Regression-checked afterward: 11/11 still passing, behavior unchanged.
* Week 4 status reviewed and documented: Days 16-19 confirmed complete; Update (PUT) endpoint + optimistic concurrency carried to Day 21; transactions/query analysis carried to Day 21 or 22 if needed; "stock-reservation rules" identified as dependent on a not-yet-built `Order` domain and deferred well beyond Week 4, not just to Day 21/22.
* `docs/daily-code-notes/day-20.md` created (Turkish).
* Independent task skipped today at Berkan's explicit request (move straight to the next session) — recorded honestly rather than omitted.
* Day 20 changes not yet committed (pending Berkan's confirmation).
* `Product.RowVersion` added (`byte[]`, mapped via `IsRowVersion()`); `AddProductRowVersion` migration created and applied — a real SQL Server `rowversion` column, not application data.
* `ProductDto` extended with `RowVersion` (so clients can read the token needed for a later update); `Models/UpdateProductRequest.cs` added (`Name`, `Price`, `RowVersion` — `Sku` deliberately excluded, out of today's scope).
* `IProductStore.UpdateAsync` added to both implementations: `EfProductStore` sets the client-supplied `RowVersion` as the tracked entity's `OriginalValue` before `SaveChangesAsync`, so a real concurrency conflict throws `DbUpdateConcurrencyException`; `InMemoryProductStore` ignores `rowVersion` (no real database to check against — an explicitly honest limitation, same class as Day 19's Layer 2 gap).
* `ProductsController.Update` (`[HttpPut("{id}")]`) added — `NotFound()`/`Conflict(...)` (409, on `DbUpdateConcurrencyException`)/`Ok(...)`.
* 2 new tests added (`Update_ExistingId_ReturnsUpdatedProduct`, `Update_MissingId_ReturnsNotFound`), both covering only the no-conflict path (honestly documented — real conflicts can't be produced by `InMemoryProductStore`).
* Live proof against the real database (`sqlcmd` + `curl`): captured a product's `RowVersion` via `GET`, touched the same row directly via SQL (bumping its `RowVersion`), then confirmed a `PUT` with the now-stale `RowVersion` returns HTTP 409 and a `PUT` with the fresh one returns HTTP 200; product restored to its original values afterward.
* Understanding questions needed a second, concrete pass (a named two-person timestamped example, plus a `git push`/`pull` analogy for optimistic vs. pessimistic concurrency) before landing; also clarified `DbUpdateConcurrencyException` is a built-in EF Core type and specifically a subclass of Day 19's `DbUpdateException`.
* `docs/daily-code-notes/day-21.md` created (Turkish).
* Independent task (an assertion proving `Sku` survives `Update` untouched) completed by Claude directly, at Berkan's explicit one-time request, rather than by Berkan himself; verified (13/13 passing).
* Day 21 changes committed by Berkan (`d681355`).
* `IProductStore.AddRangeAsync` added (both implementations); `EfProductStore.AddRangeAsync` wraps its loop of `AddAsync` calls in one explicit transaction (`BeginTransactionAsync`/`CommitAsync`), giving "all or nothing" atomicity across multiple otherwise-independent implicit transactions.
* `ProductsController`: `POST /api/products/bulk` added, returning `201` (no single `Location` header, since multiple resources are created) or `409` (via the same `DbUpdateException` catch pattern as single `Create`) if any item in the batch fails.
* `EfProductStore.GetAllAsync` gained `.AsNoTracking()` (read-only, never re-saved); `GetByIdAsync` deliberately left tracked, since `RemoveAsync` reuses it to find the entity it then deletes.
* Live bug demonstration: the transaction was temporarily removed, a 3-item batch with a duplicate SKU in the middle was posted, the API correctly returned 409 but the first item had still been permanently written to the database (proven via a direct query) — a real, live-triggered partial-commit bug. The transaction was restored, the leaked row cleaned up, and the same batch re-tested: this time nothing was persisted (full rollback), and a fully valid batch still succeeded normally (201, all items added).
* `AsNoTracking()`'s actual effect was proven live against the real database via a temporary test file (`TempChangeTrackerDemo.cs`, deleted immediately after): with it, `ChangeTracker.Entries().Count()` was 0; without it, it equaled the row count.
* 1 new test added (`BulkCreate_ValidRequests_AddsAllProducts`), honestly scoped to the no-conflict path only — `InMemoryProductStore`'s `AddRangeAsync` has no real transaction/rollback concept, so the atomic-rollback behavior is real and verified only against `EfProductStore`, live.
* `docs/daily-code-notes/day-22.md` created (Turkish).
* Understanding questions needed a full explanation from Claude (Berkan answered "bilmiyorum"/partial on 2 of 3) before landing: the `await using`-triggers-automatic-rollback mechanism, why `AsNoTracking()` is safe on `GetAllAsync` but not `GetByIdAsync`, and why a single `AddAsync` never needed an explicit transaction while `AddRangeAsync` does (one `SaveChangesAsync` call is already auto-wrapped in its own implicit transaction; three separate calls are three separate implicit transactions with no shared atomicity unless explicitly wrapped).
* Independent task: predicted an empty-array `POST /api/products/bulk` would throw `DbUpdateException`; live test showed it actually returns `HTTP 201` with an empty `[]` body (the `foreach` loop simply runs zero times — no database write is ever attempted, so nothing can be rejected). Incorrect prediction, corrected via live verification together; noted as an observed (not fixed) edge case — an empty batch might arguably deserve a `400 Bad Request` instead, out of today's scope.
* Day 22 code and `day-22.md` committed by Berkan (`c740af7`); this `CURRENT_STATE.md`/`LEARNING_LOG.md` update follows separately.
* **Week 4 is complete.** Days 16-22 covered: EF Core + SQL Server persistence, async conversion, unique index + two-layer 409 defense, optimistic concurrency (`RowVersion`), and transactions + query analysis (`AsNoTracking()`). "Stock-reservation rules" (originally on Week 4's topic list) remains explicitly deferred until an `Order` domain exists — not part of Week 4's closure.
* `Microsoft.AspNetCore.Authentication.JwtBearer` package added; `Jwt` config section (`Issuer`/`Audience`/`Key`/`ExpiryMinutes`) added to `appsettings.Development.json`, explicitly named/documented as a dev-only simplification (a real signing key belongs in user-secrets/Key Vault, not source control).
* `Models/LoginRequest.cs`/`LoginResponse.cs` added; `Controllers/AuthController.cs` added — a single hardcoded demo user (`admin`/`Passw0rd!`), password compared via `PasswordHasher<T>` (hashed, not plaintext), `POST /api/auth/login` issues a signed JWT (`Sub`/`Jti` claims, `Issuer`/`Audience`/expiry) on success.
* `Program.cs`: `AddAuthentication().AddJwtBearer(...)` (validates issuer, audience, signing key, and lifetime) + `AddAuthorization()`; `UseAuthentication()` added to the pipeline immediately before `UseAuthorization()` (order is load-bearing — `UseAuthorization()` only checks an already-populated `HttpContext.User`, it never validates tokens itself).
* `ProductsController.Delete` gained `[Authorize]` — the first protected endpoint in either codebase. Every other action remains deliberately open today; broader `[Authorize]` coverage and roles/policies are later Week 5 topics.
* Live proof: `DELETE` with no token → 401; login with a wrong password → 401; login with correct credentials → 200 + a real JWT; `DELETE` with a valid token against a missing id → 404 (not 401 — authentication passed, then normal business logic ran); a throwaway product created then deleted with a valid token → 204 (real successful delete).
* Honestly noted: `[Authorize]` is enforced by ASP.NET Core's middleware pipeline, which a unit test calling the controller method directly never goes through — `Delete_ExistingId_RemovesProductAndReturnsNoContent` and friends kept passing unchanged (14/14) specifically because they bypass the pipeline entirely; a real "returns 401" test would need a `WebApplicationFactory`-based integration test (Week 6's topic).
* `docs/daily-code-notes/day-23.md` created (Turkish).
* Understanding questions: 2 of 3 answered correctly and precisely unprompted (authentication populates the context from the JWT, authorization checks permission against it; `UseAuthentication()` must precede `UseAuthorization()` because otherwise the context needed for the permission check would never be filled) — the "why is JWT stateless" question needed an explanation (the token carries its own signed proof, verified cryptographically against the server's own key, with no database/session lookup needed).
* Independent task: temporarily added `[Authorize]` to `GetAll` to predict/verify a token-less `GET`'s status code. First reported as "biliyorum, 401 verecek" without having actually run it — held to the workspace's "predict, then verify live" standard (especially given Day 22's empty-array prediction had been wrong despite seeming obvious) and re-tested together live: confirmed genuinely `401`. The temporary attribute was then reverted, and the working tree confirmed to exactly match the already-pushed commit again.
* Day 23 code, `day-23.md`, and the (pending) Day 22 `CURRENT_STATE.md`/`LEARNING_LOG.md` update were all committed together by Berkan (`89cbf43`); this update (Day 23's actual completion entries) follows separately.
* `Models/IRefreshTokenStore.cs`/`InMemoryRefreshTokenStore.cs` added (Singleton DI, same reasoning as RoadmapOS Day 3's `InMemorySkillCatalog`) — `Issue(username)` generates an opaque random token; `TryConsume(token, out username)` atomically finds-and-deletes it (`ConcurrentDictionary.TryRemove`), which is what makes rotation automatic rather than a separate invalidation step.
* `Models/RefreshTokenRequest.cs` added; `LoginResponse` extended with a `RefreshToken` field.
* `AuthController`: JWT-building logic extracted into a private `GenerateAccessToken` helper (reused by both `Login` and the new `Refresh` action); `POST /api/auth/refresh` added — consumes a refresh token and issues a brand-new access+refresh pair, or `401` if the token is invalid/expired/already used.
* Live proof: refreshing with token RT1 issued a new pair (RT2); reusing RT1 a second time correctly returned `401` (rotation confirmed); the freshly-issued RT2 worked normally (proving RT1's failure wasn't a general bug).
* Real, unplanned bug found and fixed live: temporarily shortening the access token's expiry to prove real-time expiry showed the token still being accepted 7 seconds past its 5-second lifetime — traced to ASP.NET Core's JWT bearer default `ClockSkew` of 5 minutes (a deliberate tolerance for clock drift between servers). Fixed permanently with `ClockSkew = TimeSpan.Zero` in `Program.cs`; re-verified live (expired token → 401, freshly-refreshed token → 204 real delete).
* `docs/daily-code-notes/day-24.md` created (Turkish); a supplementary, extremely detailed line-by-line walkthrough (`day-24-refresh-akisi-detay.md`, Turkish) was also created after the first explanation didn't land, plus a "Bonus" section on adding custom claims (e.g. nickname/phone) — where they'd be added, where the data would come from, how to read them back via `User.FindFirst(...)`, and a security note that JWT claims are signed but not encrypted (readable by anyone holding the token).
* Understanding questions: all 3 answered by Claude directly, at Berkan's explicit request ("Sen cevapla... bana yaptırma") rather than by Berkan himself — covering why `TryRemove` (not `TryGetValue`) makes rotation automatic, why `ClockSkew` defaults to 5 minutes (clock drift tolerance) rather than zero, and why `IRefreshTokenStore` must be `Singleton` (state needs to survive across separate requests) unlike `IProductStore`'s `Scoped` (a `DbContext`'s thread-safety requirement).
* Independent task (also completed by Claude directly, at the same explicit request): `InMemoryRefreshTokenStore`'s fixed 7-day `Lifetime` was refactored into an optional constructor parameter (`TimeSpan? lifetime = null`, defaulting to 7 days) purely so an expired-token test could use a negative `TimeSpan` to make a token expire the instant it's issued, with no real waiting. 4 new tests added (`tests/StockPilot.Api.Tests/InMemoryRefreshTokenStoreTests.cs`): valid token, unknown token, reused token (rotation), and expired token. Live-verified afterward that DI still resolves the store correctly with its real 7-day production default (login still works end-to-end). 18/18 tests passing.
* Day 24 code + docs committed by Berkan (`85e0feb`).
* `AuthController.DemoUsers` extended from one hardcoded account to two (`admin`→`Admin` role, `employee`→`Employee` role), each with its own hashed password; `GenerateAccessToken` gained a `role` parameter and adds a `ClaimTypes.Role` claim to the JWT (the specific claim type ASP.NET Core's `[Authorize(Roles = "...")]` checks by default — a made-up claim name would never be recognized).
* `Refresh` looks the role back up from `DemoUsers` by username (`IRefreshTokenStore` only ever stored a username, never a role, since roles didn't exist when it was built on Day 24) — a side benefit noted: this means a refreshed token always carries the user's *current* role, never a stale one baked in at original login time.
* `ProductsController.Delete`: `[Authorize]` → `[Authorize(Roles = "Admin")]` — the first role-restricted endpoint in either codebase.
* `tests/StockPilot.Api.Tests/AuthControllerTests.cs` added (4 tests) — a `[Theory]`/`[InlineData]` test decodes the JWT `Login` produces (via `JwtSecurityTokenHandler().ReadJwtToken`, not `ValidateToken`, since it's our own just-issued token) and asserts the correct role claim for both demo accounts; 2 more cover invalid/unknown-username login attempts.
* Live proof: `employee` login + `DELETE` → `403 Forbidden` (identity known, permission denied) — not `401`; `employee`'s `GET` on the same product still succeeded (no regression, restriction is scoped to `Delete` only); `admin` login + `DELETE` on the same product → `204` (real successful delete).
* `docs/daily-code-notes/day-25.md` created (Turkish).
* Understanding questions: Q1 (401 vs 403) was initially misunderstood — Berkan reasonably expected "employee has no permission" to mean 401, since "Unauthorized" sounds like "no permission"; corrected by explaining HTTP's actual (widely confusing) semantics — 401 really means "I don't know who you are," 403 means "I know exactly who you are, but no" — via a building-security-badge analogy. Q2 and Q3 were unknown, both explained by Claude (where `HttpContext.User`'s role information is populated and by what, and why `Refresh` re-reads the role from `DemoUsers` rather than from the refresh token store).
* Independent task (extending `[Authorize(Roles = "Admin")]` to `BulkCreate`) was declined by Berkan — explicitly stated it was understood well enough to visualize without needing to actually implement it. Recorded honestly rather than silently marked complete.
* Follow-up, still within the same session: two supplementary Turkish explainer documents were created at Berkan's request after the code explanation didn't fully land — a comprehensive, jargon-minimized, file-by-file walkthrough of the *entire* JWT system end to end (`jwt-genel-akis-basit-anlatim.md`, using a "wristband" analogy throughout, covering `appsettings.json` → `AuthController` → `Program.cs` → `ProductsController`), and a follow-up chat explanation (not yet written to a file) of exactly what `HttpContext.User` contains (a `ClaimsPrincipal` holding a list of `Claim` objects mirroring the JWT's own claims, plus the fact that it is never `null` — an unauthenticated request gets an empty, `IsAuthenticated = false` principal, not a missing one).
* Day 25 code, tests, and both new doc files committed and pushed by Berkan (`f7d38c8`); this `CURRENT_STATE.md`/`LEARNING_LOG.md` update follows separately.
* `Program.cs`: a single named authorization policy (`CanManageProducts`, defined via `AddAuthorization(options => options.AddPolicy(...))`, currently just `RequireRole("Admin")`) replaces the raw role string previously repeated in `[Authorize(Roles = "Admin")]`.
* `ProductsController`: `Delete` and `BulkCreate` both now use `[Authorize(Policy = "CanManageProducts")]` — `BulkCreate`'s restriction is the completion, via a policy this time, of the independent task Berkan declined on Day 25.
* Live proof (both directions): `employee` gets `403` on both `Delete` and `BulkCreate`; `admin` succeeds on both. Then, with zero changes to `ProductsController.cs`, `Program.cs`'s single policy definition was temporarily broken (`RequireRole("Admin")` → `RequireRole("SuperAdmin")`) and both endpoints simultaneously started rejecting `admin` too (`403`) — concretely proving the "change once, apply everywhere" benefit of naming a policy instead of repeating a role string. Reverted and re-verified back to normal afterward.
* Clarified via a follow-up question: `RequireRole` is a built-in `AuthorizationPolicyBuilder` method (`Microsoft.AspNetCore.Authorization`), not something written in this project; role names are not a registered/closed set anywhere — a role is nothing more than a string value carried in a claim, so `RequireRole("Customer")` would compile and run perfectly even though no token this app issues ever carries that value, silently rejecting everyone forever rather than raising any error. Flagged as a real, easy-to-hit typo trap (a misspelled role name fails silently, not loudly).
* `docs/daily-code-notes/day-26.md` created (Turkish).
* Week 5 close-out: reviewed as a checklist that every "security failure case" on `docs/ROADMAP.md`'s Week 5 topic list was already live-proven across Days 23-26 (no token → 401, wrong credentials → 401, expired token → 401, invalid/reused refresh token → 401, insufficient role → 403) — no separate day needed for that topic.
* Understanding questions: all 3 answered correctly and precisely, unprompted (usage convenience of a single change point across multiple endpoints; confirmed understanding of why no controller edit was needed for the live "break the policy" proof; a correct, concise restatement of the 401-vs-403 distinction from Day 25).
* **Week 5 is complete.** Days 23-26 covered: JWT access tokens (auth vs. authz), refresh tokens with rotation (plus a real, live-discovered `ClockSkew` bug fixed permanently), role-based authorization, and policy-based authorization — closing with a full security-failure-case review rather than a separate day.
* `Program.cs`: `public partial class Program { }` added at the end — a pure visibility fix (top-level statements otherwise generate an `internal` `Program` class) so the test project's `WebApplicationFactory<Program>` can reference it from outside the assembly; changes no runtime behavior.
* `tests/StockPilot.Api.Tests/StockPilot.Api.Tests.csproj`: `Microsoft.AspNetCore.Mvc.Testing` package added.
* `tests/StockPilot.Api.Tests/ProductsAuthorizationIntegrationTests.cs` added — the first tests in either codebase that exercise the real HTTP pipeline (via `WebApplicationFactory`/`IClassFixture`) rather than calling a controller method directly: real `401` (no token), real `403` (employee role), real `204` (admin deletes a throwaway product it creates and cleans up itself, using a unique per-run SKU so it never collides with seeded data or other test runs).
* A real bug caught live while first writing these tests (not staged): the generated unique SKU (`"SKU-INTEGRATION-TEST-" + Guid:N`) was 53 characters, exceeding `CreateProductRequest.Sku`'s `[StringLength(50)]` (in place since Day 12) — the create step genuinely returned `400 Bad Request`. Fixed with a shorter prefix.
* `docs/daily-code-notes/day-27.md` created (Turkish).
* Understanding questions: Q1 and Q2 answered correctly and precisely, unprompted (integration tests use a real HTTP client through the real pipeline, unlike unit tests calling the controller directly; without the `public partial class Program {}` marker, the test project could not reference the otherwise-`internal` generated `Program` type). Q3 (the exact character-count math behind the 400) was attempted but incomplete — clarified with a precise breakdown (21-character prefix + 32-character `Guid:N` = 53, vs. the 50-character limit).
* Independent task (adding a `GetAll_NoToken_ReturnsOk` integration test proving an unrestricted endpoint really does return 200 without a token) completed correctly by Berkan himself, at his own request for step-by-step guidance rather than Claude writing it — passed on the first run, following the existing tests' pattern correctly (right HTTP method, right route, right expected status code). 26/26 tests passing.
* Deliberately not addressed today: these integration tests run against the real, shared `StockPilotDb` (same `appsettings.Development.json` as every other demo) rather than an isolated test database — test-database isolation (likely via Testcontainers) is a separate, later Week 6 topic.
* `Testcontainers.MsSql` package added to the test project; `tests/StockPilot.Api.Tests/StockPilotApiFactory.cs` added — a custom `WebApplicationFactory<Program>` subclass implementing `IAsyncLifetime`: `InitializeAsync()` starts a real, disposable SQL Server container, applies the app's real EF Core migrations to it, and seeds it via the same `DbSeeder` the real app uses; `ConfigureWebHost` removes the app's real `StockPilotDbContext` registration and re-adds it pointed at the container instead; `DisposeAsync()` (declared with `new`, not `override`, since it must satisfy `IAsyncLifetime`'s `Task`-returning signature while `WebApplicationFactory`'s own `IAsyncDisposable.DisposeAsync()` returns `ValueTask` — two same-named, incompatible signatures) tears the container down and still explicitly invokes the base class's own cleanup via an interface cast.
* `ProductsAuthorizationIntegrationTests` switched from `IClassFixture<WebApplicationFactory<Program>>` to `IClassFixture<StockPilotApiFactory>` — no change to any test body.
* Live proof: during a test run, `docker ps` showed a transient SQL Server container (plus Testcontainers' own "Ryuk" cleanup watchdog); after the run, `docker ps -a` showed the container fully removed, not merely stopped; `sqlcmd` against the real `StockPilotDb` confirmed its row count was identical (7) before and after the full test run — it is no longer touched at all.
* Understanding questions: all 3 ("bilmiyorum") explained by Claude — why `IAsyncLifetime`'s setup/teardown belongs at the fixture level rather than inside each `[Fact]` (an expensive resource like a container should be started once per class, not once per test); why the real `DbContext` registration must be removed before re-adding rather than "overwritten" (`IServiceCollection` has no overwrite operation — re-adding without removing would leave two ambiguous registrations); which two `DisposeAsync()` signatures clashed (`WebApplicationFactory`'s inherited `ValueTask DisposeAsync()` from `IAsyncDisposable` vs. `IAsyncLifetime`'s required `Task DisposeAsync()`).
* Independent task, done live together: a temporary `throw new Exception("test")` was added inside `StockPilotApiFactory.InitializeAsync()`; all 4 tests in the class failed instantly, each with the identical exception and stack trace pointing at that one line, none reaching their own actual logic — concretely proving `InitializeAsync()` acts as a genuine gatekeeper for the whole test class. Reverted; 26/26 passing again.
* `docs/daily-code-notes/day-28.md` created (Turkish).
* `Moq` package added to the test project; `tests/StockPilot.Api.Tests/ProductsControllerMockingTests.cs` added — `Create_StoreThrowsDbUpdateException_ReturnsConflict` mocks `IProductStore` (not EF Core itself, per `CLAUDE.md`'s rule) so `AddAsync` throws a `DbUpdateException` on demand, proving `Create`'s Layer 2 `catch` block for the first time via an automated test rather than only live curl.
* Live Red→Green proof: the `catch (DbUpdateException)` block was temporarily removed from `ProductsController.Create`; the test genuinely failed (the mocked exception propagated out of the test itself); the block was restored and the test passed again.
* A real, live-verified nuance discovered together: temporarily removing the test's `SkuExistsAsync` setup still left the test passing, because Moq's loose-mock default for an unconfigured method returning `Task<bool>` is a completed Task wrapping `false` — coincidentally already what the test needed. The explicit `.Setup(...)` was kept anyway (not strictly load-bearing here, but avoids silently depending on a library default).
* `docs/daily-code-notes/day-29.md` created (Turkish).
* Understanding questions: all 3 ("bilmiyorum") explained by Claude — the mock-vs-fake distinction (a fake has real, working logic; a mock is an empty shell that only does what's explicitly scripted via `Setup`); why `SkuExistsAsync` was set up even though (as verified live) it wasn't strictly necessary; why `InMemoryProductStore` can never produce this test's scenario (its `AddAsync` has no `throw` statement anywhere in it — a fake can only do what its own code says, and its code contains no path to a `DbUpdateException`).
* Independent task (mirroring the same mocking pattern for `Update`'s Day 21 `DbUpdateConcurrencyException` catch block) declined by Berkan — recorded honestly rather than marked complete.

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
2. Begin Phase 2, Week 6, Day 30: GitHub Actions (CI) — the next topic on Week 6's list (`docs/ROADMAP.md`: xUnit, mocking, unit testing, integration testing, `WebApplicationFactory`, Testcontainers, test-database isolation, GitHub Actions, API documentation, portfolio polish — everything through mocking is now done).
3. Wait for approval (`UYGULA`) before creating or editing any application files.

## Week 6 / Day 30 expected outcome

* A GitHub Actions workflow that builds both solutions and runs the full test suite (including the Testcontainers-backed integration tests — GitHub Actions' hosted runners have Docker available, so this should work without changes) on push/PR.
* Live proof: a real push triggers a real, passing (or deliberately-broken-then-fixed) CI run.
* Full regression across both solutions; API documentation and portfolio-polish topics remain for the final Week 6 day(s).

