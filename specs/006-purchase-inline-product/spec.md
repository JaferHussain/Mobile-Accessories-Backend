# Feature 006: Create a product while recording a purchase

**Created**: 2026-09-22 | **Status**: Draft | **Type**: Bug fix

## The problem

Buying stock the shop has never sold before is impossible. The Purchases screen searches the
existing catalogue; when nothing matches it says *"No products match that search."* and stops.
The owner must leave, go to Products, create the item, come back, and search again — or, more
likely, not record the purchase at all.

## Goal

From that same empty result, create the product and continue straight into the purchase.

## Requirements

- **FR-001**: When a purchase product search returns no match, the screen MUST offer to create a
  new product without leaving the screen.
- **FR-002**: The creation form MUST be the same one the Products screen uses — same fields, same
  validation, same optional picture. A second, drifting product form is the failure to avoid.
- **FR-003**: On save, the newly created product MUST be selected for the purchase automatically,
  landing the user on the purchase form with no further searching.
- **FR-004**: Cancelling MUST return to the search, leaving no product created.
- **FR-005**: Creating a product here is an Admin action, exactly as it is on the Products screen;
  the Purchases screen is already Admin-only, so no new authority is introduced.

## Out of scope

Editing an existing product from this screen. The purchase itself already updates cost and can
update the sale price — that is the existing, tested path.

## Success criteria

- **SC-001**: An owner can record a purchase of a product that did not exist beforehand, without
  navigating away from the Purchases screen.
- **SC-002**: The product created this way is indistinguishable from one created on the Products
  screen.

## Notes for planning

No backend change: `POST /api/products` and `POST /api/products/{id}/image` already exist and are
already Admin-only. `ProductForm` is already a reusable, tested component. This is one new UI
branch wired to code that exists.
