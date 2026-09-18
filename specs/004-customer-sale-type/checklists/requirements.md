# Specification Quality Checklist: Customer Sale-Type Filtering

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-18
**Feature**: [spec.md](../spec.md)

## Content Quality

- [X] No implementation details (languages, frameworks, APIs)
- [X] Focused on user value and business needs
- [X] Written for non-technical stakeholders
- [X] All mandatory sections completed

## Requirement Completeness

- [ ] No [NEEDS CLARIFICATION] markers remain
- [X] Requirements are testable and unambiguous
- [X] Success criteria are measurable
- [X] Success criteria are technology-agnostic (no implementation details)
- [X] All acceptance scenarios are defined
- [X] Edge cases are identified
- [X] Scope is clearly bounded
- [X] Dependencies and assumptions identified

## Feature Readiness

- [X] All functional requirements have clear acceptance criteria
- [X] User scenarios cover primary flows
- [X] Feature meets measurable outcomes defined in Success Criteria
- [X] No implementation details leak into specification

## Notes

### Open: two clarifications, both about scope

**FR-101 — what a customer's sale type means.** Sale type is recorded per invoice, so a customer
has no type of their own today, and nothing stops one customer having both kinds of sale. Marking
the customer, deriving it from their sales, or doing both lead to materially different work and
different behaviour when a customer's history is mixed. Nothing here can be defaulted safely: the
owner's phrase "find the customer sale type" fits all three readings.

**FR-107 — where the day-end split lives.** The owner said "move retail vs wholesale from Reports".
The Reports entry answers a different question from the one this feature adds — money taken today,
split by type, with the day's sales behind it — and he asked for it two features ago. Removing it
is a real loss, so it is put to him rather than assumed either way.

### Resolved as documented assumptions rather than questions

1. **"A dropdown plus two buttons" is one control with three states.** Two controls for the same
   choice could disagree with each other on screen; All / Retail / Wholesale covers everything
   described.
2. **Retail is the default** where one is needed — counter trade is the shop's ordinary business.
3. **Filters survive opening a customer and coming back, but not a new visit to the screen.** A
   filter still applied tomorrow would hide customers from whoever opens the screen next.

### Existing behaviour confirmed while specifying

- `customers` has no sale-type column; the only place sale type exists is `invoices.sale_type`.
- The live data currently has two customers, each with retail sales only — so no customer yet
  demonstrates the mixed case, but nothing prevents it.

### Numbering

Continues from feature 003: FR-091 … FR-107, SC-028 … SC-034.
