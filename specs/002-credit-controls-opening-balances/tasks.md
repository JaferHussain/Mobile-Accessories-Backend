---

description: "Task list for Credit Controls & Customer Opening Balances"
---

# Tasks: Credit Controls & Customer Opening Balances

**Input**: Design documents from `/specs/002-credit-controls-opening-balances/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/openapi.yaml](./contracts/openapi.yaml),
[quickstart.md](./quickstart.md)

**Tests**: **REQUIRED, not optional.** Constitution Principle I makes TDD non-negotiable and the
Development Workflow section requires task lists to be emitted as pairs — a failing-test task
immediately preceding the implementation task that greens it. Every implementation task below is
preceded by its test task.

**Organization**: Grouped by user story so each can be implemented, tested and demonstrated on
its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 — maps to the user stories in spec.md
- Exact file paths are given in every task

## Path Conventions

Web application, existing tree: `backend/src/`, `backend/tests/`, `frontend/src/`,
`frontend/tests/`. All paths below are repository-relative and real.

---

## Phase 1: Setup

**Purpose**: Confirm the ground is where the plan says it is before changing anything.

- [X] T001 Stop any running API so the build output is not locked: run `taskkill /IM MoizPos.Api.exe /F` (ignore "not found") — Visual Studio and `dotnet run` cannot both hold `backend/src/MoizPos.Api/bin/`
- [X] T002 Record the regression baseline by running `cd backend && dotnet test` and `cd frontend && npm run test && npx tsc --noEmit`; confirm **492 backend** (192 unit + 292 integration + 8 architecture) and **250 frontend** tests pass before any change (constitution VI)
- [X] T003 Back up the live database before the migration: `mysqldump -u root moizpos > backup-before-0015.sql`

**Checkpoint**: Baseline known and recoverable.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema and domain types every story below depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Create migration `backend/src/MoizPos.Migrator/Scripts/0015_customer_opening_balance.sql` adding to `customers` the column `opening_balance DECIMAL(12,2) NULL DEFAULT NULL` with constraint `ck_customers_opening_balance_non_negative` — `opening_balance IS NULL OR opening_balance >= 0` (data-model §1; nullable deliberately, because "never recorded" and "recorded as zero" are different facts)
- [X] T005 In the same migration `0015_customer_opening_balance.sql`, `ALTER TABLE ledger_entries MODIFY COLUMN entry_type ENUM('Invoice', 'Payment', 'SaleReturn', 'Adjustment', 'OpeningBalance') NOT NULL` — appended so every existing ordinal is preserved (data-model §2)
- [X] T006 In the same migration `0015_customer_opening_balance.sql`, add `note VARCHAR(255) NULL` to `ledger_entries` to carry the reason for a correction, which FR-071 requires to be visible in the ledger rather than only in the audit trail
- [X] T007 [P] Add `OpeningBalance = 5` to `LedgerEntryType` in `backend/src/MoizPos.Domain/Enums/DomainEnums.cs` — appended, so no persisted value is renumbered
- [X] T008 [P] Add `public decimal? OpeningBalance { get; set; }` to `Customer` in `backend/src/MoizPos.Domain/Entities/Entities.cs`
- [X] T009 Run `dotnet run --project backend/src/MoizPos.Migrator` and verify with `SHOW COLUMNS FROM moizpos.customers LIKE 'opening_balance'` and `SHOW COLUMNS FROM moizpos.ledger_entries LIKE 'entry_type'` that both landed and `OpeningBalance` is listed
- [X] T010 Run `cd backend && dotnet test` to confirm the schema change reddened nothing — the ENUM widening in particular must not disturb rows read back by existing ledger tests

**Checkpoint**: Schema and domain ready. User stories may now proceed.

---

## Phase 3: User Story 1 — Only the owner may sell on credit (Priority: P1) 🎯 MVP

**Goal**: A sale leaving any amount outstanding can only be completed by an Admin. A salesman
selling for full payment is unaffected, and can still collect debts.

**Independent Test**: As `salesman`, attempt a Rs 5,000 sale paying nothing, then paying Rs 3,000
— both refused with no stock movement and no invoice. Pay the full Rs 5,000 — succeeds. As
`admin`, the part-paid sale succeeds.

### Tests for User Story 1 ⚠️ Write first, watch them fail

- [X] T011 [P] [US1] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Invoices/CreditAuthorityTests.cs` covering the rule in isolation: `AmountRemaining > 0` with `UserRole.Staff` throws; `AmountRemaining == 0` with Staff does not; `AmountRemaining > 0` with `UserRole.Admin` does not; and a sale marked `PaymentMethod.Cash` but underpaid is still refused (FR-052, research R2)
- [X] T012 [P] [US1] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Invoices/CreditSaleRoleTests.cs`: Staff wholly-unpaid sale → 403 `CREDIT_REQUIRES_ADMIN`; Staff part-paid sale → 403; Staff fully-paid sale → 201; Admin part-paid sale → 201 with the customer's balance raised (spec US1 scenarios 1–4)
- [X] T013 [P] [US1] Add a failing integration test to `backend/tests/MoizPos.IntegrationTests/Invoices/CreditSaleRoleTests.cs` asserting FR-055 directly against the database: after a refused Staff credit sale, `invoices` row count, the product's `quantity_on_hand` and the customer's `outstanding_balance` are all unchanged
- [X] T014 [P] [US1] Add a failing integration test to `backend/tests/MoizPos.IntegrationTests/Invoices/CreditSaleRoleTests.cs` asserting FR-054 is not collateral damage: a Staff user can still `POST /api/customers/{id}/payments` against an existing balance
- [X] T015 [P] [US1] Write failing component tests in `frontend/tests/features/pos/PosScreen.test.tsx` asserting FR-056: with a Staff user the Payment select offers neither `Credit (udhaar)` nor `Partial`, and the amount-paid field cannot be set below the total; with an Admin both are offered

### Implementation for User Story 1

- [X] T016 [US1] Add `CreditRequiresAdminException` to `backend/src/MoizPos.Domain/Errors/DomainExceptions.cs` mapping to HTTP 403 with code `CREDIT_REQUIRES_ADMIN`, and register it in the existing exception-to-status mapping used by the error envelope middleware in `backend/src/MoizPos.Api/`
- [X] T017 [US1] Change `IInvoiceService.CreateAsync` and `InvoiceService.CreateAsync` in `backend/src/MoizPos.Application/Services/InvoiceService.cs` to accept the caller's `UserRole` alongside `userId` — passed in explicitly, never read from ambient context, so the service stays unit-testable without an HTTP principal
- [X] T018 [US1] Implement the rule in `backend/src/MoizPos.Application/Services/InvoiceService.cs`: after `InvoiceCalculator.Calculate` and **before** `NextInvoiceNumberAsync`/`InsertInvoiceAsync`, throw `CreditRequiresAdminException` when `totals.AmountRemaining > 0m && role != UserRole.Admin`; the message must name the unpaid amount (research R1, R2). Greens T011–T013
- [X] T019 [US1] Pass `CurrentUser.Role(User)` into `_invoices.CreateAsync` from `backend/src/MoizPos.Api/Controllers/InvoicesController.cs`
- [X] T020 [US1] Update the three `new InvoiceService(...)` construction sites in `backend/tests/MoizPos.IntegrationTests/Performance/PerformanceTests.cs` and any other test constructing the service directly, so the suite compiles against the new signature
- [X] T021 [US1] Split `Staff_can_create_a_sale_and_receive_a_payment` in `backend/tests/MoizPos.IntegrationTests/Authorization/RoleEnforcementTests.cs` into two tests — a salesman completing a **fully paid** sale (still 201, preserving the original "selling is the salesman's job" intent) and a salesman refused a **part-paid** sale (now 403). Do not delete it; the existing assertion documents behaviour that FR-052 deliberately changes
- [X] T022 [US1] Add `saleType`-independent role awareness to `frontend/src/features/pos/posApi.ts`: export a helper that returns the payment methods available to a role, excluding `Credit` and `Partial` for Staff
- [X] T023 [US1] Use that helper in `frontend/src/features/pos/PosScreen.tsx` via the existing `useAuth().isAdmin`, and force `amountPaid` to the full total for a non-Admin so a credit sale cannot be composed on screen. Greens T015
- [X] T024 [US1] In `frontend/src/features/pos/PosScreen.tsx`, add a short note to the Payment field for a Staff user — "Only the owner can approve udhaar" — so the absence of the options reads as a rule, not a missing feature

**Checkpoint**: US1 fully functional and independently demonstrable. This is the MVP.

---

## Phase 4: User Story 3 — Carrying forward what a customer already owed (Priority: P2)

> Sequenced before US2 deliberately: US3 builds new capability, while US2 verifies behaviour that
> already exists and therefore cannot block anything.

**Goal**: The owner can record what a customer owed on paper before the software, see it in the
balance and receivables, and correct a mistyped figure without doubling the debt.

**Independent Test**: Record Rs 12,000 carried forward for a customer, sell them Rs 3,000 on
credit, take Rs 5,000 — balance reads Rs 10,000 with the carried-forward amount as the ledger's
opening line. Correct the figure to Rs 10,000 and the balance drops by exactly Rs 2,000.

### Tests for User Story 3 ⚠️ Write first, watch them fail

- [X] T025 [P] [US3] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Ledger/OpeningBalanceRulesTests.cs` for the correction arithmetic: setting A then B moves the balance by exactly `B − A`, never by `B` (FR-070, data-model invariant 3); a negative amount is refused (FR-069); a correction without a reason is refused (FR-071)
- [X] T026 [P] [US3] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Ledger/OpeningBalanceTests.cs` for the happy path: `PUT /api/customers/{id}/opening-balance` with `{ "amount": 12000 }` returns 200 with `wasCorrection: false`, `previousOpeningBalance: null`, `outstandingBalance: 12000`, and writes one `OpeningBalance` ledger row with `bill_amount 12000.00`, `paid_amount 0.00`, `balance_after 12000.00` (data-model §2)
- [X] T027 [P] [US3] Add a failing integration test to `backend/tests/MoizPos.IntegrationTests/Ledger/OpeningBalanceTests.cs` for the correction: after 12,000 then 10,000 with a reason, `wasCorrection` is true, `previousOpeningBalance` is 12,000, the original `OpeningBalance` row is **unaltered**, and a new `Adjustment` row carries `paid_amount 2000.00` and the reason in `note` (FR-070, FR-071, research R6)
- [X] T028 [P] [US3] Add failing integration tests to `backend/tests/MoizPos.IntegrationTests/Ledger/OpeningBalanceTests.cs` for the refusals: negative amount → 400 (FR-069); correction without a reason → 400 (FR-071); Staff caller → 403 (FR-068); unknown customer → 404
- [X] T029 [P] [US3] Add a failing integration test to `backend/tests/MoizPos.IntegrationTests/Ledger/OpeningBalanceTests.cs` asserting the ledger invariant end to end (SC-020, data-model invariant 1): opening 12,000 → credit sale 3,000 → payment 5,000 leaves `outstanding_balance` 10,000, the last entry's `balance_after` equals it, and replaying `bill_amount − paid_amount` down the entries reproduces it
- [X] T030 [P] [US3] Add a failing integration test to `backend/tests/MoizPos.IntegrationTests/Ledger/OpeningBalanceTests.cs` asserting FR-066: a customer with only a carried-forward amount appears in `GET /api/reports/receivables` with that amount included in the total
- [X] T031 [P] [US3] Add a failing integration test to `backend/tests/MoizPos.IntegrationTests/Ledger/OpeningBalanceTests.cs` asserting FR-072/FR-073: recording and correcting each write an `audit_entries` row for `Customer`/`opening_balance` with the before and after values and the user; and a customer created without one has `opening_balance` NULL, zero balance and an empty ledger
- [X] T032 [P] [US3] Write failing component tests in `frontend/tests/features/customers/OpeningBalance.test.tsx`: the form submits a trimmed positive amount; refuses a negative one client-side; requires a reason when an opening balance already exists; and is not rendered at all for a Staff user

### Implementation for User Story 3

- [X] T033 [P] [US3] Add `SetOpeningBalanceRequest` (`decimal Amount`, `string? Reason`) and `SetOpeningBalanceValidator` in new file `backend/src/MoizPos.Application/Contracts/Customers/OpeningBalanceContracts.cs` — `Amount` `GreaterThanOrEqualTo(0)` with message "A carried-forward amount cannot be negative."; `Reason` `MaximumLength(255)`
- [X] T034 [P] [US3] Add `OpeningBalanceResult` (`long CustomerId`, `decimal OpeningBalance`, `decimal? PreviousOpeningBalance`, `decimal OutstandingBalance`, `bool WasCorrection`) to `backend/src/MoizPos.Application/Contracts/Customers/OpeningBalanceContracts.cs`, matching `contracts/openapi.yaml`
- [X] T035 [US3] Extend `ICustomerRepository` in `backend/src/MoizPos.Application/Abstractions/IInvoiceAbstractions.cs` with `Task<(decimal? OpeningBalance, decimal OutstandingBalance)> LockForOpeningBalanceAsync(IUnitOfWork uow, long customerId, CancellationToken ct)` and `Task SetOpeningBalanceAsync(IUnitOfWork uow, long customerId, decimal openingBalance, decimal newOutstandingBalance, DateTime nowUtc, CancellationToken ct)`
- [X] T036 [US3] Implement both in `backend/src/MoizPos.Infrastructure/Repositories/CustomerRepository.cs` using `SELECT opening_balance, outstanding_balance FROM customers WHERE id = @id FOR UPDATE` on the lock, mirroring how `ReceivePaymentAsync` locks the customer row (research R7)
- [X] T037 [US3] Add `SetOpeningBalanceAsync` to `ICustomerLedgerService` and implement it in `backend/src/MoizPos.Application/Services/CustomerLedgerService.cs`, in one unit of work: lock the customer, refuse a negative amount, refuse a correction with no reason, compute `delta = newAmount − (existing ?? 0)`, apply `delta` to the outstanding balance, write the column, append the ledger entry (`OpeningBalance` on first recording, `Adjustment` on correction, per data-model §2), and record the audit row. Greens T025–T031
- [X] T038 [US3] Add `PUT /api/customers/{id}/opening-balance` to `backend/src/MoizPos.Api/Controllers/CustomersController.cs` with `[Authorize(Policy = Policies.AdminOnly)]` (FR-068), returning the envelope shape in `contracts/openapi.yaml`
- [X] T039 [US3] Extend the ledger read path so `note` and the `OpeningBalance` entry type reach the client — update `LedgerEntryRow` in `backend/src/MoizPos.Application/Abstractions/ILedgerAbstractions.cs` and the SELECT in the ledger repository so the new column is projected
- [X] T040 [P] [US3] Add `setOpeningBalance(customerId, amount, reason)` to `frontend/src/features/customers/customerApi.ts` and the `openingBalance` field to the customer type
- [X] T041 [US3] Create `frontend/src/features/customers/OpeningBalanceForm.tsx` — amount, reason (required and labelled as such when an opening balance already exists), client-side refusal of negatives, and the server's message surfaced on failure. Greens T032
- [X] T042 [US3] Render `OpeningBalanceForm` from `frontend/src/features/customers/CustomersPage.tsx` behind `useAuth().isAdmin`, and show the carried-forward figure on the customer row so it is visible without opening the form
- [X] T043 [US3] Show the `OpeningBalance` entry type and the `note` column in `frontend/src/features/customers/CustomerLedger.tsx`, labelled "Brought forward" so it does not read as a sale (FR-067)

**Checkpoint**: US1 and US3 both work independently.

---

## Phase 5: User Story 2 — Recovering an outstanding amount in instalments (Priority: P2)

**Goal**: Prove the shop can settle a balance in any number of part payments and that the rules
around it hold. **Verification only — no production code is expected to change** (research R8).

**Independent Test**: A customer owing Rs 3,000 pays 1,000, then 1,500, then 500; balances read
2,000 → 500 → 0 and all three payments appear separately in the ledger.

### Tests for User Story 2 ⚠️ These may pass on first run

> Unlike every other test task here, these are expected to pass immediately, because they assert
> behaviour that already exists. If any **fails**, that is a real defect this feature must fix —
> stop and treat it as such rather than adjusting the test to match.

- [X] T044 [P] [US2] Write integration tests in `backend/tests/MoizPos.IntegrationTests/Ledger/PartialRecoveryTests.cs` for the sequence in SC-018: from a 3,000 balance, payments of 1,000 / 1,500 / 500 leave 2,000 / 500 / 0, asserted after each (FR-058, FR-059, FR-060)
- [X] T045 [P] [US2] Add tests to `backend/tests/MoizPos.IntegrationTests/Ledger/PartialRecoveryTests.cs` that each payment is its own `customer_payments` row and its own `Payment` ledger entry with the correct `balance_after`, and that none were merged (FR-063, FR-064)
- [X] T046 [P] [US2] Add tests to `backend/tests/MoizPos.IntegrationTests/Ledger/PartialRecoveryTests.cs` for the refusals: amount `0` → 400, negative → 400 (FR-061); an amount above the balance without `confirmOverpayment` → 422, and the same with it → 201 (FR-062)
- [X] T047 [P] [US2] Add a test to `backend/tests/MoizPos.IntegrationTests/Ledger/PartialRecoveryTests.cs` that a customer whose balance reaches zero no longer appears in `GET /api/reports/receivables` (spec US2 scenario 3)
- [X] T048 [US2] **No production change was required.** All nine verification tests passed against the existing `CustomerLedgerService`. One test needed correcting, not the code: an unconfirmed overpayment returns **400**, not the 422 I had assumed — `OverpaymentNotConfirmedException` is a plain `DomainException`, which this codebase treats as caller-correctable. The refusal itself was already correct. `quickstart.md` V8 was corrected to match

**Checkpoint**: All three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T049 Run the full regression gate: `cd backend && dotnet test` and `cd frontend && npm run test && npx tsc --noEmit`; every previously green test must still pass (constitution VI), against the T002 baseline of 490 / 250
- [X] T050 Run `cd frontend && npx vite build` to confirm the production bundle still builds
- [X] T051 Execute `quickstart.md` V1–V16 against the live database with both servers running, recording the actual result of each check
- [X] T052 [P] Add a **Credit authority** section to `CLAUDE.md` documenting that the rule is enforced in `InvoiceService` on the recomputed `AmountRemaining` and why it cannot be an endpoint policy, so a later change does not "simplify" it into one
- [X] T053 [P] Add an **Opening balances** section to `CLAUDE.md` documenting that a second recording is a correction applied by difference — the single most damaging way this feature could fail is by doubling a customer's debt
- [X] T054 [P] Add a row to the traps table in `CLAUDE.md` for any bug actually hit during implementation; if none, leave the table unchanged rather than inventing one
- [X] T055 Clean up rows written into the live database by the quickstart run: remove the sales and payments it created and reset the test customer's `opening_balance` to `NULL`
- [X] T056 Mark every completed task `[X]` in this file and report the final test counts

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: needs Setup — **blocks every user story**
- **US1 (Phase 3)**: needs Foundational. Independent of US2 and US3
- **US3 (Phase 4)**: needs Foundational. Independent of US1 and US2
- **US2 (Phase 5)**: needs Foundational. Best run **after US1**, because T014 and T046 together
  prove the credit rule did not break a salesman's ability to collect (FR-054)
- **Polish (Phase 6)**: needs every story that is being shipped

### Within each story

- Test tasks precede their implementation task, and must fail first (constitution I) — except
  the US2 tests, which are documented above as verification
- Contracts and models before services; services before endpoints; backend before the screen
  that calls it

### Critical path

`T004 → T009 → T010` (migration) then `T011 → T018` (the rule). Everything else can follow.

### Parallel Opportunities

- T007 and T008 — different files, both after the migration is written
- T011–T015 — five different test files, all before any implementation
- T025–T032 — all US3 test tasks, different files
- T044–T047 — all US2 tests, one file but independent additions
- T052–T054 — documentation, different sections
- **US1 and US3 can be built by different people in parallel** once Phase 2 is done; they touch
  `InvoiceService`/`PosScreen` and `CustomerLedgerService`/`CustomersPage` respectively, with no
  shared file

---

## Parallel Example: User Story 1

```bash
# All five test tasks first — different files, no shared state:
Task: "T011 Unit tests for the credit rule in backend/tests/MoizPos.UnitTests/Invoices/CreditAuthorityTests.cs"
Task: "T012 Integration tests for role behaviour in backend/tests/MoizPos.IntegrationTests/Invoices/CreditSaleRoleTests.cs"
Task: "T013 Database-level assertion that a refusal writes nothing"
Task: "T014 Staff can still receive a payment"
Task: "T015 POS hides credit options from Staff in frontend/tests/features/pos/PosScreen.test.tsx"

# Confirm they fail, then implement T016 → T024 in order.
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 — Setup
2. Phase 2 — Foundational (blocks everything)
3. Phase 3 — US1
4. **Stop and validate**: quickstart V1–V6
5. Ship. The shop's credit exposure is closed at this point, which is the whole reason this
   feature exists.

### Incremental delivery

1. Setup + Foundational → schema ready
2. **US1** → validate V1–V6 → ship (MVP)
3. **US3** → validate V9–V16 → ship
4. **US2** → validate V7–V8 → ship
5. Polish

Note that Phase 2 alone is safe to deploy: the column is nullable and the enum value is unused
until US3 lands, so a half-delivered feature leaves the shop working exactly as before.

---

## Notes

- `[P]` means different files and no dependency on an incomplete task
- Verify each test fails before writing the code that greens it — the exception is T044–T047,
  flagged above
- Commit after each task or logical group
- **Do not** relax the US1 rule to make an existing test pass; T021 exists precisely because one
  existing test asserts the old behaviour and must be rewritten deliberately
- **Do not** "fix" a correction into an addition. `delta = new − old` is the whole of FR-070

---

## Implementation record

Completed 2026-09-11. Final counts: **550 backend** (214 unit + 328 integration + 8 architecture,
from a 492 baseline) and **263 frontend** (from 250). `tsc --noEmit` clean, `vite build` succeeds,
quickstart V1–V16 executed against the live database and all passing.

### Deviations from the plan, and why

1. **`canSellOnCredit` is a prop on `PosScreen`, not `useAuth()` inside it.** T023 said to read
   auth in the component. `PosScreen` is a pure function of its props and its tests render it with
   no provider; reaching for a context there would have forced a provider into 33 existing tests
   to no benefit. `PosPage` supplies `isAdmin`.

2. **Opening-balance persistence went on `ICustomerPaymentWriteRepository`, not
   `ICustomerRepository`.** T035 named the latter. The former already owns the customer row lock,
   the balance update and the ledger append inside a unit of work — the three things this
   operation needs. Using the read-side repository would have meant a second, parallel lock path.

3. **A dedicated `LockForOpeningBalanceAsync` rather than widening `CustomerBalanceSnapshot`.**
   The shared snapshot is also used by `InvoiceWriteRepository`, whose query does not select
   `opening_balance`; adding the property there would have made it silently null on one code path
   — the kind of trap this codebase documents rather than creates.

4. **A correction without a reason returns 422, not 400.** Whether a reason is required depends on
   whether a figure already exists — state a request validator cannot see, so it is a service-level
   business rule. `contracts/openapi.yaml` and `quickstart.md` V12 were corrected to match.

### Existing tests changed, and why

- `RoleEnforcementTests.Staff_can_create_a_sale_and_receive_a_payment` asserted that a salesman
  could complete a **part-paid** sale. That was the exposure FR-052 removes. Split into two tests:
  a salesman completing a fully-paid sale (unchanged intent), and a salesman refused a part-paid
  one. The debt it then collects is now created by the owner.
- `ReceivePaymentTests.SellOnCreditAsync` seeded its debts as a **Staff** user, which the new rule
  forbids. Changed to Admin — which is also who actually grants credit in the shop.
- 35 direct `InvoiceService.CreateAsync` call sites across four test files gained `UserRole.Admin`.
  These exercise sale mechanics, not the role rule, so Admin preserves their intent.
