---

description: "Task list for Product Filters & Forgiving Search"
---

# Tasks: Product Filters & Forgiving Search

**Input**: Design documents from `/specs/003-product-filters-search/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/openapi.yaml](./contracts/openapi.yaml),
[quickstart.md](./quickstart.md)

**Tests**: **REQUIRED.** Constitution Principle I makes TDD non-negotiable, and its workflow
section requires every implementation task to be preceded by a failing-test task. Test tasks below
must be written and seen to fail before the task that greens them.

**Organization**: One phase per user story, in priority order, each independently shippable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable — different files, no dependency on an incomplete task
- **[Story]**: US1, US2, US3 from spec.md
- Every task names its file

## Path Conventions

Web application, existing tree: `backend/src/`, `backend/tests/`, `frontend/src/`,
`frontend/tests/`. All paths are repository-relative and real.

---

## Phase 1: Setup

**Purpose**: Know the baseline and be able to recover it.

- [X] T001 Stop any running API so build output is not locked: `taskkill /IM MoizPos.Api.exe /F` (ignore "not found") — Visual Studio and `dotnet run` cannot both hold `backend/src/MoizPos.Api/bin/`
- [X] T002 Record the regression baseline: `cd backend && dotnet test` and `cd frontend && npm run test && npx tsc --noEmit` must show **550 backend** (214 unit + 328 integration + 8 architecture) and **263 frontend** passing before any change (constitution VI)
- [X] T003 Back up the live database: `mysqldump -u root moizpos > backup-before-0016.sql`

**Checkpoint**: Baseline recorded and recoverable.

---

## Phase 2: Foundational

**Purpose**: Schema and types for the local flag. **Blocks US3 only** — US1 and US2 need no schema
and may start in parallel with this phase.

- [X] T004 Create migration `backend/src/MoizPos.Migrator/Scripts/0016_brand_is_local.sql` adding to `brands` the column `is_local BOOLEAN NOT NULL DEFAULT FALSE` (data-model §1 — `NOT NULL` so every brand is exactly Local or Imported; `DEFAULT FALSE` so every existing brand starts Imported and nothing is misreported as local)
- [X] T005 [P] Add `public bool IsLocal { get; set; }` to `Brand` in `backend/src/MoizPos.Domain/Entities/Entities.cs`
- [X] T006 Run `dotnet run --project backend/src/MoizPos.Migrator`, then verify `SHOW COLUMNS FROM moizpos.brands LIKE 'is_local'` shows NOT NULL default 0 and `SELECT COUNT(*) FROM moizpos.brands WHERE is_local = TRUE` returns 0
- [X] T007 Run `cd backend && dotnet test` to confirm the new column reddened nothing

**Checkpoint**: Local flag exists; no behaviour has changed yet.

---

## Phase 3: User Story 1 — Search the way the shopkeeper types (Priority: P1) 🎯 MVP

**Goal**: Every typed word must appear in the product's name, model, brand or category — in any
order, ignoring case, spaces, hyphens and punctuation, tolerating a trailing plural "s".

**Independent Test**: With "Type-C Braided Cable" (Baseus, Cables) in stock, each of `c type`,
`type c`, `type-c`, `typec`, `baseus cable` and `cable baseus` finds it; `oppo charger` finds only
the Oppo charger.

### Tests for User Story 1 ⚠️ Write first, watch them fail

- [X] T008 [P] [US1] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Products/ProductSearchTermsTests.cs` for `ProductSearchTerms.Parse` (data-model §3): `"c type"`, `"type-c"`, `"TYPE-C"` and `"  type   c  "` each yield words `type`,`c` (order-insensitive); `"typec"` yields `typec`; `"Chargers"` yields `charger`; `"bus"` stays `bus` (the plural rule applies only to words of **≥ 4 characters**); `"20-W"` yields `20`,`w`; `"cable cable"` yields one word; 9 words are capped at **8**; every resulting word matches `^[a-z0-9]+$` so nothing meaningful to SQL or `LIKE` survives (try `"50%_off' --"`); empty and whitespace-only input yields no words; `IsTooShort` is true for `"c"` and `"c t"` and false for `"c type"`
- [X] T009 [P] [US1] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Products/ProductSearchTests.cs` seeding the quickstart catalogue (Type-C Braided Cable / Baseus / Cables; Charger 20W Fast / Oppo / Chargers; Charger 18W / Samsung / Chargers) with unique suffixes, asserting SC-022: `c type`, `type c`, `type-c`, `typec`, `Type C` all return the Type-C cable (FR-077)
- [X] T010 [P] [US1] Add failing tests to `backend/tests/MoizPos.IntegrationTests/Products/ProductSearchTests.cs` for words matching different fields and order (FR-075, FR-076): `oppo` finds the Oppo charger; `charger` and `chargers` both find both chargers (FR-078); `oppo charger` and `charger oppo` find **only** the Oppo charger (SC-023); `baseus cable` finds the cable
- [X] T011 [P] [US1] Add failing tests to `backend/tests/MoizPos.IntegrationTests/Products/ProductSearchTests.cs` for data-model §6 invariants: adding a word never widens (`charger` ⊇ `charger samsung`); `charger xyz` returns an empty list with 200; a product with **no brand** is still found by name and category; a full barcode still returns that exact product (FR-080)
- [X] T012 [P] [US1] Add failing tests to `backend/tests/MoizPos.IntegrationTests/Products/ProductSearchTests.cs` for FR-079: `search=c` and `search=c t` return **400** with code `VALIDATION_FAILED` and message "Type at least 2 letters to search."; `search=c type` returns 200
- [X] T013 [P] [US1] Add a failing test to `backend/tests/MoizPos.IntegrationTests/Products/ProductSearchTests.cs` asserting FR-089 for search: the same search as Staff returns the same product ids as Admin, and the Staff response body contains no `costPrice`
- [X] T014 [P] [US1] Write a failing component test in new file `frontend/tests/features/pos/PosPage.test.tsx` (the one-letter handling lives in `PosPage`, not the pure `PosScreen`) asserting that when product search rejects with a 400 `VALIDATION_FAILED`, `findProduct` resolves `null` so the counter shows "No product found" rather than an error (research R3, R6)

### Implementation for User Story 1

- [X] T015 [US1] Add `SearchTooShortException : DomainException` in `backend/src/MoizPos.Domain/Errors/DomainExceptions.cs` with code `ErrorCodes.ValidationFailed` and message "Type at least 2 letters to search." — the existing middleware maps any `DomainException` to 400, so no middleware change is needed
- [X] T016 [US1] Create `backend/src/MoizPos.Application/Calculations/ProductSearchTerms.cs`: a pure static `Parse(string? text)` returning `IReadOnlyList<string> Words` and `bool IsTooShort`, implementing data-model §3 exactly — split on `[^A-Za-z0-9]+`, lower-case, strip one trailing `s` from words of length ≥ 4, drop empties, de-duplicate preserving first occurrence, take at most 8. Greens T008
- [X] T017 [US1] In `backend/src/MoizPos.Application/Services/ProductService.cs`, parse `query.Search` with `ProductSearchTerms` in `SearchAsync`; throw `SearchTooShortException` when `IsTooShort`; pass the words to the repository (extend `ProductQuery` in `backend/src/MoizPos.Application/Abstractions/IProductAbstractions.cs` with `IReadOnlyList<string> SearchWords`, leaving `Search` for the exact barcode comparison)
- [X] T018 [US1] Rewrite the search predicate in `backend/src/MoizPos.Infrastructure/Repositories/ProductRepository.cs` per data-model §4: for each word `i`, `(REGEXP_REPLACE(LOWER(p.name),'[^a-z0-9]','') LIKE @wi OR REGEXP_REPLACE(LOWER(p.model),'[^a-z0-9]','') LIKE @wi OR REGEXP_REPLACE(LOWER(b.name),'[^a-z0-9]','') LIKE @wi OR REGEXP_REPLACE(LOWER(c.name),'[^a-z0-9]','') LIKE @wi)` with `@wi = %word%` bound as a parameter, all words joined by AND, and the whole joined group OR'd with `p.barcode = @exactSearch`. Fields are normalised **separately, never concatenated** (research R1). Apply the same WHERE to the COUNT query. Greens T009–T013
- [X] T019 [US1] In `frontend/src/features/pos/PosPage.tsx`, catch an `ApiError` with status 400 from the name search in `findProduct` and return `null`. Greens T014
- [X] T020 [US1] In `frontend/src/features/products/ProductsPage.tsx`, when every typed word is a single character, show the hint "Type at least 2 letters to search" and do not send the search (keep the previous list); extract the check as a small pure helper so it is unit-tested with the component

**Checkpoint**: Forgiving search works on Products, POS and Purchases. This is the MVP.

---

## Phase 4: User Story 2 — Filter by brand and by category (Priority: P2)

**Goal**: Brand and Category filters on the Products screen, individually or combined with each
other and with search, with a Clear button and an explicit empty-result message.

**Independent Test**: Brand = Oppo shows only Oppo; add Category = Chargers shows only Oppo
chargers; Clear filters restores the full list and keeps the typed search.

### Tests for User Story 2 ⚠️ Write first, watch them fail

- [X] T021 [P] [US2] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Products/ProductFilterTests.cs` proving the existing API combines filters with search by AND (FR-083): `brandId` alone; `categoryId` alone; `brandId` + `categoryId`; `categoryId` + `search`; a combination matching nothing returns an empty 200. These are expected to pass against the existing API — any failure is a real defect to fix, not a test to adjust
- [X] T022 [P] [US2] Add a failing test to `backend/tests/MoizPos.IntegrationTests/Products/ProductFilterTests.cs` for FR-090: a product's `salePrice` is identical with and without `brandId`/`categoryId`, and changes only with `saleType=Wholesale`
- [X] T023 [P] [US2] Write failing component tests in new file `frontend/tests/features/products/ProductFilters.test.tsx` rendering `ProductsPage` with mocked `productApi`, `categoryApi` and `brandApi`: Brand and Category selects are present and populated A→Z from active rows (FR-085); choosing a brand calls `productApi.search` with that `brandId`; choosing both sends both; typed search is sent alongside them (FR-083)
- [X] T024 [P] [US2] Add failing tests to `frontend/tests/features/products/ProductFilters.test.tsx`: **Clear filters** is absent with no filter set, appears once one is set, resets both selects, and leaves the search box text unchanged (FR-084); an empty result with a filter set shows "No products match the chosen filters" rather than an empty table (FR-086)

### Implementation for User Story 2

- [X] T025 [US2] Add Brand and Category `<select>` controls to the filters area of `frontend/src/features/products/ProductsPage.tsx`, populated from `categoryApi.search({ pageSize: 200 })` and `brandApi.search({ pageSize: 200 })` with an "All brands" / "All categories" first option; include `brandId` and `categoryId` in the TanStack Query key and in the `productApi.search` call. Greens T023
- [X] T026 [US2] Add the **Clear filters** button and the filter-aware empty message to `frontend/src/features/products/ProductsPage.tsx`, passing the message as `emptyMessage` to the existing `QueryState`. Greens T024
- [X] T027 [US2] **No backend change was required.** All six tests in `ProductFilterTests.cs` passed on first run against the existing API, confirming the brand and category filters already worked on the server and only the Products screen lacked them

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: User Story 3 — Filter local brands on their own (Priority: P3)

**Goal**: The owner marks a brand Local or Imported; the Products screen's **Local brands only**
filter lists products of Local brands, never unbranded products, combinable with everything else.

**Independent Test**: Mark one brand Local; tick Local brands only; only its products show, and an
unbranded product does not.

### Tests for User Story 3 ⚠️ Write first, watch them fail

- [X] T028 [P] [US3] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Taxonomy/CategoryAndBrandTests.cs`: creating a brand without `isLocal` stores Imported (`isLocal: false`, FR-087a default); creating with `isLocal: true` stores Local; updating flips it and the read reflects it immediately (SC-027); Staff still cannot create or update a brand
- [X] T029 [P] [US3] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Products/ProductFilterTests.cs` for `localOnly` (FR-087, FR-088): only products of Local brands return; an **unbranded** product is never returned; `localOnly` + `categoryId` and `localOnly` + `search` combine by AND; `localOnly` + `brandId` of an Imported brand returns empty 200; with no brand marked Local the result is empty
- [X] T030 [P] [US3] Add a failing test to `backend/tests/MoizPos.IntegrationTests/Products/ProductFilterTests.cs` that `brandIsLocal` appears on the product list for Staff and Admin, is `false` for an unbranded product, and that the architecture tests in `backend/tests/MoizPos.ArchitectureTests/StaffDtoExposureTests.cs` still pass with the new field
- [X] T031 [P] [US3] Add failing tests to `frontend/tests/features/taxonomy/TaxonomyPage.test.tsx`: the Brands screen shows a "Local brand" tick in its form and a Local/Imported marker in its table, saving sends `isLocal`; the **Categories** screen shows neither
- [X] T032 [P] [US3] Add failing tests to `frontend/tests/features/products/ProductFilters.test.tsx`: ticking **Local brands only** calls `productApi.search` with `localOnly: true`; it combines with the Category select and the search; **Clear filters** also unticks it

### Implementation for User Story 3

- [X] T033 [P] [US3] Add `bool IsLocal` to `BrandDto` and `BrandUpsertRequest` (default `false`) in `backend/src/MoizPos.Application/Contracts/Taxonomy/TaxonomyContracts.cs`
- [X] T034 [US3] Read and write `is_local` in `backend/src/MoizPos.Infrastructure/Repositories/BrandRepository.cs` (SELECT, INSERT, UPDATE) and map it in `backend/src/MoizPos.Application/Services/BrandService.cs` for create, update and the DTO projection. Greens T028
- [X] T035 [US3] Add `bool LocalOnly` to `ProductQuery` and `bool BrandIsLocal` to `ProductRow` in `backend/src/MoizPos.Application/Abstractions/IProductAbstractions.cs`, and `BrandIsLocal` to `ProductStaffDto` in `backend/src/MoizPos.Application/Contracts/Products/ProductContracts.cs`; project it in both branches of `ProductService.Project`
- [X] T036 [US3] In `backend/src/MoizPos.Infrastructure/Repositories/ProductRepository.cs`, select `COALESCE(b.is_local, FALSE) AS BrandIsLocal` and add `AND b.is_local = TRUE` to the WHERE when `LocalOnly` is set — through the existing `LEFT JOIN brands`, which by construction excludes unbranded products (data-model §5)
- [X] T037 [US3] Accept `[FromQuery] bool localOnly = false` in `backend/src/MoizPos.Api/Controllers/ProductsController.cs` and pass it into `ProductQuery`. Greens T029–T030
- [X] T038 [P] [US3] Add `isLocal` to `TaxonomyItem` and `TaxonomyUpsert` in `frontend/src/features/taxonomy/taxonomyApi.ts`, and `localOnly` / `brandIsLocal` to `ProductSearchParams` / `Product` in `frontend/src/features/products/productApi.ts`
- [X] T039 [US3] In `frontend/src/features/taxonomy/TaxonomyPage.tsx`, when `resource === 'brands'` only, add a "Local brand" tick to the form (defaulting unticked, prefilled when editing) and a Local/Imported marker column to the table. Greens T031
- [X] T040 [US3] Add the **Local brands only** tick to `frontend/src/features/products/ProductsPage.tsx`, included in the query key and search call, and reset by **Clear filters**. Greens T032

**Checkpoint**: All three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T041 Add a multi-word performance test to `backend/tests/MoizPos.IntegrationTests/Performance/PerformanceTests.cs`: against the existing 5,000-product catalogue, a three-word search (`baseus cable m`) completes in under **one second** (SC-025). If it fails, stop and apply the stored-column fallback documented in plan.md Complexity Tracking rather than loosening the bound
- [X] T042 Run the full regression gate: `cd backend && dotnet test` and `cd frontend && npm run test && npx tsc --noEmit`; nothing from the T002 baseline of 550 / 263 may be reddened (constitution VI)
- [X] T043 Run `cd frontend && npx vite build` to confirm the production bundle builds
- [X] T044 Execute `quickstart.md` V1–V17 against the live database with both servers running, recording the actual outcome of each check
- [X] T045 [P] Add a **Product search** section to `CLAUDE.md`: words are matched individually against separately normalised fields, never a concatenation; the plural rule applies only to ≥ 4-character words; search is shared by Products, POS and Purchases, so a change to it is a change to all three
- [X] T046 [P] Add a **Local brands** note to the Categories and Brands section of `CLAUDE.md`: `is_local` defaults to Imported, and unbranded products are deliberately excluded from the Local filter
- [X] T047 [P] Add a row to the traps table in `CLAUDE.md` only for a bug actually hit during implementation; leave it unchanged if none
- [X] T048 Remove the quickstart test catalogue from the live database and reset any brand marked Local only for V13
- [X] T049 Mark every completed task `[X]` in this file and record final test counts and any deviations

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (1)**: none
- **Foundational (2)**: needs Setup. **Blocks US3 only.**
- **US1 (3)**: needs Setup. Independent of Phase 2 — no schema involved
- **US2 (4)**: needs Setup. Independent of Phase 2. Touches `ProductsPage.tsx`, so sequence after
  T020 to avoid conflicting edits to that file
- **US3 (5)**: needs Phase 2. Touches `ProductsPage.tsx` and `ProductRepository.cs`, so sequence
  after US1 and US2
- **Polish (6)**: needs every story being shipped

### Critical path

`T008 → T016 → T018` — the word rules and the predicate that uses them. Everything visible in US1
follows from those three.

### Shared files (why some stories are sequential)

| File | Touched by |
|---|---|
| `ProductRepository.cs` | US1 (T018), US2 (T027 if needed), US3 (T036) |
| `ProductsPage.tsx` | US1 (T020), US2 (T025, T026), US3 (T040) |
| `ProductFilters.test.tsx` | US2 (T023, T024), US3 (T032) |

### Parallel opportunities

- **T005** alongside T004 once the migration is written
- **T008–T014**: seven US1 test tasks, all before implementation
- **T021–T024**: four US2 test tasks
- **T028–T032**: five US3 test tasks
- **T033 and T038**: backend and frontend contract types
- **T045–T047**: documentation sections
- **Phase 2 can run in parallel with US1** — they share no file

---

## Parallel Example: User Story 1

```bash
# All test tasks first; they are independent:
Task: "T008 Unit tests for ProductSearchTerms in backend/tests/MoizPos.UnitTests/Products/ProductSearchTermsTests.cs"
Task: "T009-T013 Integration tests in backend/tests/MoizPos.IntegrationTests/Products/ProductSearchTests.cs"
Task: "T014 POS one-letter handling in frontend/tests/features/pos/PosPage.test.tsx"

# Watch them fail, then T015 → T016 → T017 → T018 in order, then T019 and T020.
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 — Setup
2. Phase 3 — US1 (Phase 2 is not needed for it)
3. **Stop and validate**: quickstart V1–V7
4. Ship. "c type" and "oppo charger" now work on the Products, POS and Purchases screens — the
   change with the most effect on every sale.

### Incremental delivery

1. Setup → **US1** → V1–V7 → ship (MVP)
2. **US2** → V8–V11 → ship
3. Foundational → **US3** → V12–V15 → ship
4. Polish → V16–V17, performance, docs

Phase 2 alone is safe to deploy early: `is_local` defaults to Imported and nothing reads it until
US3 lands.

---

## Notes

- `[P]` = different files and no dependency on an incomplete task
- Verify every test fails before writing the code that greens it. **T021 and T022 are the
  exception** — they assert an API that already exists and are expected to pass; a failure there
  is a defect (T027)
- **Never concatenate fields before matching.** `CONCAT(name, brand)` lets a word match across the
  boundary between them (research R1). Normalise each field on its own
- **Never build SQL from typed text.** Words reach the repository already reduced to `[a-z0-9]`
  and are still bound as parameters
- **Do not loosen the one-second bound** in T041 to make it pass; the fallback is documented

---

## Implementation record

Completed 2026-09-17. Final counts: **627 backend** (250 unit + 369 integration + 8 architecture,
from a 550 baseline) and **297 frontend** (from 263). `tsc --noEmit` clean, `vite build` succeeds.
Quickstart V1–V17 executed against the live database; every check passed, and the test catalogue
was removed afterwards (4 products, 2 brands, 3 categories, 0 local brands — the starting state).

### Results worth recording

- **Search performance**: the new three-word test over 5,000 products took **391 ms** including
  its warm-up call, inside the one-second bound (SC-025). The stored-column fallback was not needed.
- **T027 and T021–T022**: the brand and category filters already worked on the server; all six
  filter tests passed on first run and **no backend change was required** for US2.
- **Phrase-matching risk**: the full suite ran green straight after the predicate change, before
  any screen work — no existing test relied on phrase matching, as the plan's risk table predicted.

### Deviations from the plan, and why

1. **The one-letter refusal is `SearchTooShortException`, not a FluentValidation rule.** Corrected
   in plan.md before tasks were written: `search` arrives on the query string, and FluentValidation
   here validates request bodies. A `DomainException` is already mapped to 400.
2. **A small shared helper `isTooShortSearch` in `frontend/src/lib/searchTerms.ts`**, unit-tested
   in `frontend/tests/lib/searchTerms.test.ts`. T020 asked for the check to be extracted; it lives
   in `lib/` beside `cart.ts` so it can mirror the server rule in one place.
3. **The Brands table gained a "Made" column** showing Local or Imported. T039 asked for a marker;
   a column reads more clearly than a badge beside the name, and matches the rest of the table.

### Existing tests changed, and why

- `TaxonomyPage.test.tsx › Brands page › creates a brand` asserted the exact payload
  `{ name, description }`. A brand save now also sends `isLocal` (FR-087a), so the expectation
  gained `isLocal: false`. The matching Categories test was left unchanged and still passes,
  confirming `isLocal` is sent for brands only.

### Found during the live run

The live database holds **two** products named "Charger 18W QC3.0" (ids 3 and 5 — the earlier
`dd` test entry and one added on 2026-09-11). Search correctly returned both; this is data, not a
query defect, and was left untouched.
