# Specification Quality Checklist: Credit Controls & Customer Opening Balances

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-10
**Feature**: [spec.md](../spec.md)

## Content Quality

- [X] No implementation details (languages, frameworks, APIs)
- [X] Focused on user value and business needs
- [X] Written for non-technical stakeholders
- [X] All mandatory sections completed

## Requirement Completeness

- [X] No [NEEDS CLARIFICATION] markers remain
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

### Validation record

Three points were resolved as documented assumptions rather than raised as clarifications,
because each had a defensible default that the feature description itself points to:

1. **Blocked vs. queued for approval.** The description says a salesman "must not be able to"
   make a credit sale. Refusing outright is the direct reading; an approval workflow would add
   scope the description does not ask for. Recorded under Assumptions and reflected in FR-055.

2. **Whether a part-paid sale counts as credit.** The description explicitly says "neither a
   fully-credit sale nor a part-paid one", so this needed no guess — it became FR-052.

3. **Correcting a mistyped carried-forward amount.** The existing udhaar register is append-only,
   so a correction is an entry rather than an edit. This follows the established behaviour of the
   system rather than inventing a rule, and became FR-070 and FR-071.

### Scope note carried into planning

User Story 2 (part payment during recovery) describes behaviour the shop already has. It is
specified here so the rule is stated and provably holds — planning should expect verification
work for that story rather than new capability, and should say so rather than rebuilding it.

### Numbering

Functional requirements continue from feature 001, which ended at FR-050; success criteria
continue from SC-014. This keeps a single requirement namespace across the shop's system so a
reference like "FR-051" is unambiguous.
