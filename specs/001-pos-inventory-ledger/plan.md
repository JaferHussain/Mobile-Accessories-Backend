# Implementation Plan: POS, Inventory & Customer Ledger System

**Branch**: `001-pos-inventory-ledger` | **Date**: 2026-09-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-pos-inventory-ledger/spec.md`

## Summary

A single-shop point-of-sale, inventory and customer-credit (udhaar) system for Moiz Mobile &
Corporation. The counter records sales against a variant-per-row product catalogue, decrementing
stock and raising customer balances atomically; purchases raise stock and supplier payables and
overwrite the product's cost under a latest-purchase-cost rule; returns invert either operation;
and a read-only reporting layer turns the resulting ledger into daily/monthly/annual profit,
receivables and stock reports. Staff may sell but may never see cost or profit.

Technically: an ASP.NET Core 8 Web API layered Controller → Service → Repository over Dapper and
MySQL 8, paired with a React + TypeScript counter application. Every money-touching operation
runs inside a database transaction that locks the affected product rows before verifying stock,
and every calculation is recomputed server-side from persisted line items rather than trusted
from the client. Built test-first in ten dependency-ordered phases mirroring the spec's user
story priorities.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (backend); TypeScript 5.4 with React 18 (frontend)

**Primary Dependencies**: ASP.NET Core 8 Web API, Dapper 2.1 + MySqlConnector, FluentValidation
11, DbUp (migrations), QuestPDF (invoice/receipt PDFs, Community licence), Serilog (structured
logging); React Router 6, TanStack Query 5, Axios, React Hook Form + Zod, Vite

**Storage**: MySQL 8, normalized schema, versioned SQL migrations. Product images on disk with
relative paths persisted in the database. Backups as compressed `mysqldump` output.

**Testing**: xUnit + Moq + FluentAssertions (backend unit); xUnit + `WebApplicationFactory`
against a disposable MySQL schema (backend integration); Vitest + React Testing Library +
MSW (frontend)

**Target Platform**: Windows or Linux server on the shop premises or a small VPS; browser clients
on desktop and tablet at the counter

**Project Type**: Web application — separate `backend/` and `frontend/` projects

**Performance Goals**: Product search returns in under 2 seconds over a 5,000-item catalogue
(SC-002); dashboard fully rendered within 10 seconds (SC-008); a three-item sale completed end
to end in under 60 seconds of operator time (SC-001)

**Constraints**: No floating-point arithmetic in any money path; stock may never go negative;
every stock/balance/invoice mutation is all-or-nothing; Staff principals must never receive cost
or profit data from any endpoint; all reporting periods resolve against `Asia/Karachi` (UTC+05:00)

**Scale/Scope**: One shop, one or two concurrent counter terminals, 2–5 named users, ~5,000
product variants, order of 100–300 sales per day. Roughly 45 API endpoints and 15 React screens
across 10 phases.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against **Moiz Mobile POS Constitution v1.0.0**.

| Principle | Gate | Initial (pre-research) | Post-design |
|---|---|---|---|
| I. TDD (NON-NEGOTIABLE) | Every story has a failing test before implementation; stock/profit/balance/discount/permission maths unit-tested | PASS — plan sequences all work as test-first pairs, and `/speckit-tasks` is instructed to emit them | PASS |
| II. Layered Backend | Controller → Service → Repository; Dapper only; MySQL 8; FluentValidation; JWT with per-endpoint authorization | PASS — structure below enforces the three layers; no ORM introduced | PASS |
| III. Frontend Logic Tested | Stateful components with business logic have component tests; calculations extracted to pure functions | PASS — cart/ledger maths isolated in `frontend/src/lib/` (R12) | PASS |
| IV. Transactional & Server-Authoritative | Single transaction per money mutation; server recomputes totals; stock never negative | PASS — row-locking strategy resolved in R4; `decimal`/`DECIMAL` throughout per R5 | PASS |
| V. Complete Vertical Slices | Each module ships migration + repository + tested service + tested controller + React screen | PASS — phase plan below lists all five artifacts per phase | PASS |
| VI. Definition of Done | Unit green + integration green + no previously green test broken | PASS — full-suite run is the gate on every phase | PASS |

### Constraint checks

- **Cost confidentiality** — satisfied by role-separated DTOs, an `AdminOnly` policy on every
  cost/profit route, and an architecture test asserting no Staff-reachable DTO carries a cost or
  profit property (R10).
- **Auditability** — a single `audit_entries` table written from inside the same transaction as
  the mutation it records, so an audit row cannot survive a rolled-back change.
- **API shape** — shared `ApiResponse<T>` / `ApiError` envelope and a `PagedResult<T>` used by
  every list endpoint, both established in Phase 1 before any domain module exists.
- **Migrations** — DbUp with numbered scripts; no ad-hoc schema edits (R1).

### Justified exception (one)

**Constitution requirement**: "Authentication MUST be enforced on every endpoint."

**Exception**: `GET /api/public/documents/{token}` is unauthenticated.

**Why it is unavoidable**: FR-044 requires a customer to open their receipt from a WhatsApp
message. The recipient is a shop customer, not a system user, and will never hold a credential.
Research R2 establishes that `wa.me` cannot attach a file, so a retrievable link is the only
mechanism available.

**How it is contained**: the token is ≥128 bits of cryptographic randomness, maps to exactly one
document, expires after 30 days, is revocable, and the endpoint returns nothing but that PDF —
no list, no lookup by invoice number, no other field. It is rate-limited and its access is
logged. This is recorded in Complexity Tracking below.

## Project Structure

### Documentation (this feature)

```text
specs/001-pos-inventory-ledger/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── openapi.yaml
│   └── conventions.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── MoizPos.Api/                 # Controllers, middleware, DI, auth policies, Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/              # Exception → error envelope, request logging
│   │   └── Authorization/           # AdminOnly policy, role constants
│   ├── MoizPos.Application/         # Services, DTOs, validators, pure calculators
│   │   ├── Services/
│   │   ├── Contracts/               # Request/response DTOs, role-separated
│   │   ├── Validation/              # FluentValidation validators
│   │   └── Calculations/            # Pure: invoice totals, profit, running balance
│   ├── MoizPos.Domain/              # Entities, enums, domain errors
│   ├── MoizPos.Infrastructure/      # Dapper repositories, connection factory, IClock,
│   │   │                            # PDF service, backup service, audit writer
│   │   ├── Repositories/
│   │   ├── Documents/
│   │   └── Backup/
│   └── MoizPos.Migrator/            # DbUp host + embedded numbered SQL scripts
│       └── Scripts/                 # 0001_users_roles.sql, 0002_products.sql, ...
└── tests/
    ├── MoizPos.UnitTests/           # Calculators, services (mocked repositories), validators
    ├── MoizPos.IntegrationTests/    # Endpoints against a disposable MySQL schema
    └── MoizPos.ArchitectureTests/   # Staff DTOs carry no cost/profit property

frontend/
├── src/
│   ├── api/                         # Axios client, interceptors, typed endpoint wrappers
│   ├── components/                  # Reusable UI (DataTable, MoneyInput, LowStockBadge)
│   ├── features/                    # Screen groups by domain
│   │   ├── auth/  products/  purchases/  suppliers/
│   │   ├── pos/   customers/  returns/   expenses/
│   │   └── reports/  dashboard/  admin/
│   ├── lib/                         # PURE, heavily unit-tested: cart maths, ledger balance,
│   │                                # money formatting, wa.me link builder
│   ├── routes/                      # Router config, ProtectedRoute, role-aware nav shell
│   └── types/
└── tests/                           # Vitest setup, MSW handlers, shared test utilities
```

**Structure Decision**: Web application with separate `backend/` and `frontend/` trees, chosen
because the spec requires a browser counter client and a server that is the sole authority on
money (Constitution Principle IV). The backend splits into four projects so the layering the
constitution mandates is enforced by project references rather than convention: `Domain` depends
on nothing, `Application` depends on `Domain`, `Infrastructure` implements `Application`'s
repository interfaces, and `Api` composes them. A repository can therefore never reach into a
controller, and a service can never open a database connection directly. `MoizPos.Migrator` is
separate so migrations can run in deployment and in integration-test setup without booting the
API.

## Phase Plan

Ordered by the spec's user-story priorities. Each phase ships the full vertical slice required by
Constitution Principle V and must leave the whole suite green (Principle VI) before the next
begins.

| # | Phase | Delivers | Spec coverage |
|---|---|---|---|
| 1 | Foundation & Auth | Migrations for users/roles, JWT + refresh, `AdminOnly` policy, error envelope, `PagedResult`, `IClock`, health check, React shell + login + protected routes | US6 (partial), FR-038/039, FR-048/049 |
| 2 | Product & Variant Catalogue | Products CRUD, unified search, low-stock flag, image upload, stock-movement ledger | US3 (partial), FR-001–006 |
| 3 | Purchases & Suppliers | Suppliers CRUD, `RecordPurchase` (stock ↑, payable ↑, **cost overwrite**), supplier payments, purchase history | US3, FR-007–011d |
| 4 | Sales / POS / Invoicing | Customers table, `CreateInvoice` (locked stock ↓, balances, payment methods), POS screen | US1, FR-012–018 |
| 5 | Customer Ledger | Running-balance ledger, `ReceivePayment`, customer profile totals | US2, FR-019–023 |
| 6 | Returns | Sale return and purchase return, inverted transactions at originally recorded cost | US5, FR-024–028 |
| 7 | Expenses | Expense CRUD, period-sum query for the profit engine | FR-029/030 |
| 8 | Profit, Dashboard & Reports | Read-only aggregations, period boundaries, all reports, KPI dashboard | US4, FR-031–037 |
| 9 | PDF & WhatsApp | QuestPDF invoice/receipt, tokenized document endpoint, `wa.me` link builder | US7, FR-042–044 |
| 10 | Hardening | Role enforcement tests, audit retrofit, scheduled backup, restore | US6, US8, FR-040/041, FR-045–047 |

Phase 2 introduces the `stock_movements` write path used by Phases 3, 4 and 6. Phase 3
establishes the cost-overwrite rule that Phase 8's profit engine depends on. Phase 10's audit
work is a retrofit into the existing transactional services, not a rewrite.

## Complexity Tracking

> Filled because the Constitution Check records one justified exception.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Unauthenticated `GET /api/public/documents/{token}` | FR-044 requires a customer — who holds no credential and never will — to open their own receipt from a WhatsApp message; `wa.me` cannot carry an attachment (R2) | Requiring authentication would mean issuing shop customers logins, which is disproportionate and unusable at a counter. Sending only plain text loses the receipt entirely. Containment: ≥128-bit token, single document, 30-day expiry, revocable, rate-limited, access logged. |
| Four backend projects rather than one | Makes the constitution's mandated Controller → Service → Repository layering a compile-time guarantee via project references | A single project relies on folder convention alone; nothing prevents a repository from referencing a controller or a service from opening a connection, and the violation is invisible in review. |
