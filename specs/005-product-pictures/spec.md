# Feature Specification: Product Pictures

**Feature Branch**: `005-product-pictures`
**Created**: 2026-09-22
**Status**: Draft
**Input**: Add pictures to products, shown as thumbnails in the Products grid view and as
picker cards in the POS search results; full image on the product detail view.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Owner adds a picture when creating a product (Priority: P1)

The owner (Admin) is entering a new product and wants to attach a photo so it can later be
told apart from visually similar accessories (cases, cables, chargers) without relying on
memorising SKUs.

**Why this priority**: Nothing else in this feature has a picture to show until one can be
attached to a product. This is the foundation every other story depends on.

**Independent Test**: Create a product with a picture attached; verify the product is saved,
a thumbnail exists alongside the full image, and both can be retrieved. Create a second
product with no picture; verify it saves successfully and is treated as having no picture
(not an error).

**Acceptance Scenarios**:

1. **Given** the New Product form, **When** the owner attaches a JPEG/PNG/WebP photo within
   the existing 2 MB limit and saves, **Then** the product is created, a thumbnail
   (~150×150) is generated automatically, and both the full image and thumbnail are
   retrievable for that product.
2. **Given** the New Product form, **When** the owner saves without attaching a picture,
   **Then** the product is created successfully and is shown with a placeholder image
   everywhere a picture would appear.
3. **Given** a product that already has a picture, **When** the owner uploads a new one,
   **Then** the new picture and its thumbnail replace the old ones and the old files are
   removed.

---

### User Story 2 - Owner or salesman browses products as pictures (Priority: P2)

Anyone viewing the Products screen wants to switch between the current table and a picture
grid, so browsing a shelf of similar-looking accessories is easier than reading a list of
near-identical names.

**Why this priority**: Depends on Story 1 (pictures must exist to show), but delivers value
on its own once pictures exist — independent of the POS change in Story 3.

**Independent Test**: With a mix of products (some with pictures, some without), switch the
Products screen to the picture grid and confirm thumbnails, placeholders, and all existing
filters/search behave identically to the table view. Reload the page and confirm the last
chosen view is remembered.

**Acceptance Scenarios**:

1. **Given** the Products screen, **When** the user toggles to the picture grid, **Then**
   each product is shown as a card with its thumbnail (or placeholder), name, brand,
   category, price, and stock.
2. **Given** the picture grid or the table, **When** the user searches or filters by brand,
   category, or "local brands only", **Then** both views show the identical filtered set —
   only the presentation differs.
3. **Given** the picture grid, **When** the user clicks a product card, **Then** a detail
   view opens showing the full image, name, brand, category, model, barcode, price, and
   stock; cost and profit fields are visible only to an Admin, exactly as they are today
   everywhere else in the product screens.
4. **Given** the user has previously chosen a view (grid or table), **When** they return to
   the Products screen later, **Then** it opens in the previously chosen view; the table is
   the default for a user who has never chosen.

---

### User Story 3 - Salesman confirms the right product at the counter (Priority: P3)

A salesman searching for a product at the counter (not scanning a barcode) wants to see the
matching product(s) with a picture before adding one to the cart, so a wrong item is not
added by mistake when several products have similar names.

**Why this priority**: Highest-value safety improvement but also the highest-risk change to
an existing, speed-critical workflow, so it lands last and can be evaluated once Stories 1–2
are in use.

**Independent Test**: Search for a product name that matches several products; confirm up to
8 result cards appear, each showing its picture, name, brand, category, the price for the
currently selected sale type, and stock, with nothing added to the cart until a card's Add
button is clicked. Search for a barcode with a scanner; confirm it still adds directly with
no card shown.

**Acceptance Scenarios**:

1. **Given** the POS screen with a sale type already chosen, **When** the salesman types a
   search that matches several products, **Then** up to 8 result cards are shown, each
   priced for the chosen sale type, and no item is added to the cart automatically.
2. **Given** a search that matches exactly one product, **When** the results are shown,
   **Then** that single result is still shown as a card requiring the Add button — it is
   never added automatically.
3. **Given** a barcode is scanned, **When** it matches a product, **Then** that product is
   added directly to the cart with no card shown, exactly as today.
4. **Given** items already in the cart, **When** the salesman changes the sale type at the
   top of the sale, **Then** all cart lines are re-priced for the new sale type, unchanged
   from current behaviour.

### Edge Cases

- A product's picture file is missing or fails to load: the placeholder is shown instead of
  a broken image, in both the grid and the POS cards.
- A search in the POS returns more than 8 matches: only the 8 best matches (by the existing
  search ordering) are shown; the salesman can narrow the search to see others.
- An uploaded image is rejected (wrong type or over the size limit): the existing upload
  error is shown and the product is otherwise saved/updated as if no image was attempted.
- A Staff user views a product's full detail: cost and profit fields are absent, matching
  current role rules; the picture and its other details are shown normally.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The New Product form MUST offer an optional picture field; a product MUST be
  saveable with or without one.
- **FR-002**: When a picture is saved, the system MUST generate a thumbnail of approximately
  150×150 pixels and retain the original full-size image; both MUST be retrievable
  independently.
- **FR-003**: Replacing a product's picture MUST remove the previously stored full image and
  thumbnail so they do not accumulate as orphaned files.
- **FR-004**: The Products screen MUST offer a toggle, next to "New product", between a
  picture grid view and the existing table view.
- **FR-005**: The picture grid MUST show, per product: thumbnail (or a placeholder when
  absent), name, brand, category, price, and stock.
- **FR-006**: The table view MUST remain unchanged in content and MUST NOT show pictures.
- **FR-007**: Search, brand filter, category filter, and "local brands only" filter MUST
  produce the same result set in both views.
- **FR-008**: The user's last-chosen view (grid or table) MUST be remembered for their next
  visit to the Products screen; the table MUST be the default when no choice has been made
  yet.
- **FR-009**: Clicking a product in the grid MUST open a detail view showing the full image,
  name, brand, category, model, barcode, price, and stock.
- **FR-010**: The product detail view MUST show cost and profit fields only to an Admin,
  consistent with every other product-data surface in the system.
- **FR-011**: On the POS screen, typing a search MUST show up to 8 result cards, each with
  thumbnail (or placeholder), name, brand, category, price for the currently selected sale
  type, stock, and an explicit control to add it to the cart.
- **FR-012**: A search that matches exactly one product MUST still present it as a card
  requiring an explicit add action; the system MUST NOT add it to the cart automatically.
- **FR-013**: Scanning a barcode on the POS screen MUST continue to add the matched product
  directly to the cart, with no result cards shown, unchanged from current behaviour.
- **FR-014**: The sale type (Retail/Wholesale) MUST remain a single choice made once at the
  top of the sale; it MUST NOT be selectable per product, and changing it MUST re-price
  items already in the cart, unchanged from current behaviour.
- **FR-015**: List and grid views MUST load only thumbnails, never the full image; the full
  image MUST be loaded only when a product's detail view is opened.
- **FR-016**: Thumbnails MUST be lazy-loaded as they scroll into view rather than all at
  once.
- **FR-017**: The system's data backup MUST include the stored product images and
  thumbnails, not the database alone, so a restore does not leave every product without its
  picture.

### Key Entities

- **Product Image**: The full-size photo attached to a product; optional; one per product;
  replaced (not accumulated) on re-upload.
- **Product Thumbnail**: A small, automatically generated rendition of the Product Image
  (~150×150), used everywhere a picture is shown in a list or search result; regenerated
  whenever the Product Image is replaced.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can tell two visually similar products apart using their pictures in
  the Products grid, without reading the SKU, in under 5 seconds.
- **SC-002**: Switching the Products screen between grid and table produces the same set of
  products (given the same search/filters) 100% of the time.
- **SC-003**: A salesman completing a non-barcode sale at the counter can pick the correct
  product from the POS result cards without adding a wrong item, even when several products
  share a similar name.
- **SC-004**: Browsing the Products picture grid for a catalogue of at least 2,000 products
  feels no slower to the user than the existing table view, because only thumbnails are
  fetched for items on screen.
- **SC-005**: After a full data restore, every product that had a picture before the restore
  has that same picture afterward.

## Assumptions

- "Remembers last choice" (grid vs. table) is a per-browser preference, not a value synced
  across the owner's and salesman's different devices.
- The "up to 8" result cards on the POS screen use the existing product search ordering
  already shared with the Products and Purchases screens; no new ranking is introduced.
- Thumbnail generation happens once, at upload time, not on every read — later reads serve
  the already-generated thumbnail.
- A picture is a single photo per product; multiple photos per product are out of scope for
  this feature.
