# Implementation Plan: Product Pictures

**Branch**: `005-product-pictures` | **Date**: 2026-09-22 | **Spec**: [spec.md](./spec.md)

**Input**: `specs/005-product-pictures/spec.md`

## Summary

Add an optional picture to each product, auto-generate a ~150×150 thumbnail on upload, show
thumbnails in a new Products grid view (toggled against the existing table) and in up to 8
POS search-result cards that now require an explicit Add click (barcode scan unaffected).
Builds entirely on the existing `image_path` column and `ImageStorageService`; no new table,
one new backend dependency (thumbnail generation), one behavior change to POS lookup, one
addition to the backup job's scope.

## Technical Context

**Language/Version**: C# / .NET 8 (backend), TypeScript / React 18 via Vite (frontend) —
unchanged, matches existing stack.

**Primary Dependencies**: `SixLabors.ImageSharp` (new, backend-only, Infrastructure layer) for
thumbnail resize. No new frontend dependency — `<img loading="lazy">` covers lazy-loading
natively.

**Storage**: MySQL 8 via Dapper (existing `products.image_path` column, no schema change);
images on disk under `Storage:ProductImageRoot` (existing).

**Testing**: xUnit + FluentAssertions (backend unit/integration, existing projects), Vitest +
React Testing Library (frontend, existing).

**Target Platform**: Existing — ASP.NET Core 8 on IIS/Kestrel, React SPA.

**Project Type**: Web application (existing single backend project + SPA frontend).

**Performance Goals**: Grid/POS card lists fetch thumbnails only (never full images); SC-004
— browsing a 2,000+ product grid feels no slower than the existing table.

**Constraints**: Existing 2 MB upload cap unchanged. Thumbnail generation runs once per
upload (not per read) so it never sits on a hot read path.

**Scale/Scope**: 3 user stories, 1 new library dependency, 0 schema changes, 1 existing
backend method extended (`ImageStorageService`), 1 existing endpoint's caller behavior
extended (POS lookup page size 1 → 8), 2 frontend screens changed (Products, POS), 1
operational change (backup scope).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — still passes.*

| Gate | Status | Notes |
|---|---|---|
| TDD (failing test first) | PASS | Each task in `/speckit-tasks` will add/extend a test before the implementation it covers — thumbnail generation, `ImageStorageService` deletion-of-both-files, POS multi-result prop shape, grid/table filter parity. |
| Layering (`Domain`→nothing, `Application`→`Domain`, `Infrastructure`→`Application`, `Api`→both) | PASS | Thumbnail generation lives in `Infrastructure/Storage/ImageStorageService.cs`, already the correct layer; no new SQL, no new Application-layer database access. |
| Money as `decimal` | PASS | Feature touches no money fields. |
| Server-recomputed totals | PASS | Feature touches no pricing/total calculation; POS re-pricing on sale-type switch is existing, unchanged behavior (FR-014). |
| Cost/profit never reach Staff | PASS | No DTO change; `ImagePath` already lives on the Staff-visible base DTO, which is correct since a picture isn't cost data (FR-010 explicitly re-affirms this). |
| Schema changes via DbUp | PASS (N/A) | No schema change — thumbnail path is derived by convention, not stored (see `research.md`). |
| Product search shared across Products/POS/Purchases | PASS | POS reuses the existing search method unchanged, only requesting more results (`pageSize=8`); no new search logic. |
| One unauthenticated endpoint only | PASS | No new endpoint added; thumbnail is served by the existing static-file path already used for full images, under the same authorization posture. |

No violations, no complexity-tracking entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/005-product-pictures/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/api.md
└── tasks.md          # produced by /speckit-tasks, not this command
```

### Source Code (existing structure, files touched by this feature)

```text
backend/src/MoizPos/
├── Infrastructure/Storage/ImageStorageService.cs   # + thumbnail generation, dual-file delete
├── MoizPos.csproj                                  # + SixLabors.ImageSharp package reference
├── Application/Services/BackupService.cs (or equivalent)  # + include Storage:ProductImageRoot
└── (no new controller routes; no schema/migration change)

frontend/src/features/products/
├── ProductForm.tsx        # + picture field (upload control)
├── ProductsPage.tsx       # + grid/table toggle, localStorage-persisted
├── productApi.ts          # (no shape change — imagePath already present)
└── (new) ProductGrid / ProductCard / ProductDetail components

frontend/src/features/pos/
├── PosScreen.tsx    # onFindProduct prop becomes multi-result; renders up to 8 cards
├── PosPage.tsx      # search call raises pageSize 1 → 8; barcode path untouched
└── posApi.ts        # (no shape change)
```

No new top-level folders — everything fits the five existing layers, so `LayeringTests`
requires no changes.

## Next Step

`/speckit-tasks` to break this into the phased, testable task list (Setup → Foundational →
US1 → US2 → US3 → Polish).
