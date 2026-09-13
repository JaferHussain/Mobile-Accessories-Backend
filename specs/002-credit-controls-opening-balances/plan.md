# Implementation Plan: Credit Controls & Customer Opening Balances

**Branch**: `002-credit-controls-opening-balances` | **Date**: 2026-09-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-credit-controls-opening-balances/spec.md`

## Summary

Three changes to how the shop handles money owed to it:

1. **Only the owner may sell on credit.** Enforced in `InvoiceService` against the *recomputed*
   `AmountRemaining`, so it cannot be evaded by mislabelling the payment method or understating a
   total. A Staff caller selling for full payment is unaffected.
2. **Partial recovery already works** — this feature proves it rather than rebuilding it.
3. **Customers get an opening balance**: what they owed on paper before the software existed,
   held as a column on `customers` plus a new `OpeningBalance` ledger entry, with re-recording
   treated as a correction by difference rather than a second debt.

One migration (`0015`), one new endpoint, one changed endpoint, and role-aware POS controls.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (backend); TypeScript 5 / React 18 (frontend)

**Primary Dependencies**: ASP.NET Core 8, Dapper, FluentValidation, DbUp, xUnit + Moq,
Vitest + React Testing Library, TanStack Query

**Storage**: MySQL 8. New migration `0015_customer_opening_balance.sql`; no existing script edited.

**Testing**: `dotnet test` (unit, integration, architecture); `npm run test` + `tsc --noEmit`

**Target Platform**: Windows desktop/tablet at the shop counter; API and MySQL on the same machine

**Project Type**: Web application — existing `backend/` + `frontend/`

**Performance Goals**: No new hot path. The credit check is an in-memory comparison on a value
already computed. Recording an opening balance is one locked-row transaction, comparable to
receiving a payment (already well under the shop's needs at ~5,000 products / hundreds of
customers).

**Constraints**: Money is `decimal` throughout, `DECIMAL(12,2)` in MySQL. Every balance mutation
runs in one transaction with the customer row locked `FOR UPDATE`. Cost and profit remain
unreachable by a Staff principal. UTC storage, `Asia/Karachi` period boundaries.

**Scale/Scope**: One shop, two roles, two users. Roughly 6 backend files changed, 4 added; 4
frontend files changed, 2 added.

## Constitution Check

*GATE: passed before Phase 0. Re-checked after Phase 1 design — see below.*

| Principle | How this feature complies |
|---|---|
| **I. TDD (non-negotiable)** | Every task pair in `tasks.md` will be a failing test followed by the implementation that greens it. The credit rule in particular is a permission check, which the constitution names explicitly as requiring unit tests. |
| **II. Layered backend** | The credit rule lives in `InvoiceService` (Service layer); the opening balance adds a repository method, a service method and a controller action in the existing `Controller → Service → Repository` order. Dapper only. FluentValidation for the new request. Role checked at the endpoint **and** in the service — see the note below. |
| **III. Frontend logic tested** | The POS's role-aware payment options change what the shopkeeper can charge, so they get component tests. The opening-balance form is a stateful component with validation and gets its own tests. |
| **IV. Transactional & server-authoritative** | The credit decision uses the server's recomputed total, never the client's. Recording an opening balance locks the customer row before reading the balance, then writes column, ledger entry and audit row in one unit of work. |
| **V. Complete vertical slices** | Migration → repository → service (+ unit tests) → controller (+ integration tests) → React screen (+ component tests). Nothing ships half-built. |
| **VI. Definition of done** | Baseline is 490 backend and 250 frontend tests. The feature is done only when both suites are green and no previously passing test has been reddened. |

**Cost confidentiality**: untouched. This feature adds no cost or profit field, and the opening
balance is money owed *to* the shop, which Staff can already see on the customer's ledger.

**Auditability**: FR-072 is a direct instance of the constitution's rule that every ledger
mutation records user, field, before, after and timestamp — satisfied through the existing
`IAuditWriter`.

**A note on where the role check sits.** The constitution says authorization MUST be enforced at
the endpoint and never by hiding controls in the interface alone. The credit rule cannot be a
policy attribute, because whether a sale is a credit sale is only known after the server
recomputes the totals (research R1). It is therefore enforced inside the service, on the server,
on every request — which satisfies the intent of the principle (the rule survives a caller who
bypasses the UI) even though the mechanism is a service check rather than a policy attribute. The
opening-balance endpoint *is* a plain `AdminOnly` policy, since that decision needs no computed
state. Recorded in Complexity Tracking below.

### Post-design re-check

Re-evaluated after Phase 1. No new violations. The design adds no new project, no new data-access
mechanism, and no new authorization concept beyond the two already in use.

## Project Structure

### Documentation (this feature)

```text
specs/002-credit-controls-opening-balances/
├── plan.md              # This file
├── spec.md              # Phase -1 (/speckit-specify)
├── research.md          # Phase 0 — 9 decisions, no open questions
├── data-model.md        # Phase 1 — migration 0015, invariants, transaction boundaries
├── quickstart.md        # Phase 1 — V1..V16 end-to-end checks
├── contracts/
│   └── openapi.yaml     # Phase 1 — delta on feature 001's contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (all passing)
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

Real paths in the existing tree. **Changed** unless marked new.

```text
backend/
├── src/
│   ├── MoizPos.Migrator/Scripts/
│   │   └── 0015_customer_opening_balance.sql          # NEW
│   ├── MoizPos.Domain/
│   │   ├── Entities/Entities.cs                       # Customer.OpeningBalance
│   │   └── Enums/DomainEnums.cs                       # LedgerEntryType.OpeningBalance
│   ├── MoizPos.Application/
│   │   ├── Abstractions/IInvoiceAbstractions.cs       # ICustomerRepository lives here
│   │   ├── Contracts/Customers/                       # NEW folder
│   │   │   └── OpeningBalanceContracts.cs             # NEW — request + validator
│   │   ├── Services/InvoiceService.cs                 # the credit rule
│   │   └── Services/CustomerLedgerService.cs          # SetOpeningBalanceAsync
│   ├── MoizPos.Infrastructure/Repositories/
│   │   └── CustomerRepository.cs                      # read/write opening balance under lock
│   └── MoizPos.Api/Controllers/
│       ├── InvoicesController.cs                      # pass the caller's role through
│       └── CustomersController.cs                     # PUT {id}/opening-balance, AdminOnly
└── tests/
    ├── MoizPos.UnitTests/Invoices/CreditAuthorityTests.cs        # NEW
    ├── MoizPos.IntegrationTests/Invoices/CreditSaleRoleTests.cs  # NEW
    ├── MoizPos.IntegrationTests/Ledger/PartialRecoveryTests.cs   # NEW (verification)
    └── MoizPos.IntegrationTests/Ledger/OpeningBalanceTests.cs    # NEW

frontend/
├── src/features/
│   ├── pos/PosScreen.tsx                              # hide credit/partial from Staff
│   ├── pos/posApi.ts                                  # payment options by role
│   └── customers/
│       ├── customerApi.ts                             # setOpeningBalance
│       └── OpeningBalanceForm.tsx                     # NEW
└── tests/features/
    ├── pos/PosScreen.test.tsx                         # role-aware payment options
    └── customers/OpeningBalance.test.tsx              # NEW
```

**Structure Decision**: The existing two-project web application layout is kept unchanged. This
feature adds no new project and no new layer — it extends three existing vertical slices
(selling, the customer ledger, the POS screen), which is why every path above is either an
existing file or a new file inside an existing folder.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Credit authorization enforced in the **service**, not as an endpoint policy attribute | Whether a sale is credit is a property of the server-recomputed `AmountRemaining`, which does not exist until after `InvoiceCalculator` runs. Constitution IV forbids deciding it from client-supplied totals. | An `AdminOnly` policy on `POST /api/invoices` would block the salesman from ordinary fully-paid sales — the shop's main trade. A separate Admin-only credit endpoint would leave the ordinary endpoint still accepting a part-paid sale from Staff, so the rule would not hold. |
| The `OpeningBalance` ledger entry is **not back-dated** ahead of existing entries | `ledger_entries.balance_after` is persisted by design (migration 0009) so the ledger screen is one indexed read. Inserting earlier would invalidate every subsequent `balance_after`. | Rewriting the stored running balance of an append-only register to improve display order trades a real invariant for a cosmetic one. In the intended flow — a customer entered from the paper register with what they owe — the entry *is* first, so FR-067 holds with no deviation. |

## Phase Sequence

Story priority from the spec drives the order, and each phase is independently shippable.

1. **Foundation** — migration `0015`, domain enum and entity, repository plumbing. Blocks the rest.
2. **US1 — Admin-only credit (P1)**. Backend rule with unit + integration tests, then the POS
   role-aware controls. Delivers the money-at-risk protection on its own.
3. **US3 — Opening balances (P2)**. Service, endpoint, screen, tests. Independently demonstrable.
4. **US2 — Partial recovery (P2)**. Verification tests only. Deliberately last: it proves
   existing behaviour, so it cannot block anything, and running it after US1 also confirms the
   credit rule did not break a salesman's ability to collect (FR-054).
5. **Polish** — quickstart V1–V16 against a live database, `CLAUDE.md` updated, both suites green.

## Risks

| Risk | Mitigation |
|---|---|
| The credit rule accidentally blocks a salesman from *receiving payments* (FR-054) | An explicit integration test asserts a Staff user can still record a recovery payment. The rule is scoped to invoice creation only. |
| A correction doubles a customer's debt instead of adjusting it | Data-model invariant 3 plus quickstart V11 assert the balance moves by exactly the difference. This is the single most damaging way this feature could fail. |
| Adding to the `entry_type` ENUM disturbs existing rows | The value is appended, preserving every existing ordinal. Verified by running the full integration suite, which reads back existing entry types. |
| Existing tests assume Staff can create credit sales | **Confirmed, one test.** `RoleEnforcementTests.Staff_can_create_a_sale_and_receive_a_payment` posts a `Partial` sale as Staff (400 paid of 1,000) and asserts 201; under FR-052 that becomes 403. It is split into two tests — a salesman completing a *fully paid* sale, and a salesman refused a part-paid one — so the original intent ("selling is the salesman's job") is kept and the new rule is asserted alongside it. Not deleted, and the change is noted in the task list. |
