# Specification Quality Checklist: POS, Inventory & Customer Ledger System

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-09
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- The source brief carried an explicit technology stack (React, ASP.NET Core 8, MySQL, Dapper, xUnit/Vitest) and a phased TDD build order. Those are deliberately **excluded** from this spec and belong in `/speckit-plan` and the project constitution. Nothing was lost — the phased order maps to the priority ordering of the user stories below.
- Iteration 1 correction applied: FR-013 and FR-040 originally used the terms "server-side" and "at the point where data is served"; both were reworded to state the business rule (totals computed from recorded lines; access refused wherever the data is requested) without prescribing an architecture.
- Assumptions made in place of clarification markers, all recorded in the spec's Assumptions section: single location, PKR and Pakistan local time, desktop/tablet only, two fixed roles, keyboard-wedge barcode scanning, operator-confirmed WhatsApp sends, indefinite record retention, no tax/GST in v1, manual opening-balance entry at go-live.
- **RESOLVED 2026-09-09** — cost attribution when the same product is bought repeatedly at different costs. The owner selected **latest purchase cost**, applied uniformly to every product (FR-011a/b/c/d, FR-031): the newest purchase cost replaces the cost of all stock on hand, and profit on every subsequent sale is measured against it. Worked example confirmed by the owner: 10 units bought at 800, 5 sold, then 10 bought at 850 — all 15 remaining units carry a cost of 850. Each sale line stores the cost in force at the time of sale, so historical profit is never rewritten. Old stock is likewise sold at the **current** sale price, not the price in force when it was bought. Weighted average and FIFO/batch costing are both out of scope. The owner has accepted that this understates realised margin during price rises (see Assumptions).

## Phase-to-Story Map (from the source brief)

| Brief phase | Covered by |
|-------------|------------|
| 1 Foundation & Auth | US6, FR-038/039 |
| 2 Product/Variant Catalog | US3, FR-001..006 |
| 3 Purchases & Suppliers | US3, FR-007..011 |
| 4 Sales / POS / Invoicing | US1, FR-012..018 |
| 5 Customer Ledger (Udhaar) | US2, FR-019..023 |
| 6 Returns | US5, FR-024..028 |
| 7 Expenses | US4, FR-029/030 |
| 8 Profit, Dashboard, Reports | US4, FR-031..037 |
| 9 PDF & WhatsApp | US7, FR-042..044 |
| 10 Roles, Backup, Audit | US6, US8, FR-040/041, FR-045..047 |

- Items marked incomplete require spec updates before `/speckit-clarify` or `/speckit-plan`. All items currently pass.
