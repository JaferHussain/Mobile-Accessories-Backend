# Tasks: Product Pictures

**Input**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/api.md`
**Tests**: TDD is a project non-negotiable (constitution) — every task below that changes
behavior is preceded by its failing test.

## Phase 1: Setup

- [X] T001 Add `SixLabors.ImageSharp` package reference in `backend/src/MoizPos/MoizPos.csproj`

## Phase 2: Foundational (blocks all user stories)

- [X] T002 [P] Failing test: `ImageStorageService` generates a ~150×150 thumbnail alongside
      the full image on save, at the `_thumb` convention path — in
      `backend/tests/MoizPos.UnitTests/Infrastructure/ImageStorageServiceTests.cs` (new)
- [X] T003 [P] Failing test: `ImageStorageService.DeleteProductImage` removes both the full
      image and its thumbnail (same file) — extend
      `backend/tests/MoizPos.UnitTests/Infrastructure/ImageStorageServiceTests.cs`
- [X] T004 Implement thumbnail generation + dual-file delete in
      `backend/src/MoizPos/Infrastructure/Storage/ImageStorageService.cs` to pass T002–T003
      (FR-002, FR-003)
- [X] T005 [P] Failing integration test: re-uploading a product image via
      `POST /api/products/{id}/image` removes the previous full image and thumbnail files —
      in `backend/tests/MoizPos.IntegrationTests/Products/ProductImageTests.cs` (new)
- [X] T006 Wire the extended `ImageStorageService` into the existing upload endpoint in
      `backend/src/MoizPos/Api/Controllers/ProductsController.cs` to pass T005 (no route/DTO
      change — confirms existing endpoint already calls the now-extended service correctly)

**Checkpoint**: pictures can be uploaded, thumbnailed, and cleanly replaced. Nothing user-facing
yet — both user-facing stories below build on this.

## Phase 3: User Story 1 — Owner adds a picture when creating a product (P1)

**Goal**: New Product form can attach a picture; product saves fine without one.
**Independent Test**: create a product with and without a picture per spec US1.

- [X] T007 [P] [US1] Failing test: `ProductForm` renders a picture field, submits it on save,
      and saves successfully with no picture attached — in
      `frontend/src/features/products/ProductForm.test.tsx` (new or extended)
- [X] T008 [US1] Add the picture upload control to
      `frontend/src/features/products/ProductForm.tsx`, calling the existing
      `POST /api/products/{id}/image` endpoint after create/update, to pass T007 (FR-001)
- [X] T009 [P] [US1] Failing test: a product with no `imagePath` renders the placeholder
      image wherever a picture is shown — in a shared image-display component test (new,
      e.g. `frontend/src/features/products/ProductThumbnail.test.tsx`)
- [X] T010 [US1] Implement a shared `ProductThumbnail`/`ProductImage` component (derives the
      thumbnail path from `imagePath` by the `_thumb` convention, falls back to a placeholder)
      in `frontend/src/features/products/` to pass T009 — this component is reused by US2 and
      US3, so it is built here rather than duplicated later

**Checkpoint**: US1 independently testable and complete.

## Phase 4: User Story 2 — Owner or salesman browses products as pictures (P2)

**Goal**: Products screen toggles between table and picture grid; both filtered identically;
clicking a card opens full detail with role-correct fields.
**Independent Test**: per spec US2 — toggle, filter parity, detail view, persistence.

- [X] T011 [P] [US2] Failing test: toggling the Products view calls the same filtered query
      and renders the identical result set in both grid and table — in
      `frontend/src/features/products/ProductsPage.test.tsx` (extended)
- [X] T012 [P] [US2] Failing test: the chosen view (grid/table) persists across a reload via
      `localStorage`, defaulting to table when unset — same test file as T011
- [X] T013 [US2] Add the grid/table toggle and `localStorage` persistence to
      `frontend/src/features/products/ProductsPage.tsx`, reusing the existing search/filter
      state for both views, to pass T011–T012 (FR-004, FR-006, FR-007, FR-008)
- [X] T014 [P] [US2] Failing test: the picture grid card shows thumbnail (via the T010
      component), name, brand, category, price, stock — in
      `frontend/src/features/products/ProductGrid.test.tsx` (new)
- [X] T015 [US2] Implement `ProductGrid`/`ProductCard` in
      `frontend/src/features/products/ProductGrid.tsx` (new) to pass T014 (FR-005)
- [X] T016 [P] [US2] Failing test: clicking a card opens a detail view with full image, name,
      brand, category, model, barcode, price, stock; cost/profit fields present only for the
      Admin-shaped DTO — in `frontend/src/features/products/ProductDetail.test.tsx` (new)
- [X] T017 [US2] Implement `ProductDetail` in
      `frontend/src/features/products/ProductDetail.tsx` (new), sourcing role-scoped fields
      from the existing `ProductStaffDto`/`ProductAdminDto` shape already returned by the API,
      to pass T016 (FR-009, FR-010)

**Checkpoint**: US2 independently testable and complete; does not depend on US3.

## Phase 5: User Story 3 — Salesman confirms the right product at the counter (P3)

**Goal**: POS search shows up to 8 result cards requiring explicit Add; barcode scan
unchanged; sale type stays a single top-of-sale choice.
**Independent Test**: per spec US3 — multi-result cards, single-result still a card, barcode
bypass, re-price on sale-type switch.

- [X] T018 [P] [US3] Failing test: `PosPage`'s product lookup requests `pageSize=8` (not 1)
      and returns an array, not a single product — in
      `frontend/src/features/pos/PosPage.test.tsx` (extended)
- [X] T019 [US3] Change `PosPage.tsx`'s `findProduct` to call search with `pageSize: 8` and
      return the result array; keep the separate barcode call path untouched, to pass T018
      (FR-011, FR-013)
- [X] T020 [P] [US3] Failing test: `PosScreen` renders up to 8 result cards (thumbnail via
      T010, name, brand, category, sale-type price, stock, Add control) and adds nothing to
      the cart until Add is clicked, including when exactly one result is returned — in
      `frontend/src/features/pos/PosScreen.test.tsx` (extended)
- [X] T021 [US3] Change `PosScreen.tsx`'s `onFindProduct` prop contract to `Product[]`, render
      result cards instead of auto-adding, wire each card's Add button to the existing
      `addProduct` logic, to pass T020 (FR-011, FR-012)
- [X] T022 [P] [US3] Failing test: barcode entry still adds directly to the cart with no
      cards shown — same test file as T020, confirms the untouched path
- [X] T023 [US3] Confirm/adjust the barcode call site in `PosPage.tsx`/`PosScreen.tsx` remains
      a direct single-add, unaffected by T019/T021, to pass T022 (FR-013)
- [X] T024 [P] [US3] Failing test: changing sale type with items already in the cart re-prices
      every existing line (regression guard on already-correct behavior) — in
      `frontend/src/features/pos/PosScreen.test.tsx` (extended, if not already covered)
- [X] T025 [US3] Confirm existing `onRepriceProduct` wiring in `PosScreen.tsx` still passes
      T024 unchanged (FR-014) — expected to require no code change, only confirms no
      regression from T021's refactor

**Checkpoint**: US3 independently testable and complete.

## Phase 6: Polish & Cross-Cutting

- [X] T026 [P] Failing test: thumbnails use `loading="lazy"` and only thumbnail URLs (never
      full-image URLs) are requested by the grid/POS cards; the full image is requested only
      from `ProductDetail` — in the T010/T015/T017 component tests, extended
- [X] T027 Add `loading="lazy"` and verify URL selection in `ProductThumbnail`/`ProductGrid`/
      `ProductDetail` to pass T026 (FR-015, FR-016)
- [X] T028 [P] Failing test: the backup routine's output includes files from
      `Storage:ProductImageRoot`, not the database dump alone — in
      `backend/tests/MoizPos.IntegrationTests/Backup/BackupServiceTests.cs` (extended)
- [X] T029 Extend the backup implementation to archive `Storage:ProductImageRoot` to pass
      T028 (FR-017)
- [X] T030 Run full suite (`dotnet test`, `npm run test`, `npx tsc --noEmit`) and confirm
      nothing previously green broke, per constitution Definition of Done

## Dependencies

- Phase 1 → Phase 2 → {Phase 3, Phase 4, Phase 5} → Phase 6
- Phase 4 (US2) depends on T010 (the shared thumbnail component from US1) — build US1 first
- Phase 5 (US3) also depends on T010 — same reason
- US2 and US3 do not depend on each other and may proceed in parallel once US1's T010 lands

## Parallel Example

Once Phase 2 and T010 (US1) are done, these can run together:

```
T011, T012, T014, T016   (US2 test-writing)
T018, T020, T022, T024   (US3 test-writing)
```

## MVP Scope

Phase 1 + 2 + 3 (US1) alone delivers a working, independently valuable increment: products
can carry pictures. US2 (browsing) and US3 (POS safety) are additive on top.
