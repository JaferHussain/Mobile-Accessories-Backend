# Quickstart: Product Pictures

Prerequisites: backend and frontend running per repo root `CLAUDE.md` ("Commands"), signed in
as Admin for upload steps, Staff for role-check steps.

## 1. Upload a picture on a new product (US1 — FR-001, FR-002)

1. Products → New product → fill required fields → attach a JPEG under 2 MB → Save.
2. Confirm the product appears with its thumbnail, not a placeholder.
3. Re-open the product and upload a different image (FR-003).
4. Confirm the old image/thumbnail files are gone from `Storage:ProductImageRoot` on disk and
   the new one is shown.
5. Create a second product with no image → confirm it saves and shows the placeholder
   everywhere.

## 2. Grid/table toggle (US2 — FR-004..010)

1. Products screen → toggle to picture grid → confirm thumbnail, name, brand, category,
   price, stock per card.
2. Apply a brand filter, a category filter, and "local brands only" → toggle between grid and
   table → confirm identical result sets (FR-007).
3. Click a card → confirm full image + name/brand/category/model/barcode/price/stock; sign in
   as Staff and repeat — cost/profit fields must be absent (FR-010).
4. Reload the browser → confirm the grid/table choice persisted (FR-008); clear site data →
   confirm it defaults back to table.

## 3. POS result cards (US3 — FR-011..014)

1. POS screen → pick a sale type → type a search matching several products → confirm up to 8
   cards, each priced for that sale type, nothing added to the cart yet.
2. Narrow the search to exactly one match → confirm it is still a card, not auto-added
   (FR-012).
3. Click Add on a card → confirm it lands in the cart.
4. Scan/enter an exact barcode → confirm it adds directly with no cards shown (FR-013).
5. With items in the cart, switch sale type → confirm existing lines re-price (unchanged
   behavior, FR-014).

## 4. Backup coverage (FR-017)

1. Admin → Backups → trigger a backup.
2. Confirm the resulting archive/output includes files from `Storage:ProductImageRoot`, not
   only the database dump.

## 5. Performance spot-check (SC-004)

1. With a large product catalogue, open the picture grid and confirm network requests show
   only thumbnail-sized files for on-screen cards, with additional thumbnails requested as
   the list is scrolled (lazy-load), and the full image is requested only after opening a
   product's detail view (FR-015, FR-016).
