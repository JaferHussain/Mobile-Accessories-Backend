# Feature 007: Fuller supplier records

**Created**: 2026-09-22 | **Status**: Draft

## The problem

A supplier record holds only a name, a phone number and an address. The owner deals with these
people in person and by bank transfer, and keeps the rest — their CNIC, which bank account to pay
into — on paper or in his head.

## Goal

Hold the details the shop actually needs to identify a supplier and pay them.

## Requirements

- **FR-001**: A supplier MUST be able to carry, all optional: **CNIC**, **email**, **bank name**,
  **account title**, **account number / IBAN**, and free-text **notes**.
- **FR-002**: Every new field is optional. Existing suppliers MUST remain valid and editable
  without being forced to fill anything in.
- **FR-003**: The supplier form MUST group payment details (bank name, account title, account
  number) together, so the person paying an invoice reads them in one place.
- **FR-004**: Supplier access is **unchanged**: the whole module is already Admin-only, reading
  included. The new fields inherit that and nothing is widened. Suppliers, their balances and now
  their bank details are the owner's business, and opening them to Staff would be an authority
  change, not a field addition — if the owner wants that, it is its own decision to make
  deliberately.
- **FR-005**: Creating and editing a supplier remains Admin-only, unchanged.
- **FR-006**: CNIC MUST be stored as entered. The system MUST NOT reformat or validate it into a
  fixed pattern — a supplier's paperwork is the authority, not this software.

## Explicitly NOT in this feature: supplier opening balance

You listed it, and it is deliberately left out — it is not a field, it is a ledger change.

`suppliers.payable_balance` is governed by an invariant the integration suite asserts:

```
payable_balance = SUM(purchases.total) − SUM(supplier_payments.amount) − SUM(purchase_returns.total)
```

An opening balance is a figure that belongs to none of those three terms, so adding one either
breaks that test or silently breaks the invariant it protects. Doing it properly means a ledger
entry, a correction-by-difference rule (recording it twice must never double the debt) and an
amended invariant — the same shape as the customer opening balance in feature 002, and the same
size. It deserves its own feature, not a column bolted onto this one.

## Success criteria

- **SC-001**: The owner can record a supplier's bank details and read them back when paying.
- **SC-002**: Every supplier that existed before this feature still opens, edits and saves.

## Notes for planning

One migration adding six nullable columns to `suppliers`; extend the entity, DTO, repository
read/write and the supplier form. No new endpoint, no behaviour change to purchases or payments.
