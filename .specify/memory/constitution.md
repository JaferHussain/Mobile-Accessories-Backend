<!--
SYNC IMPACT REPORT
Version change: (unratified template) → 1.0.0
Rationale: Initial ratification. All placeholder tokens replaced with concrete project
governance; no prior adopted version existed, so this is a first release rather than a bump.

Modified principles:
  [PRINCIPLE_1_NAME] → I. Test-Driven Development (NON-NEGOTIABLE)
  [PRINCIPLE_2_NAME] → II. Layered Backend Architecture
  [PRINCIPLE_3_NAME] → III. Frontend Business Logic Is Tested
  [PRINCIPLE_4_NAME] → IV. Transactional & Server-Authoritative Money
  [PRINCIPLE_5_NAME] → V. Complete Vertical Slices
  (added, beyond template's 5 slots) → VI. Definition of Done

Added sections:
  [SECTION_2_NAME] → Technology Stack & Constraints
  [SECTION_3_NAME] → Development Workflow & Quality Gates

Removed sections: none

Deferred items: none.

---
Version change: 1.0.1 → 1.1.0 (MINOR)
Rationale: Principle II gains an explicit statement of how the layering is enforced. The backend
was collapsed from five projects into one on 2026-09-19 at the owner's request, which removed the
project references that had enforced the layers implicitly. The layering RULE is unchanged and
undiminished; what changed is the mechanism, which is now LayeringTests in the architecture test
project. MINOR rather than PATCH because guidance was materially expanded, and MINOR rather than
MAJOR because no principle was removed or redefined — a change that weakened the rule would have
been refused.

Migration note: none required for existing code. The single project keeps the same namespaces, so
no source file changed. `dotnet run --project backend/src/MoizPos.Migrator` becomes
`dotnet run --project backend/src/MoizPos -- migrate`.

---
Version change: 1.0.0 → 1.0.1 (PATCH)
Rationale: clarification only. The deferred TODO(GUIDANCE_FILE) is resolved — CLAUDE.md now
exists at the repository root and Governance references it. No principle was added, removed or
redefined.
-->

# Moiz Mobile POS Constitution

## Core Principles

### I. Test-Driven Development (NON-NEGOTIABLE)

For every user story, a failing unit test MUST exist before the implementation that makes it
pass. Backend tests use xUnit; frontend tests use Vitest. No task is complete without a green
test suite, and no module is "done" without passing unit tests covering its business rules —
specifically stock math, profit math, balance math, discount math, and permission checks.

Rationale: Sale, stock, ledger, and profit calculations are mutually dependent; a silent error
in any one of them corrupts every downstream report and destroys the owner's trust in the
system. Tests written first are the only reliable guard against that.

### II. Layered Backend Architecture

The backend MUST be an ASP.NET Core 8 Web API organized strictly as
Controller → Service → Repository. Data access MUST use Dapper only; EF Core and other ORMs are
prohibited. MySQL 8 is the database. Request validation MUST use FluentValidation. Authentication
MUST use JWT, and role-based authorization MUST be enforced on every endpoint — never by hiding
controls in the interface alone.

The dependency direction is fixed: Domain depends on nothing, Application on Domain,
Infrastructure on Application, and the Api host on all of them. **This MUST be enforced
automatically, and a build that cannot fail on a violation does not satisfy this principle.** The
project layout is free to change — one project or several — but whichever layout is chosen MUST
carry an enforcement mechanism with it. As of 2026-09-19 the backend is a single project and the
mechanism is `LayeringTests`, which reads each file's namespace and using directives; before that
it was project references. Relaxing the rule to suit a layout is not permitted; the layout gives
way instead.

Rationale: Enforcement is the whole point — a layering rule nobody can break is architecture, and
one held up only by good intentions is a preference. A single, explicitly bounded data-access
approach keeps the money-handling SQL
visible and reviewable. Authorization enforced at the endpoint is the only form that survives a
caller who bypasses the UI.

### III. Frontend Business Logic Is Tested

The frontend MUST be React with TypeScript. Every stateful component containing business logic —
totals, discounts, balances, remaining amounts — MUST have component tests. Calculation logic
SHOULD be extracted into pure functions so it can be unit tested independently of rendering.

Rationale: The POS cart is where the shopkeeper sees the number they will charge. It must be
correct on screen, and correctness that is only asserted through the UI is fragile to test.

### IV. Transactional & Server-Authoritative Money

Every mutation touching stock, customer balances, supplier payables, or invoices MUST execute
inside a single database transaction that either completes fully or leaves no trace. All
monetary and stock calculations MUST be computed and re-validated server-side on every write;
client-supplied totals MUST NOT be trusted. Stock MUST NOT go negative through any code path.

Rationale: A crash midway through a sale must never deduct goods from the shelf without
recording the sale, or raise a customer's debt without an invoice behind it. Partial state in a
ledger is worse than no system at all.

### V. Complete Vertical Slices

Every module MUST ship with all of: a versioned SQL migration script, a repository, a service
with unit tests, a controller with integration tests, and a minimal React screen with component
tests. Partial slices MUST NOT be merged.

Rationale: A service without a migration cannot run; a controller without a screen cannot be
demonstrated. Shipping whole slices keeps the system continuously usable and prevents a backlog
of invisible half-built modules.

### VI. Definition of Done

A module is done when, and only when, all three hold: its unit tests pass; integration tests for
its main API endpoints pass; and it breaks no previously green test. A change that reddens an
existing test is not done regardless of the new tests it adds.

Rationale: The value of the suite is regression protection. Allowing a merge that breaks earlier
tests forfeits exactly the protection the earlier work paid for.

## Technology Stack & Constraints

- **Backend**: ASP.NET Core 8 Web API, C#, layered as Controller → Service → Repository.
- **Data access**: Dapper exclusively. No EF Core, no alternative ORM.
- **Database**: MySQL 8, normalized schema. Schema changes MUST be applied through
  version-controlled SQL migration scripts run by a migration tool (DbUp or Flyway); ad-hoc
  schema edits against any shared database are prohibited.
- **Validation**: FluentValidation for all inbound request models.
- **Auth**: JWT bearer tokens carrying a role claim of `Admin` or `Staff`.
- **Frontend**: React + TypeScript, component-driven, REST client, React Router, responsive for
  desktop and tablet use at the counter.
- **Testing**: xUnit + Moq (backend unit and integration); Vitest + React Testing Library
  (frontend).
- **Cost confidentiality**: purchase price, profit figures, and financial reports MUST NOT be
  served to a `Staff` principal by any endpoint or field projection.
- **Auditability**: every stock and ledger mutation MUST record user, field, value before, value
  after, and timestamp.
- **API shape**: list endpoints MUST be paginated; all endpoints MUST return the shared,
  consistent error envelope.

## Development Workflow & Quality Gates

- Work proceeds in the phase order established by the feature specification's story priorities:
  foundation and auth, then catalogue, purchasing, selling, customer ledger, returns, expenses,
  reporting, document delivery, and finally operational hardening. A phase MUST NOT begin before
  the modules it depends on have green tests.
- Task lists MUST be generated as pairs: a failing-test task immediately preceding its
  implementation task. A task list that emits implementation without a preceding test task is
  rejected.
- Every change MUST run the full backend and frontend test suites before being accepted.
- Any deviation from these principles MUST be justified in writing at review time, or the change
  is rejected. Complexity that cannot be justified is removed rather than merged.

## Governance

This constitution supersedes all other development practices for this project. Where a task
prompt, template, or convenience conflicts with a principle here, the principle wins.

**Amendments** MUST be recorded in this file with an updated Sync Impact Report, a version bump,
and an amended date. An amendment that removes or redefines a principle in a
backward-incompatible way requires an explicit migration note describing what existing code must
change.

**Versioning policy** follows semantic versioning:

- **MAJOR**: a principle or governance rule is removed or redefined incompatibly.
- **MINOR**: a new principle or section is added, or existing guidance is materially expanded.
- **PATCH**: clarifications, wording, and typo fixes that do not change meaning.

**Compliance review**: every review MUST verify that the change carries its tests, keeps money
and stock mutations transactional, and does not expose cost or profit data to Staff. Runtime
development guidance lives in [CLAUDE.md](../../CLAUDE.md) at the repository root, which records
the practical rules and the specific traps that have already caused defects in this codebase.

**Version**: 1.1.0 | **Ratified**: 2026-09-09 | **Last Amended**: 2026-09-19
