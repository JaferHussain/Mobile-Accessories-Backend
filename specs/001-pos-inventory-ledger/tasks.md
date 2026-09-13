---
description: "Task list for POS, Inventory & Customer Ledger System"
---

# Tasks: POS, Inventory & Customer Ledger System

**Input**: Design documents from `/specs/001-pos-inventory-ledger/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: **REQUIRED, NOT OPTIONAL.** Constitution v1.0.0 Principle I makes TDD non-negotiable:
a failing test MUST exist before the implementation that makes it pass. Every implementation task
below is preceded by its test task. A phase is not done until unit tests pass, integration tests
for its endpoints pass, and no previously green test is broken (Principle VI).

**Organization**: Grouped by user story, ordered by the priorities in spec.md and the phase order
in plan.md.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps to a user story from spec.md (US1–US8)
- Exact file paths are given in every task

## Path Conventions

Web application per plan.md: `backend/src/`, `backend/tests/`, `frontend/src/`, `frontend/tests/`.

---

## ⚠️ Story independence — read before planning parallel work

The spec's user stories are prioritized and individually *testable*, but they are **not all
independently implementable**. Money flows through them in a fixed order: you cannot record a
credit sale (US2) before you can record a sale (US1), and you cannot sell (US1) what the
catalogue cannot hold (US3). The dependency table in [Dependencies](#dependencies--execution-order)
is authoritative. Treating these as freely parallel work streams will produce broken increments.

Genuinely parallel: **US7** (documents) and **US8** (backup/audit) after US2; **US4**'s expense
sub-module after Foundational.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository, solution, tooling. No business logic.

- [X] T001 Initialize git repository at repository root with a .NET + Node `.gitignore`, and create `README.md` describing the two-tree layout from plan.md
- [X] T002 Create the solution and four backend projects per plan.md: `backend/MoizPos.sln` with `backend/src/MoizPos.Api`, `backend/src/MoizPos.Application`, `backend/src/MoizPos.Domain`, `backend/src/MoizPos.Infrastructure`, `backend/src/MoizPos.Migrator`
- [X] T003 Wire project references so layering is compile-enforced (Constitution II): `Domain` → nothing; `Application` → `Domain`; `Infrastructure` → `Application`; `Api` → `Application` + `Infrastructure`; verify `Domain` has zero package references to ASP.NET or Dapper
- [X] T004 [P] Create the three test projects `backend/tests/MoizPos.UnitTests`, `backend/tests/MoizPos.IntegrationTests`, `backend/tests/MoizPos.ArchitectureTests` with xUnit, Moq and FluentAssertions
- [X] T005 [P] Add backend NuGet packages per plan.md: Dapper 2.1, MySqlConnector, FluentValidation 11 + `FluentValidation.DependencyInjectionExtensions`, DbUp.MySql, QuestPDF, Serilog.AspNetCore
- [X] T006 [P] Scaffold the React app at `frontend/` with Vite + TypeScript, and add React Router 6, TanStack Query 5, Axios, React Hook Form, Zod
- [X] T007 [P] Configure Vitest + React Testing Library + MSW in `frontend/vitest.config.ts` and `frontend/tests/setup.ts`
- [X] T008 [P] Add `.editorconfig` and `backend/Directory.Build.props` enabling nullable reference types and `TreatWarningsAsErrors`
- [X] T009 [P] Add ESLint + Prettier config at `frontend/.eslintrc.cjs` and `frontend/.prettierrc`
- [X] T010 [P] Create `frontend/.env.example` with `VITE_API_BASE_URL=http://localhost:5080/api`
- [X] T011 Configure Serilog structured logging and `appsettings.json` / `appsettings.Development.json` in `backend/src/MoizPos.Api`, reading the connection string from user-secrets in Development
- [X] T012 [P] Add QuestPDF Community licence declaration in `backend/src/MoizPos.Api/Program.cs` (`QuestPDF.Settings.License = LicenseType.Community`) per research.md R3

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Everything every user story needs. **No user story may begin until this completes.**

**⚠️ CRITICAL**: This phase establishes the transaction, money, time and auth primitives that all
correctness depends on.

### Migrations & data access

- [X] T013 Implement the DbUp migration host in `backend/src/MoizPos.Migrator/Program.cs` running embedded numbered scripts from `Scripts/`, per research.md R1
- [X] T014 Create migration `backend/src/MoizPos.Migrator/Scripts/0001_users_roles.sql` for `users` (username VARCHAR(50) UNIQUE NOT NULL, full_name VARCHAR(100) NOT NULL, password_hash VARCHAR(255) NOT NULL, role ENUM('Admin','Staff') NOT NULL, is_active BOOLEAN NOT NULL DEFAULT TRUE) and `refresh_tokens` (token_hash CHAR(64) UNIQUE, expires_at_utc DATETIME(6) NOT NULL, revoked_at_utc DATETIME(6) NULL) per data-model.md §1–2
- [X] T015 Create migration `backend/src/MoizPos.Migrator/Scripts/0002_audit_entries.sql` for `audit_entries` (entity_type VARCHAR(50), entity_id BIGINT UNSIGNED, field_name VARCHAR(50), old_value VARCHAR(100) NULL, new_value VARCHAR(100) NULL, action VARCHAR(50), user_id FK, occurred_at_utc DATETIME(6) INDEX) per data-model.md §16
- [X] T016 [P] Write failing unit test in `backend/tests/MoizPos.UnitTests/Infrastructure/DbConnectionFactoryTests.cs` asserting the factory returns an open MySQL connection and honours the configured connection string
- [X] T017 Implement `IDbConnectionFactory` and `MySqlConnectionFactory` in `backend/src/MoizPos.Infrastructure/Data/` to make T016 pass
- [X] T018 [P] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Infrastructure/UnitOfWorkTests.cs` asserting that a transaction which throws mid-way commits nothing (Constitution IV)
- [X] T019 Implement `IUnitOfWork` / `UnitOfWork` in `backend/src/MoizPos.Infrastructure/Data/` wrapping `MySqlTransaction` at READ COMMITTED isolation per research.md R4, to make T018 pass
- [X] T020 Configure Dapper decimal handling and a `DECIMAL(12,2)` / `DECIMAL(12,4)` type map in `backend/src/MoizPos.Infrastructure/Data/DapperConfig.cs`; assert in `backend/tests/MoizPos.UnitTests/Infrastructure/DapperConfigTests.cs` that no `double` is used in any money path (research.md R5)

### Time

- [X] T021 [P] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Time/ClockTests.cs` covering `Asia/Karachi` (+05:00) period boundaries: Today, ThisMonth, ThisYear, and a sale at 23:59:59 local landing in exactly one day (FR-034, spec edge case)
- [X] T022 Implement `IClock`, `SystemClock` and `PeriodResolver` in `backend/src/MoizPos.Application/Time/` returning half-open UTC ranges `[startUtc, endUtc)`, to make T021 pass

### API envelope & pagination

- [X] T023 [P] Write failing tests in `backend/tests/MoizPos.UnitTests/Contracts/ApiResponseTests.cs` for the success and error envelope shapes in contracts/conventions.md
- [X] T024 Implement `ApiResponse<T>`, `ApiError`, `ErrorDetail` and `PagedResult<T>` in `backend/src/MoizPos.Application/Contracts/Common/` to make T023 pass
- [X] T025 Implement the global exception middleware in `backend/src/MoizPos.Api/Middleware/ExceptionHandlingMiddleware.cs` mapping domain exceptions to the 12 error codes in contracts/conventions.md, including `traceId`
- [X] T026 [P] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Common/ErrorEnvelopeTests.cs` asserting a validation failure returns 400 with `code: "VALIDATION_FAILED"` and populated `details[]`
- [X] T027 Wire FluentValidation into the pipeline in `backend/src/MoizPos.Api/Program.cs` so failures produce the envelope from T025, making T026 pass

### Auth (serves US6)

- [X] T028 [P] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Auth/PasswordHasherTests.cs` for hash/verify round-trip and rejection of a wrong password
- [X] T029 Implement `IPasswordHasher` / `PasswordHasher` (PBKDF2) in `backend/src/MoizPos.Infrastructure/Auth/` to make T028 pass
- [X] T030 [P] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Auth/TokenServiceTests.cs` asserting the JWT carries `sub`, `role` and a 60-minute expiry, and that refresh tokens are 30-day and rotate on use (research.md R9)
- [X] T031 Implement `ITokenService` / `JwtTokenService` and `RefreshTokenRepository` in `backend/src/MoizPos.Infrastructure/Auth/` to make T030 pass
- [X] T032 [P] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Auth/AuthEndpointsTests.cs` for `POST /api/auth/login` (200 + token pair), wrong password (401), `POST /api/auth/refresh` (rotates), and reuse of a revoked refresh token (401 + chain revoked)
- [X] T033 Implement `AuthController` (`login`, `refresh`, `logout`), `AuthService` and `UserRepository` across `backend/src/MoizPos.Api/Controllers/`, `backend/src/MoizPos.Application/Services/` and `backend/src/MoizPos.Infrastructure/Repositories/` to make T032 pass
- [X] T034 Register the `AdminOnly` authorization policy and role constants in `backend/src/MoizPos.Api/Authorization/`, and add `GET /api/health` checking database connectivity

### Frontend shell

- [X] T035 [P] Write failing component test in `frontend/tests/api/client.test.ts` asserting the Axios client attaches the bearer token, unwraps the success envelope, and surfaces `error.code` on failure
- [X] T036 Implement the Axios client with interceptors in `frontend/src/api/client.ts` to make T035 pass
- [X] T037 [P] Write failing component tests in `frontend/tests/features/auth/LoginForm.test.tsx` for required-field validation, submit, and error display on bad credentials
- [X] T038 Implement `frontend/src/features/auth/LoginForm.tsx`, `frontend/src/features/auth/AuthContext.tsx` (token storage, silent refresh) to make T037 pass
- [X] T039 [P] Write failing test in `frontend/tests/routes/ProtectedRoute.test.tsx` asserting an unauthenticated user is redirected and a Staff user cannot reach an Admin-only route
- [X] T040 Implement `frontend/src/routes/ProtectedRoute.tsx`, `frontend/src/routes/index.tsx` and the role-aware navigation shell in `frontend/src/components/AppShell.tsx` to make T039 pass
- [X] T041 [P] Write failing unit tests in `frontend/tests/lib/money.test.ts` for PKR formatting and 2-decimal rounding with no floating-point drift
- [X] T042 Implement `frontend/src/lib/money.ts` to make T041 pass

**Checkpoint**: Foundation ready. Auth, transactions, money, time, envelope and shell all green.

---

## Phase 3: User Story 3 - Catalogue, stock and purchasing (Priority: P1) 🎯 MVP part 1

**Goal**: A variant-per-row product catalogue with unified search and low-stock flagging, plus
suppliers and purchase recording that raises stock, raises payables, and **overwrites the product
cost** under the owner's latest-cost rule.

**Independent Test**: Create supplier and products, record a purchase, verify stock rose, payable
rose, cost was overwritten, and a below-threshold item appears in the low-stock list.

### Migrations

- [X] T043 Create migration `backend/src/MoizPos.Migrator/Scripts/0003_suppliers.sql` per data-model.md §3 (name VARCHAR(150) NOT NULL INDEX, contact_number VARCHAR(20) NULL, address VARCHAR(255) NULL, payable_balance DECIMAL(12,2) NOT NULL DEFAULT 0, is_active BOOLEAN NOT NULL DEFAULT TRUE)
- [X] T044 Create migration `backend/src/MoizPos.Migrator/Scripts/0004_products.sql` per data-model.md §4 including `cost_price DECIMAL(12,4) NOT NULL`, `sale_price DECIMAL(12,2) NOT NULL`, `quantity_on_hand INT NOT NULL DEFAULT 0` with a `CHECK (quantity_on_hand >= 0)`, `barcode VARCHAR(64) UNIQUE where not null`, `FULLTEXT(name, brand, model, category)`, and a `(quantity_on_hand, min_stock_threshold)` index
- [X] T045 Create migration `backend/src/MoizPos.Migrator/Scripts/0005_stock_movements.sql` per data-model.md §5 (change_qty INT signed, resulting_qty INT, reason ENUM('Purchase','Sale','SaleReturn','PurchaseReturn','Adjustment'), reference_id BIGINT UNSIGNED NULL, user_id FK, note VARCHAR(255) NULL)
- [X] T046 Create migration `backend/src/MoizPos.Migrator/Scripts/0006_purchases.sql` for `purchases` and `supplier_payments` per data-model.md §11–12 (unit_cost DECIMAL(12,4) > 0, quantity INT > 0, total DECIMAL(12,2), returned_qty INT NOT NULL DEFAULT 0, is_overpayment BOOLEAN NOT NULL DEFAULT FALSE)

### Products — tests then implementation

- [X] T047 [P] [US3] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Products/ProductValidatorTests.cs` asserting all prices >= 0, quantity >= 0, min threshold >= 0, name required max 150, category required max 80, barcode max 64 and unique
- [X] T048 [US3] Implement `ProductUpsertValidator` in `backend/src/MoizPos.Application/Validation/ProductUpsertValidator.cs` to make T047 pass
- [X] T049 [P] [US3] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Products/LowStockTests.cs` for the boundary: quantity > threshold is not low, quantity == threshold **is** low, quantity < threshold is low (FR-004)
- [X] T050 [US3] Implement the low-stock rule as a pure function in `backend/src/MoizPos.Application/Calculations/StockRules.cs` to make T049 pass
- [X] T051 [P] [US3] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Products/ProductSearchTests.cs` asserting one `search` term matches by name, brand, model, category **and** barcode (FR-003)
- [X] T052 [US3] Implement `ProductRepository` (Dapper, paged, fulltext + barcode search) in `backend/src/MoizPos.Infrastructure/Repositories/ProductRepository.cs` to make T051 pass
- [X] T053 [US3] Implement `ProductService` in `backend/src/MoizPos.Application/Services/ProductService.cs` — create, update, deactivate (never hard-delete, FR-002), search, low-stock list
- [X] T054 [P] [US3] Write failing unit test in `backend/tests/MoizPos.UnitTests/Products/ProductDtoTests.cs` asserting `ProductStaffDto` has **no** `CostPrice` property at all (FR-040, research.md R10)
- [X] T055 [US3] Implement role-separated `ProductStaffDto` and `ProductAdminDto` in `backend/src/MoizPos.Application/Contracts/Products/` to make T054 pass
- [X] T056 [P] [US3] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Products/ProductEndpointsTests.cs` for GET list/search/paging, POST (Admin 201, Staff 403), PUT, DELETE (deactivates), and GET returning `ProductStaffDto` for a Staff token
- [X] T057 [US3] Implement `ProductsController` in `backend/src/MoizPos.Api/Controllers/ProductsController.cs` per contracts/openapi.yaml to make T056 pass
- [X] T058 [US3] Implement product image upload (2 MB cap, content-type validation, re-encode to JPEG, store path only) in `backend/src/MoizPos.Infrastructure/Storage/ImageStorageService.cs` per research.md R7, with tests in `backend/tests/MoizPos.UnitTests/Storage/ImageStorageServiceTests.cs` written first

### Stock movements

- [X] T059 [P] [US3] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Stock/StockMovementTests.cs` asserting every quantity change writes exactly one append-only movement row whose `resulting_qty` equals the product's new quantity (FR-005, invariant 1)
- [X] T060 [US3] Implement `StockMovementRepository` and `StockService.ApplyMovement` in `backend/src/MoizPos.Infrastructure/Repositories/` and `backend/src/MoizPos.Application/Services/StockService.cs`, transaction-scoped, to make T059 pass

### Suppliers & purchasing — the cost-overwrite rule

- [X] T061 [P] [US3] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Suppliers/SupplierEndpointsTests.cs` for CRUD, ledger view, and 403 for a Staff token on every supplier route
- [X] T062 [US3] Implement `SupplierRepository`, `SupplierService` and `SuppliersController` to make T061 pass
- [X] T063 [P] [US3] **Write the failing cost-rule test first** in `backend/tests/MoizPos.IntegrationTests/Purchases/PurchaseCostRuleTests.cs` encoding the owner's worked example verbatim: buy 10 @ 800 → sell 5 → buy 10 @ 850 → assert `quantity_on_hand == 15` and **`cost_price == 850` exactly (NOT 825, NOT a blend)** (FR-011a, spec US4 scenario 2)
- [X] T064 [P] [US3] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Purchases/RecordPurchaseTests.cs` asserting one transaction performs all six effects from data-model.md §11 (purchase row, stock +qty, cost overwrite, payable +total, stock movement, audit rows), plus a rollback test asserting a forced mid-transaction failure leaves **no** partial state
- [X] T065 [US3] Implement `PurchaseService.RecordPurchase` in `backend/src/MoizPos.Application/Services/PurchaseService.cs` and `PurchaseRepository`, inside one `IUnitOfWork` transaction, to make T063 and T064 pass
- [X] T066 [P] [US3] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Suppliers/SupplierPaymentTests.cs` asserting payable decreases by the amount, and that a payment exceeding the payable is rejected with `OVERPAYMENT_NOT_CONFIRMED` unless `confirmOverpayment: true` (FR-009)
- [X] T067 [US3] Implement `SupplierService.RecordPayment` and the `POST /api/suppliers/{id}/payments` endpoint to make T066 pass
- [X] T068 [US3] Implement `POST /api/purchases` and `GET /api/purchases` in `backend/src/MoizPos.Api/Controllers/PurchasesController.cs`, including the optional `newSalePrice` that repriches all remaining stock (FR-011d)

### Frontend

- [X] T069 [P] [US3] Write failing component tests in `frontend/tests/features/products/ProductForm.test.tsx` for price/quantity validation messages and submit behaviour
- [X] T070 [US3] Implement `frontend/src/features/products/ProductForm.tsx` and `ProductList.tsx` (search box, paging, image upload) to make T069 pass
- [X] T071 [P] [US3] Write failing component test in `frontend/tests/components/LowStockBadge.test.tsx` asserting the badge renders at and below threshold and not above
- [X] T072 [US3] Implement `frontend/src/components/LowStockBadge.tsx` to make T071 pass
- [X] T073 [P] [US3] Write failing component test in `frontend/tests/features/purchases/PurchaseForm.test.tsx` asserting the total equals unit cost × quantity and that changing the cost prompts for a new sale price
- [X] T074 [US3] Implement `frontend/src/features/purchases/PurchaseForm.tsx` and `frontend/src/features/suppliers/SupplierLedger.tsx` to make T073 pass

**Checkpoint**: Catalogue and purchasing work end to end. Quickstart V3 and V4 should pass.

---

## Phase 4: User Story 1 - Counter sale (Priority: P1) 🎯 MVP core

**Goal**: Ring up a multi-line sale with discounts and payment methods; stock decrements
atomically; totals are computed server-side.

**Independent Test**: Ring up a multi-line cash sale, verify totals, saved invoice and reduced
stock; attempt to oversell and verify nothing is written.

**Depends on**: Phase 3 (products and stock must exist to sell).

### Migrations

- [X] T075 Create migration `backend/src/MoizPos.Migrator/Scripts/0007_customers.sql` per data-model.md §6 (name VARCHAR(150) NOT NULL INDEX, mobile_number VARCHAR(20) NULL INDEX, address VARCHAR(255) NULL, outstanding_balance DECIMAL(12,2) NOT NULL DEFAULT 0)
- [X] T076 Create migration `backend/src/MoizPos.Migrator/Scripts/0008_invoices.sql` for `invoices` and `invoice_items` per data-model.md §7–8, including `invoice_items.unit_cost_price DECIMAL(12,4) NOT NULL`, `product_name VARCHAR(150)` snapshot, `returned_qty INT NOT NULL DEFAULT 0`, and `invoices.payment_method ENUM('Cash','BankTransfer','JazzCash','EasyPaisa','Raast','Credit','Partial')`

### Pure calculation logic — the heart of correctness

- [X] T077 [P] [US1] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Calculations/InvoiceCalculatorTests.cs` covering: no discount (10 @ 1,100 × 2 = 2,200); line discount 100 + order discount 400 on subtotal 5,000 = 4,500; zero discount; discount exceeding the line → `DISCOUNT_EXCEEDS_TOTAL`; order discount exceeding subtotal → rejected; partial payment remaining calculation; full credit (paid 0); total never below zero (FR-013, spec US1 scenarios 1–2, edge cases)
- [X] T078 [US1] Implement the pure `InvoiceCalculator` in `backend/src/MoizPos.Application/Calculations/InvoiceCalculator.cs` (no I/O, `decimal` only) to make T077 pass

### Sale transaction

- [X] T079 [P] [US1] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Invoices/InsufficientStockTests.cs` asserting selling 5 of a product with 3 in stock returns 400 `INSUFFICIENT_STOCK` and writes **no** invoice, **no** stock change and **no** stock movement (FR-016, spec US1 scenario 3)
- [X] T080 [P] [US1] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Invoices/CreateInvoiceTransactionTests.cs` asserting the sale writes invoice + items + stock decrements + movements atomically, and that a forced mid-transaction failure leaves none of them (FR-015, FR-050)
- [X] T081 [P] [US1] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Invoices/CostSnapshotTests.cs` asserting `invoice_items.unit_cost_price` captures the product's cost **at sale time**, and that a later purchase changing `products.cost_price` does not alter it (FR-011c, spec US4 scenario 3)
- [X] T082 [P] [US1] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Invoices/ConcurrentSaleTests.cs` asserting two concurrent sales for the last unit result in exactly one success and one failure, with stock never negative (FR-006, spec edge case, research.md R4)
- [X] T083 [P] [US1] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Invoices/CustomerRequiredTests.cs` asserting an unpaid sale with no customer returns 400 `CUSTOMER_REQUIRED` (FR-017)
- [X] T084 [US1] Implement `InvoiceService.CreateInvoice` in `backend/src/MoizPos.Application/Services/InvoiceService.cs` — one transaction: lock product rows with `SELECT ... FOR UPDATE` **ordered by product id** (research.md R4), verify stock, recompute all totals server-side ignoring client values, insert invoice and items with cost snapshot, decrement stock, write movements — making T079–T083 pass
- [X] T085 [US1] Implement `InvoiceRepository` and `CustomerRepository` in `backend/src/MoizPos.Infrastructure/Repositories/`
- [X] T086 [US1] Implement invoice number generation (`INV-{yyyy}-{000000}`, unique, gap-tolerant) in `backend/src/MoizPos.Infrastructure/Numbering/DocumentNumberService.cs` with tests written first
- [X] T087 [P] [US1] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Invoices/IdempotencyTests.cs` asserting a replayed `Idempotency-Key` within 24 hours returns the original invoice and does not double-decrement stock
- [X] T088 [US1] Implement idempotency handling for `POST /api/invoices` to make T087 pass
- [X] T089 [US1] Implement `InvoicesController` (`POST`, `GET` list, `GET {id}`) in `backend/src/MoizPos.Api/Controllers/InvoicesController.cs` per contracts/openapi.yaml
- [X] T090 [US1] Implement `CustomersController` `POST` supporting inline quick-create during a sale (FR-017)

### Frontend POS

- [X] T091 [P] [US1] Write failing unit tests in `frontend/tests/lib/cart.test.ts` mirroring T077's cases against the pure cart functions — line totals, line discount, order discount, remaining, over-discount rejection (Constitution III)
- [X] T092 [US1] Implement pure cart maths in `frontend/src/lib/cart.ts` to make T091 pass
- [X] T093 [P] [US1] Write failing component tests in `frontend/tests/features/pos/PosScreen.test.tsx` for adding items by search and by barcode entry, changing quantity, applying discounts, switching payment method, and the running total/remaining display
- [X] T094 [US1] Implement `frontend/src/features/pos/PosScreen.tsx`, `Cart.tsx`, `ProductSearchInput.tsx` (barcode-as-keyboard-input per spec assumption) and `PaymentPanel.tsx` to make T093 pass
- [X] T095 [P] [US1] Write failing component test in `frontend/tests/features/pos/QuickCreateCustomer.test.tsx` asserting the modal appears when saving an unpaid sale with no customer
- [X] T096 [US1] Implement `frontend/src/features/pos/QuickCreateCustomer.tsx` to make T095 pass

**Checkpoint**: 🎯 **MVP reachable.** The shop can sell for cash and track stock. Quickstart V1 passes.

---

## Phase 5: User Story 2 - Customer ledger / udhaar (Priority: P1)

**Goal**: Credit sales raise a customer balance; a chronological ledger shows a running balance;
payments reduce it.

**Independent Test**: Record a credit sale and a later payment; verify each ledger entry's running
balance and that the profile totals reconcile.

**Depends on**: Phase 4 (a ledger entry originates from an invoice).

### Migration

- [X] T097 Create migration `backend/src/MoizPos.Migrator/Scripts/0009_ledger.sql` for `ledger_entries` (entry_type ENUM('Invoice','Payment','SaleReturn','Adjustment'), bill_amount DECIMAL(12,2) NOT NULL DEFAULT 0, paid_amount DECIMAL(12,2) NOT NULL DEFAULT 0, balance_after DECIMAL(12,2) NOT NULL, index `(customer_id, entry_date_utc, id)`) and `customer_payments` (receipt_number VARCHAR(20) UNIQUE, amount DECIMAL(12,2) > 0, is_overpayment BOOLEAN NOT NULL DEFAULT FALSE) per data-model.md §9–10

### Running balance

- [X] T098 [P] [US2] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Calculations/LedgerBalanceTests.cs` encoding the spec's worked example exactly: opening 0 → bill 3,000 paid 1,000 → balance **2,000**; then payment 1,500 → balance **500**; plus a multi-entry sequence and a payment exceeding the balance (FR-020, spec US2 scenarios 1–2)
- [X] T099 [US2] Implement the pure `LedgerBalanceCalculator` in `backend/src/MoizPos.Application/Calculations/LedgerBalanceCalculator.cs` (`balance_after = previous + bill − paid`) to make T098 pass
- [X] T100 [P] [US2] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Ledger/CreditSaleLedgerTests.cs` asserting a partial-payment sale writes one ledger entry and raises `customers.outstanding_balance` in the same transaction as the invoice (FR-015)
- [X] T101 [US2] Extend `InvoiceService.CreateInvoice` to append the ledger entry and update the customer balance inside the existing transaction, making T100 pass
- [X] T102 [P] [US2] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Ledger/ReceivePaymentTests.cs` for: payment reduces balance and appends an entry; payment exceeding balance without confirmation → 400 `OVERPAYMENT_NOT_CONFIRMED` with balance unchanged; with `confirmOverpayment: true` → accepted and flagged (FR-021, FR-022)
- [X] T103 [US2] Implement `CustomerLedgerService.ReceivePayment` and `LedgerRepository` to make T102 pass
- [X] T104 [P] [US2] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Ledger/LedgerInvariantTests.cs` asserting `customers.outstanding_balance` always equals the latest `ledger_entries.balance_after` (data-model.md invariant 2)
- [X] T105 [US2] Implement `GET /api/customers/{id}/ledger` (paged, chronological) and `GET /api/customers/{id}` returning total purchased, total paid and total outstanding (FR-023)
- [X] T106 [US2] Implement `POST /api/customers/{id}/payments` in `CustomersController` with receipt number generation (`RCP-{yyyy}-{000000}`)

### Frontend

- [X] T107 [P] [US2] Write failing unit tests in `frontend/tests/lib/ledger.test.ts` for running-balance display logic across a sequence of entries
- [X] T108 [US2] Implement `frontend/src/lib/ledger.ts` to make T107 pass
- [X] T109 [P] [US2] Write failing component tests in `frontend/tests/features/customers/CustomerLedger.test.tsx` and `ReceivePaymentModal.test.tsx` for table rendering, running balance column, amount validation, and the overpayment confirmation prompt
- [X] T110 [US2] Implement `frontend/src/features/customers/CustomerDetail.tsx`, `CustomerLedger.tsx` and `ReceivePaymentModal.tsx` to make T109 pass

**Checkpoint**: Udhaar works. Quickstart V2 passes. **This is the recommended first release.**

---

## Phase 6: User Story 5 - Returns (Priority: P2)

**Goal**: Sale and purchase returns that invert the original transaction at the originally
recorded prices, adjusting stock, invoice net, balances and profit.

**Independent Test**: Record a sale, return part of it, verify stock, invoice net, customer
balance and profit all move by exactly the expected amounts.

**Depends on**: Phases 3, 4 and 5.

- [X] T111 Create migration `backend/src/MoizPos.Migrator/Scripts/0010_returns.sql` for `sale_returns`, `sale_return_items` and `purchase_returns` per data-model.md §13–14, including `refund_due DECIMAL(12,2)` and `return_number VARCHAR(20) UNIQUE`
- [X] T112 [P] [US5] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Returns/SaleReturnCreditTests.cs`: return 1 of 2 units from an **unpaid** credit sale → stock +1, `invoice_items.returned_qty` +1, `invoices.net_amount` down by the line value, customer balance down by the same, `SaleReturn` ledger entry appended (FR-024, spec US5 scenario 1)
- [X] T113 [P] [US5] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Returns/SaleReturnPaidTests.cs`: return against a **fully paid** cash sale → stock +1 and `refund_due` recorded rather than silently discarded (FR-028, spec US5 scenario 2)
- [X] T114 [P] [US5] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Returns/ReturnCostBasisTests.cs` asserting the return reverses at the **originally recorded** `unit_sale_price` and `unit_cost_price`, not the product's current figures, even after a purchase has changed the cost (research.md R11)
- [X] T115 [P] [US5] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Returns/ReturnLimitTests.cs` asserting returning more than was sold, or returning twice past the original quantity, returns 400 `RETURN_EXCEEDS_ORIGINAL` (FR-026, spec edge case)
- [X] T116 [US5] Implement `SaleReturnService.RecordSaleReturn` in `backend/src/MoizPos.Application/Services/SaleReturnService.cs` — one transaction inverting the sale — to make T112–T115 pass
- [X] T117 [P] [US5] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Returns/PurchaseReturnTests.cs`: return 10 of 50 purchased → stock −10, supplier payable −(10 × original unit cost), `purchases.returned_qty` +10; returning more than purchased → 400; stock guarded against going negative; **`products.cost_price` is NOT reverted** (FR-025, research.md R11)
- [X] T118 [US5] Implement `PurchaseReturnService.RecordPurchaseReturn` to make T117 pass
- [X] T119 [US5] Implement `SaleReturnsController` and `PurchaseReturnsController` per contracts/openapi.yaml
- [X] T120 [P] [US5] Write failing component tests in `frontend/tests/features/returns/SaleReturnForm.test.tsx` asserting returnable quantity is capped at `quantity − returnedQty` per line
- [X] T121 [US5] Implement `frontend/src/features/returns/SaleReturnForm.tsx` and `PurchaseReturnForm.tsx` to make T120 pass

**Checkpoint**: Corrections work without breaking the books. Quickstart V5 passes.

---

## Phase 7: User Story 4 - Expenses, profit engine, dashboard and reports (Priority: P2)

**Goal**: Turn the recorded ledger into gross and net profit, KPIs and the full report set.

**Independent Test**: With known sales, purchases and expenses in place, verify every dashboard
and report figure against hand calculation.

**Depends on**: Phases 3–6 (reports aggregate over all of them). The expenses sub-module
(T122–T126) depends only on Foundational and **may be built in parallel** with Phases 4–6.

### Expenses

- [X] T122 Create migration `backend/src/MoizPos.Migrator/Scripts/0011_expenses.sql` for `expense_categories` (name VARCHAR(80) UNIQUE, seeded with Rent, Electricity, Internet, Transport, Salary, Other) and `expenses` (amount DECIMAL(12,2) > 0, expense_date_utc INDEX, note VARCHAR(255) NULL) per data-model.md §15
- [X] T123 [P] [US4] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Expenses/ExpenseValidatorTests.cs` asserting amount > 0, category required and existing, date required
- [X] T124 [US4] Implement `ExpenseValidator` and `ExpenseService` to make T123 pass
- [X] T125 [P] [US4] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Expenses/ExpenseSumByPeriodTests.cs` asserting the period-sum query respects `Asia/Karachi` boundaries and excludes expenses outside the range
- [X] T126 [US4] Implement `ExpenseRepository.SumByPeriod` and `ExpensesController` (Admin only) to make T125 pass

### Profit engine

- [X] T127 [P] [US4] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Calculations/ProfitCalculatorTests.cs` encoding the spec's worked example: sale 1,100, cost 800 → gross profit **300** per unit; with a line discount; with a partial return excluded; zero-quantity edge case (FR-031, FR-027, spec US4 scenario 1)
- [X] T128 [US4] Implement the pure `ProfitCalculator` in `backend/src/MoizPos.Application/Calculations/ProfitCalculator.cs` using `(unit_sale_price − unit_cost_price) × (quantity − returned_qty) − line_discount` to make T127 pass
- [X] T129 [P] [US4] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Calculations/NetProfitTests.cs`: gross 50,000 − expenses 12,000 = net **38,000**; day/week/month/year boundaries; a transaction at 23:59 Karachi landing in exactly one bucket (FR-032, FR-034, spec US4 scenario 2)
- [X] T130 [US4] Implement `ProfitReportService` in `backend/src/MoizPos.Application/Services/ProfitReportService.cs` to make T129 pass
- [X] T131 [P] [US4] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Reports/ProductProfitTests.cs` asserting product-wise totals (total sale, total cost, total profit) reconcile to the sum of their invoice lines (FR-033)
- [X] T132 [US4] Implement the read-optimized Dapper aggregation queries in `backend/src/MoizPos.Infrastructure/Repositories/ReportRepository.cs` — **read-only, no new write paths** — to make T131 pass

### Dashboard & reports

- [X] T133 [P] [US4] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Reports/DashboardTests.cs` asserting all 12 KPIs from data-model.md `DashboardDto` recalculate correctly for Today, ThisMonth and ThisYear (FR-035)
- [X] T134 [US4] Implement `DashboardService` and `DashboardController` to make T133 pass
- [X] T135 [P] [US4] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Reports/ReportEndpointsTests.cs` covering all ten report endpoints in contracts/openapi.yaml, each asserting Staff receives 403
- [X] T136 [US4] Implement `ReportsController` with sales, profit, profit-by-product, stock, stock-movements, receivables, payables, expenses and purchases endpoints to make T135 pass

### Frontend

- [X] T137 [P] [US4] Write failing component tests in `frontend/tests/features/dashboard/Dashboard.test.tsx` for KPI card rendering and period-toggle refetch behaviour
- [X] T138 [US4] Implement `frontend/src/features/dashboard/Dashboard.tsx`, `KpiCard.tsx` and `PeriodToggle.tsx` to make T137 pass
- [X] T139 [P] [US4] Write failing component test in `frontend/tests/features/reports/ReportFilters.test.tsx` for date-range filtering and validation that `from <= to`
- [X] T140 [US4] Implement the report screens in `frontend/src/features/reports/` with export-friendly tables to make T139 pass
- [X] T141 [US4] Implement `frontend/src/features/expenses/ExpenseForm.tsx` with tests in `frontend/tests/features/expenses/ExpenseForm.test.tsx` written first

**Checkpoint**: The owner can see whether the shop made money. Quickstart V6 passes.

---

## Phase 8: User Story 6 - Role enforcement (Priority: P2)

**Goal**: Prove Staff cannot reach cost or profit data by **any** route, not merely in the UI.

**Independent Test**: Sign in as each role and attempt restricted areas both through the UI and
by direct API call.

**Depends on**: All endpoints existing (Phases 3–7). Auth itself was built in Foundational.

- [X] T142 [P] [US6] Write failing architecture test in `backend/tests/MoizPos.ArchitectureTests/StaffDtoExposureTests.cs` asserting no DTO type reachable from a Staff-accessible endpoint declares any property whose name contains `Cost`, `Profit`, `Margin` or `Purchase` price (research.md R10)
- [X] T143 [P] [US6] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Authorization/StaffForbiddenTests.cs` asserting a Staff JWT receives **403** from every route in the Admin-only column of contracts/conventions.md — dashboard, all `/reports/*`, purchases, suppliers, expenses, all `/admin/*`, and product write routes (FR-040)
- [X] T144 [P] [US6] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Authorization/StaffProductPayloadTests.cs` asserting `GET /api/products/{id}` with a Staff token returns 200 whose JSON contains **no `costPrice` key at all** — absent, not null (quickstart V7)
- [X] T145 [P] [US6] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Authorization/StaffPermittedTests.cs` asserting a Staff user **can** create a sale, search products and customers, and receive a customer payment (spec US6 scenario 2)
- [X] T146 [P] [US6] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Authorization/UnauthenticatedTests.cs` asserting every business endpoint returns 401 without a token, and that `GET /api/health` and `GET /api/public/documents/{token}` are the only exceptions
- [X] T147 [US6] Apply `[Authorize(Policy = "AdminOnly")]` per-endpoint (not per-controller) across all controllers and correct any DTO leak, making T142–T146 pass
- [X] T148 [US6] Implement role-aware navigation hiding in `frontend/src/components/AppShell.tsx` as a usability layer only — with a test in `frontend/tests/components/AppShell.test.tsx` asserting Staff sees no reports/purchases/expenses nav items

**Checkpoint**: Cost confidentiality is enforced where data is served. Quickstart V7 passes.

---

## Phase 9: User Story 7 - PDF documents and WhatsApp delivery (Priority: P3)

**Goal**: A branded PDF for every invoice and payment receipt, shareable to the customer's
WhatsApp.

**Independent Test**: Generate both document types for a customer with a stored number and verify
the share action opens addressed to that number carrying the correct document link.

**Depends on**: Phases 4 and 5. **Can be built in parallel with Phases 6–8.**

- [X] T149 Create migration `backend/src/MoizPos.Migrator/Scripts/0012_document_tokens.sql` per data-model.md §17 (token_hash CHAR(64) UNIQUE, document_type ENUM('Invoice','PaymentReceipt'), reference_id, expires_at_utc, revoked_at_utc NULL, last_accessed_utc NULL, access_count INT NOT NULL DEFAULT 0)
- [X] T150 [P] [US7] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Documents/InvoiceDocumentModelTests.cs` asserting the assembled document model contains every field required by FR-042 — shop name "Moiz Mobile & Corporation, Danwran Lodhran", contact details, invoice number, date/time, customer name and mobile, each line's product/qty/rate/discount, subtotal, total, paid, remaining, thank-you footer — and that its totals equal the invoice's
- [X] T151 [US7] Implement `InvoiceDocumentBuilder` in `backend/src/MoizPos.Infrastructure/Documents/` to make T150 pass
- [X] T152 [P] [US7] Write failing unit test in `backend/tests/MoizPos.UnitTests/Documents/ReceiptDocumentModelTests.cs` for the payment receipt requiring receipt number, amount paid and remaining balance (FR-043)
- [X] T153 [US7] Implement `ReceiptDocumentBuilder` to make T152 pass
- [X] T154 [US7] Implement QuestPDF rendering in `backend/src/MoizPos.Infrastructure/Documents/PdfRenderer.cs` with a multi-page line-item table, and integration tests asserting a non-empty `application/pdf` stream for both document types
- [X] T155 [P] [US7] Write failing unit tests in `backend/tests/MoizPos.UnitTests/Documents/WhatsAppLinkBuilderTests.cs` asserting: a Pakistani mobile is normalised to `92XXXXXXXXXX` (leading `0` dropped, `+`/spaces/dashes stripped); the URL is `https://wa.me/<number>?text=<url-encoded message containing the share link>`; a null or malformed number returns a failure result rather than a broken URL (FR-044, research.md R2)
- [X] T156 [US7] Implement `WhatsAppLinkBuilder` in `backend/src/MoizPos.Application/Documents/WhatsAppLinkBuilder.cs` to make T155 pass
- [X] T157 [P] [US7] Write failing integration tests in `backend/tests/MoizPos.IntegrationTests/Documents/DocumentTokenTests.cs` asserting: a minted token is ≥128 bits and stored only as a hash; `GET /api/public/documents/{token}` returns the PDF without authentication; an expired token, a revoked token and an unknown token all return an **identical** 404; the endpoint is rate-limited; each access increments `access_count`
- [X] T158 [US7] Implement `DocumentTokenService` and the `GET /api/public/documents/{token}` endpoint to make T157 pass — the single justified unauthenticated endpoint per plan.md Complexity Tracking
- [X] T159 [US7] Implement `POST /api/documents/share-link` returning `shareUrl`, `whatsAppUrl` and `expiresAt`, returning 400 when the customer has no mobile number on file
- [X] T160 [P] [US7] Write failing component tests in `frontend/tests/features/documents/ShareButtons.test.tsx` asserting Download PDF and Send on WhatsApp click handlers, and that the WhatsApp button is **disabled with a stated reason** when no phone number is on file (spec US7 scenario 2)
- [X] T161 [US7] Implement `frontend/src/features/documents/ShareButtons.tsx` and mount it on the invoice and ledger-payment screens to make T160 pass

**Checkpoint**: Receipts reach the customer's phone. Quickstart V9 passes.

---

## Phase 10: User Story 8 - Audit trail, backup and restore (Priority: P3)

**Goal**: Every stock and balance change is attributable, and the shop's records survive a
hardware failure.

**Independent Test**: Trigger a manual backup, restore it into a clean database and verify all
records; change stock and a balance and verify the audit entries.

**Depends on**: Phases 3–6 (the transactional services being retrofitted must exist).

- [X] T162 [P] [US8] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Audit/AuditWriteTests.cs` asserting every stock change and every balance change writes an audit row capturing user, field, old value, new value and timestamp (FR-041)
- [X] T163 [P] [US8] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Audit/AuditRollbackTests.cs` asserting a rolled-back mutation leaves **no** audit row claiming the change happened (data-model.md invariant 5)
- [X] T164 [US8] Retrofit `IAuditWriter` calls into the existing `PurchaseService`, `InvoiceService`, `CustomerLedgerService`, `SaleReturnService`, `PurchaseReturnService` and `StockService` **inside their existing transactions** — a retrofit, not a rewrite — making T162–T163 pass
- [X] T165 [US8] Implement `GET /api/admin/audit` (paged, filterable by entity, user and date range) in `backend/src/MoizPos.Api/Controllers/AdminController.cs`
- [X] T166 [P] [US8] Write failing unit test in `backend/tests/MoizPos.UnitTests/Backup/BackupSchedulerTests.cs` using a **mockable `IClock`** asserting the backup job triggers once per day and not more often (research.md R8)
- [X] T167 [US8] Implement `BackupHostedService` in `backend/src/MoizPos.Infrastructure/Backup/` invoking `mysqldump` to a timestamped compressed file with 30-day retention, to make T166 pass
- [X] T168 [P] [US8] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Backup/RestoreTests.cs` asserting a backup restored into a **throwaway test database** reproduces matching row counts for products, invoices, invoice_items, customers, ledger_entries, purchases and expenses (FR-047, SC-012)
- [X] T169 [US8] Implement `BackupService.CreateBackup` / `RestoreBackup` and the `POST /api/admin/backups`, `GET /api/admin/backups`, `POST /api/admin/backups/restore` endpoints to make T168 pass
- [X] T170 [P] [US8] Write failing integration test in `backend/tests/MoizPos.IntegrationTests/Backup/BackupAuthorizationTests.cs` asserting a Staff token receives 403 on every backup and restore route (FR-046, spec US8 scenario 3)
- [X] T171 [US8] Implement `frontend/src/features/admin/BackupPanel.tsx` (Admin-only, with a destructive-action confirmation on restore) and `AuditLogViewer.tsx`, with component tests written first
- [X] T172 [US8] Implement the corrupt-backup path: restore validates the dump before applying and fails safely without touching current data, with a test asserting current data survives a corrupt file (spec edge case)

**Checkpoint**: Records are attributable and recoverable. Quickstart V10 and V11 pass.

---

## Phase 11: Polish & Cross-Cutting Concerns

- [X] T173 [P] Write and verify the cross-cutting invariant tests in `backend/tests/MoizPos.IntegrationTests/Invariants/DataInvariantTests.cs` for all six invariants in data-model.md (stock vs latest movement, balance vs latest ledger entry, payable vs purchases−payments−returns, non-negative stock, no partial state, immutable `unit_cost_price`)
- [X] T174 [P] Add a performance test in `backend/tests/MoizPos.IntegrationTests/Performance/ProductSearchPerformanceTests.cs` seeding 5,000 products and asserting search returns in under 2 seconds (SC-002)
- [X] T175 [P] Verify dashboard response time against SC-008 (under 10 seconds) with a seeded year of data, and add covering indexes if it fails
- [X] T176 [P] Add `frontend/src/components/ErrorBoundary.tsx` and consistent error-toast handling driven by the envelope's `error.code`
- [X] T177 [P] Add responsive layout verification for desktop and tablet widths across POS, product list and dashboard
- [X] T178 Seed script for first-run: one Admin user, the six expense categories, and a note in `README.md` that the default password **must** be changed before the shop uses the system
- [X] T179 [P] Create `CLAUDE.md` at repository root documenting the layering rules, the latest-cost rule, and the transactional invariants — this resolves `TODO(GUIDANCE_FILE)` in `.specify/memory/constitution.md`
- [X] T180 Update `.specify/memory/constitution.md` Governance section to reference `CLAUDE.md`, removing the TODO
- [X] T181 [P] Document deployment and the backup directory placement in `docs/deployment.md`, including the 24-hour data-loss window from research.md R8 stated plainly for the owner
- [X] T182 Run the full `quickstart.md` validation V1–V11 end to end against a fresh database and record results
- [X] T183 Run `dotnet test` and `npm run test` and confirm all suites green with no previously passing test broken (Constitution VI)

---

## Dependencies & Execution Order

### Phase dependencies

```
Phase 1 Setup
      ↓
Phase 2 Foundational  ← BLOCKS EVERYTHING
      ↓
Phase 3 US3 Catalogue & Purchasing (P1)
      ↓
Phase 4 US1 Counter Sale (P1)  ────────────┐
      ↓                                     │
Phase 5 US2 Customer Ledger (P1)  ← MVP ────┤
      ↓                                     │
      ├── Phase 6 US5 Returns (P2)          │
      ├── Phase 7 US4 Reports (P2) ←────────┘  (expenses sub-module can start after Phase 2)
      ├── Phase 9 US7 Documents (P3)  [parallel-safe]
      └── Phase 10 US8 Backup & Audit (P3)  [needs Phases 3–6 to retrofit audit]
                    ↓
            Phase 8 US6 Role Enforcement (P2)  ← needs all endpoints to exist
                    ↓
            Phase 11 Polish
```

### User story dependencies — the honest version

| Story | Priority | Depends on | Why |
|---|---|---|---|
| US3 Catalogue & purchasing | P1 | Foundational | Nothing can be sold that cannot be stocked |
| US1 Counter sale | P1 | US3 | Sells products, decrements their stock |
| US2 Customer ledger | P1 | US1 | A ledger entry originates from an invoice |
| US5 Returns | P2 | US1, US2, US3 | Inverts sales and purchases |
| US4 Reports & profit | P2 | US1, US3, US5, expenses | Aggregates over all write paths |
| US6 Role enforcement | P2 | All endpoints | Cannot test 403 on a route that does not exist |
| US7 Documents | P3 | US1, US2 | Renders invoices and receipts |
| US8 Backup & audit | P3 | US3–US5 | Retrofits audit into existing transactions |

**US6 is deliberately late.** Its auth foundation is built in Phase 2, but the requirement it
tests — "Staff cannot reach cost or profit by any route" — is only meaningfully verifiable once
every route exists. Role attributes are applied as each controller is written; Phase 8 is the
systematic audit that none were missed.

### Within each story

- The failing test task always precedes its implementation task
- Migrations → repositories → services → controllers → frontend
- Pure calculators before the services that use them

### Parallel opportunities

- **Phase 1**: T004–T010, T012 all parallel
- **Phase 2**: T016/T018/T021/T023/T026/T028/T030/T032 (test tasks) parallel; T035/T037/T039/T041 parallel
- **Phase 3**: T047/T049/T051/T054/T056/T059/T061/T063/T064/T066 parallel; frontend T069/T071/T073 parallel
- **Phase 4**: T079–T083 and T087 all parallel; T091/T093/T095 parallel
- **Phase 6**: T112–T115 and T117 all parallel
- **Phase 7**: T123/T125/T127/T129/T131/T133/T135 parallel
- **Phase 8**: T142–T146 all parallel
- **Phase 9**: T150/T152/T155/T157/T160 parallel
- **Across phases**: Phase 9 (documents) can run alongside Phases 6–8 with a second developer; the
  expenses sub-module (T122–T126) can start any time after Phase 2

### Parallel example: User Story 1 test batch

```bash
# All five sale-transaction tests are independent files — launch together:
Task: "Insufficient stock test in backend/tests/MoizPos.IntegrationTests/Invoices/InsufficientStockTests.cs"
Task: "Transaction atomicity test in backend/tests/MoizPos.IntegrationTests/Invoices/CreateInvoiceTransactionTests.cs"
Task: "Cost snapshot test in backend/tests/MoizPos.IntegrationTests/Invoices/CostSnapshotTests.cs"
Task: "Concurrent sale test in backend/tests/MoizPos.IntegrationTests/Invoices/ConcurrentSaleTests.cs"
Task: "Customer required test in backend/tests/MoizPos.IntegrationTests/Invoices/CustomerRequiredTests.cs"
```

---

## Implementation Strategy

### MVP scope — recommended first release

**Phases 1, 2, 3, 4 and 5** (T001–T110). This delivers: sign-in with roles, a searchable
variant catalogue with low-stock alerts, supplier purchasing with the latest-cost rule, a working
POS with discounts and all payment methods, and the udhaar ledger.

That is a shop that can trade. It is deliberately larger than "User Story 1 only" because
US1 alone cannot function — a sale needs products (US3) — and a mobile-accessories shop that
cannot track udhaar (US2) will not adopt the system at all. All three P1 stories ship together.

**Stop and validate**: run quickstart V1–V4 before proceeding.

### Incremental delivery after MVP

1. **+ Phase 6 (Returns)** → the books stay true when goods come back
2. **+ Phase 7 (Reports)** → the owner sees profit
3. **+ Phase 8 (Role enforcement)** → safe to hand a login to a salesman
4. **+ Phase 9 (Documents)** → customers get receipts on WhatsApp
5. **+ Phase 10 (Backup & audit)** → records are safe and attributable
6. **+ Phase 11 (Polish)**

**Do not hand a Staff login to an employee before Phase 8 is green.** Until then, cost
confidentiality is asserted only by UI hiding, which the constitution explicitly rejects as
insufficient.

### Parallel team strategy

With two developers, after Phase 5:

- Developer A: Phase 6 (Returns) → Phase 7 (Reports)
- Developer B: Phase 9 (Documents) → Phase 10 (Backup & audit)
- Both converge on Phase 8 (Role enforcement), which needs all routes present

---

## Notes

- **[P]** = different files, no dependency on an incomplete task
- Every implementation task has a preceding test task — this is Constitution Principle I, not a
  style preference
- **Verify each test fails before implementing it.** A test that passes before the code exists is
  testing nothing
- The three tests that most protect the shop's money are T063 (cost overwrite = 850, not 825),
  T082 (concurrent sale cannot oversell) and T098 (ledger running balance) — treat a failure in
  any of them as a release blocker
- Commit after each task or logical pair
- A phase is done only when its unit tests pass, its integration tests pass, and no previously
  green test broke (Constitution Principle VI)
