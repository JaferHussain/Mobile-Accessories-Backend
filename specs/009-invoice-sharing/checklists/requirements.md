# Specification Quality Checklist: Invoice and Receipt Sharing

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-23
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

### Validation pass 1 — issues found and corrected

1. **Implementation detail leaked into requirements.** An early draft named the share-link
   endpoint, the `wa.me` scheme and the React component by name in the functional requirements.
   Rewritten to describe the capability ("send to the customer's stored number") and the channel
   constraint moved to Assumptions, where naming it is context rather than instruction.
2. **"Who may share" was unstated.** The input asked the question without answering it. Rather
   than raise a clarification marker, a default was taken and recorded (Assumption 1): sharing is
   open to Staff because the salesman is who serves the customer; revoking is Admin because it is a
   containment action. Cheap to reverse if the owner disagrees.
3. **A success criterion was untestable.** "The customer receives a professional bill" was replaced
   by SC-004 and SC-005, which state what must be readable and what must never appear.

### Deliberate decisions

- **No [NEEDS CLARIFICATION] markers were raised.** The three candidates — who may share, link
  lifetime, and how printing is produced — all had defensible defaults already present in the
  built system or in the project's existing authority model. Each is recorded in Assumptions so it
  can be challenged rather than discovered later.
- **FR numbering continues from FR-105** (the customer sale-type feature), keeping one sequence
  across the project rather than restarting per feature.

### Carried into planning

- FR-113 and FR-114 are the two requirements most worth an architecture-level test rather than a
  screen test: they assert what must *never* appear on the shop's only unauthenticated surface, and
  a screen test would only prove that today's layout happens not to show it.
