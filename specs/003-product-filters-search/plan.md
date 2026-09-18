# Implementation Plan: Product Filters & Forgiving Search

**Branch**: `003-product-filters-search` | **Date**: 2026-09-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-product-filters-search/spec.md`

## Summary

Three changes to finding products:

1. **Forgiving search (US1).** The typed text is split into words; a product matches when every
   word appears in its name, model, brand or category, with case, spaces, hyphens and punctuation
   ignored and a trailing plural "s" tolerated. `c type` finds "Type-C". Implemented as a pure
   `ProductSearchTerms` value (unit-tested) feeding a rewritten SQL predicate. **No schema change.**
2. **Brand and category filters (US2).** The API already supports both; this is screen work —
   two selects and a Clear button on the Products screen.
3. **Local brands (US3).** `brands.is_local` (migration `0016`), a tick on the Brands screen, and a
   **Local brands only** filter on Products.

Product search is shared by the Products, POS and Purchases screens, so the improved search
reaches all three; the plan verifies each rather than assuming it.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (backend); TypeScript 5 / React 18 (frontend)

**Primary Dependencies**: ASP.NET Core 8, Dapper, FluentValidation, DbUp, xUnit + Moq, Vitest +
React Testing Library, TanStack Query

**Storage**: MySQL 8.0.40 (verified — supports `REGEXP_REPLACE`, required by research R1). One new
migration, `0016_brand_is_local.sql`.

**Testing**: `dotnet test` (unit, integration, architecture); `npm run test` + `tsc --noEmit`

**Target Platform**: Windows desktop/tablet at the shop counter; API and MySQL on the same machine

**Project Type**: Web application — existing `backend/` + `frontend/`

**Performance Goals**: SC-025 — search and filter results within one second across a 5,000-product
catalogue. Normalising at query time is a scan of ≤ 5,000 rows; guarded by a new multi-word
performance test alongside the existing single-word one.

**Constraints**: Search words reduced to `[a-z0-9]` before binding, and always passed as
parameters. At most 8 words per search. Filters never alter price (sale type decides it) or
field visibility (role decides it). Cost fields remain unreachable by Staff.

**Scale/Scope**: One shop, two users, up to 5,000 products, tens of brands and categories.
Roughly 6 backend files changed and 2 added; 5 frontend files changed.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 — see below.*

| Principle | How this feature complies |
|---|---|
| **I. TDD** | Every implementation task is preceded by a failing test. Search rules live in a pure class specifically so each SC-022 spelling is a unit test before any SQL exists. |
| **II. Layered backend** | `ProductSearchTerms` (Application, pure) decides the words; `ProductRepository` (Infrastructure) only builds SQL from them; the controller passes the query through. Dapper only. The one-letter refusal is enforced on the server, not just as a screen hint — as a `SearchTooShortException` (a `DomainException`, mapped to 400 `VALIDATION_FAILED`), because FluentValidation here validates request bodies and `search` arrives on the query string. |
| **III. Frontend logic tested** | The Products screen's filter state (combination, Clear leaving the search in place, the empty-result message, the one-letter hint) gets component tests. |
| **IV. Transactional & server-authoritative** | Search and filters are read-only. Marking a brand local is a single-row update through the existing brand service. Filtering is decided by the server; the screen only sends choices. |
| **V. Complete vertical slices** | Migration → repository → service → controller (integration tests) → screens (component tests), for both the search change and the local flag. |
| **VI. Definition of done** | Baseline **550 backend / 263 frontend**. Done only when both suites are green with nothing previously passing reddened. |

**Cost confidentiality**: `brandIsLocal` is added to `ProductStaffDto`. It is not cost, margin or
profit and contains none of the architecture test's forbidden fragments; the architecture tests
run unchanged and must stay green.

**Auditability**: marking a brand local changes a label, not stock or a ledger, so the
constitution's audit rule for stock and ledger mutations does not apply. The existing brand update
path is reused unchanged in that respect.

### Post-design re-check

Re-evaluated after Phase 1. No violations. No new project, data-access mechanism, or
authorization concept.

## Project Structure

### Documentation (this feature)

```text
specs/003-product-filters-search/
├── plan.md              # This file
├── spec.md              # /speckit-specify (1 clarification resolved: option A)
├── research.md          # Phase 0 — 8 decisions, no open questions
├── data-model.md        # Phase 1 — migration 0016, search terms, invariants
├── quickstart.md        # Phase 1 — V1..V17
├── contracts/
│   └── openapi.yaml     # Phase 1 — delta on features 001 and 002
├── checklists/
│   └── requirements.md  # all 16 passing
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

Real paths. **Changed** unless marked new.

```text
backend/
├── src/
│   ├── MoizPos.Migrator/Scripts/
│   │   └── 0016_brand_is_local.sql                       # NEW
│   ├── MoizPos.Domain/Entities/Entities.cs               # Brand.IsLocal
│   ├── MoizPos.Application/
│   │   ├── Calculations/ProductSearchTerms.cs            # NEW — the word rules (R1–R3, R5)
│   │   ├── Abstractions/IProductAbstractions.cs          # ProductQuery.LocalOnly, ProductRow.BrandIsLocal
│   │   ├── Contracts/Products/ProductContracts.cs        # ProductStaffDto.BrandIsLocal
│   │   ├── Contracts/Taxonomy/TaxonomyContracts.cs       # BrandDto / BrandUpsertRequest.IsLocal
│   │   └── Services/{ProductService,BrandService}.cs
│   ├── MoizPos.Infrastructure/Repositories/
│   │   ├── ProductRepository.cs                          # word-by-word predicate, local filter
│   │   └── BrandRepository.cs                            # read/write is_local
│   └── MoizPos.Api/Controllers/
│       ├── ProductsController.cs                         # localOnly; one-letter refusal
│       └── BrandsController.cs
└── tests/
    ├── MoizPos.UnitTests/Products/ProductSearchTermsTests.cs          # NEW
    ├── MoizPos.IntegrationTests/Products/ProductSearchTests.cs        # NEW
    ├── MoizPos.IntegrationTests/Products/ProductFilterTests.cs        # NEW
    └── MoizPos.IntegrationTests/Performance/PerformanceTests.cs       # + multi-word test

frontend/
├── src/features/
│   ├── products/ProductsPage.tsx          # brand/category selects, local tick, Clear, messages
│   ├── products/productApi.ts             # localOnly, brandIsLocal
│   ├── taxonomy/TaxonomyPage.tsx          # "Local brand" tick, brands only
│   ├── taxonomy/taxonomyApi.ts            # isLocal
│   └── pos/PosPage.tsx                    # one-letter search → "No product found", not an error
└── tests/features/
    ├── products/ProductFilters.test.tsx   # NEW
    └── taxonomy/TaxonomyPage.test.tsx     # + local tick for brands, absent for categories
```

**Structure Decision**: The existing two-project layout is kept. Every path is an existing file or
a new file in an existing folder; no project or layer is added.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| The shared `TaxonomyPage` component gains a field shown **only for brands** | Categories and Brands share one component because they were identical; "Local" applies to brands alone | Splitting into two near-identical components would duplicate ~250 lines to add one tick box. A single `resource === 'brands'` condition is smaller and keeps both screens in step. |
| Search normalises at **query time** rather than from a stored column | Brand and category names live in other tables and can be renamed; a stored copy on each product would go stale until re-indexed | A stored `search_text` column is faster in principle, but at ≤ 5,000 rows the scan is within SC-025, and staleness would silently break search after a rename — the exact thing the Categories/Brands modules exist to make safe. Revisit only if the performance test fails. |

## Phase Sequence

1. **Foundation** — migration `0016`, `Brand.IsLocal`, the DTO fields. Blocks US3 only; US1 and US2
   need no schema.
2. **US1 — Forgiving search (P1)**. `ProductSearchTerms` unit tests → class → repository predicate →
   integration tests → POS and Purchases verified. The MVP.
3. **US2 — Brand and category filters (P2)**. Mostly screen work on an API that already exists.
4. **US3 — Local brands (P3)**. Brands screen tick, `localOnly` filter, screen tick.
5. **Polish** — performance test, quickstart V1–V17 live, `CLAUDE.md`, both suites green.

## Risks

| Risk | Mitigation |
|---|---|
| **The POS adds the wrong product** now that more spellings match (it takes the first result) | Results stay ordered by name, which is deterministic; barcode lookup still runs first and is exact. Quickstart V7 checks it at the counter. Showing a pick-list at the POS is out of scope and would be its own feature. |
| **A one-letter scan at the POS surfaces as an error** once the server refuses one-letter searches | `PosPage` treats that refusal as "no product found", and a test asserts it. |
| **Search becomes slow** with several words × four normalised fields | Word cap of 8; multi-word performance test at 5,000 products must stay under one second. If it fails, the rejected stored-column alternative is the documented fallback. |
| **An existing test depends on phrase matching** | **Checked every search term in the suite.** `cable`, `Baseus`, `Cables`, `"Braided "` / `"Retired "` (first 8 chars of a generated name), full generated names in `SaleTypeTests` (`"ST " + hex`), and a 12-char barcode. Each still matches under word matching: every word is ≥ 2 chars and appears in the product, and the barcode path is unchanged. `"Cables"` now also matches through the plural rule. No existing test needs changing; the full suite still runs straight after the predicate change, before any screen work. |
| **Marking a brand local is mistaken for cost data** by the architecture test | `brandIsLocal` contains no forbidden fragment; the architecture suite runs in the regression gate. |
