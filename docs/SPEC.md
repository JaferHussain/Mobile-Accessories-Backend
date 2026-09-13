# Moiz Mobile & Corporation — POS, Inventory & Ledger System
## Consolidated Specification + Spec-Kit Implementation Plan (v2 — includes Shop Expenses & Salesman modules)

**Stack:** React (TypeScript) · ASP.NET Core 8 Web API · MySQL 8 + Dapper · xUnit / Vitest (TDD mandatory)

> v2 changes: module 7 (Expenses) is expanded into a full **Shop Expense Management** module,
> and a new **Salesman Management** module (module 14) is added. Build order and the
> dependency table are updated accordingly. Everything else is unchanged from v1.

---

## PART 1 — Master Prompt (canonical brief)

```
Build a full-stack Point-of-Sale, Inventory, and Customer Ledger Management
System for "Moiz Mobile & Corporation, Danwran Lodhran," a mobile accessories
retail shop that sells items such as chargers, cables, earbuds, covers, and
similar variant-heavy products.

TECH STACK
- Frontend: React (TypeScript), component-driven, REST client (Axios/Fetch),
  React Router, form validation, responsive (desktop + tablet POS use).
- Backend: ASP.NET Core 8 Web API, clean/layered architecture
  (Controllers -> Services -> Repositories), Dapper for data access (no EF Core),
  FluentValidation for input validation, JWT-based auth.
- Database: MySQL 8, normalized schema, migrations via a version-controlled
  SQL migration tool (e.g., DbUp or Flyway).
- Testing: xUnit + Moq for backend unit/integration tests; Vitest + React
  Testing Library for frontend unit tests. Test-Driven Development is
  mandatory — tests are written before or alongside implementation for every
  module, and no module is considered "done" without passing unit tests
  covering its business rules (stock math, profit math, balance math,
  discount math, commission math, permission checks).

CORE DOMAIN MODULES

1. Dashboard
   - Today / This Month / This Year toggle.
   - KPIs: total sale, total purchase, gross profit, expenses, net profit,
     cash sale amount, credit (udhaar) sale amount, total customer
     receivables outstanding, total supplier payables outstanding, count of
     items sold today, low-stock product list, global search bar.
   - Additional KPIs (v2): expense total by category for the period, and
     top-performing salesman for the period (sales value + commission).

2. Product & Variant Management
   - Fields: name, category, brand, model, product image, barcode/SKU,
     purchase price, wholesale price, retail price, sale price, quantity on
     hand, minimum stock threshold, supplier reference.
   - Each distinct variant of an item (e.g., 5 types of USB cable) is stored
     as its own product record with its own image, price, quantity, and
     profit — not as a shared "options" field.
   - Low-stock alert flag/badge when quantity <= minimum threshold.
   - Search by name, brand, model, category, or barcode via one search box.

3. Purchases & Suppliers
   - Supplier record: name, contact, address, running payable balance.
   - Purchase entry: supplier, product, purchase price, quantity, date,
     computed total. Saving a purchase auto-increments product stock and
     increases the supplier's payable balance.
   - Supplier payment tracking and purchase-payment history.

4. Sales / Invoice / POS
   - Product lookup by search or barcode scan.
   - Line items: quantity, unit sale price, per-line discount.
   - Order-level discount option.
   - Auto-calculated subtotal, discount, total, paid amount, remaining balance.
   - Payment methods: Cash, Bank Transfer, JazzCash, EasyPaisa, Raast,
     Credit/Udhaar, and Partial Payment (split cash + credit).
   - Every invoice records the salesman who made the sale (see module 14);
     for a Staff-role login this defaults to the logged-in user and is not
     editable, for Admin it is selectable.
   - On save: stock auto-decrements per line item; if customer is
     unregistered, prompt to quick-create a customer record; if payment is
     partial/credit, the customer ledger balance auto-increases.
   - Generates a PDF invoice/receipt and offers "Send on WhatsApp."

5. Customer Management & Ledger (Udhaar)
   - Customer record: name, mobile number, address (optional).
   - Ledger view per customer: chronological entries of bill amount, paid
     amount, and running balance (each new entry's balance = previous
     balance + new bill - new payment).
   - "Receive Payment" action reduces balance and generates a payment
     receipt (PDF + WhatsApp-shareable).
   - Customer profile shows total purchases, total paid, total outstanding,
     and full invoice/payment history.

6. Returns
   - Sale Return: reduces the original invoice's net amount, increases
     stock back, and adjusts the customer's ledger balance accordingly.
   - Purchase Return: reduces stock, adjusts the supplier's payable balance.
   - Both must recompute profit for the affected period, and reverse any
     commission already accrued to the salesman on the returned lines.

7. Shop Expense Management  [EXPANDED IN v2]
   - Expense categories are a managed lookup table (not a hard-coded enum):
     seeded with Rent, Electricity, Internet, Transport, Salary, Tea/Refreshment,
     Repair & Maintenance, Packaging, Marketing, Bank/Transfer Charges, Other.
     Admin can add, rename, activate/deactivate a category. A category that is
     already referenced by an expense can be deactivated but never deleted.
   - Expense record: category, amount, expense date, payment method
     (Cash / Bank / JazzCash / EasyPaisa / Raast), paid-to / vendor name
     (optional free text), reference or bill number (optional),
     attachment/receipt image (optional), note, created-by user, created-at.
   - Recurring expenses: an expense can be marked recurring (Monthly or
     Weekly) with a start date and optional end date. A scheduled job
     materialises the next occurrence as a normal draft expense row on its
     due date; the Admin confirms it. Recurring definitions are edited or
     stopped without touching already-posted expenses.
   - Salary expenses are linked to a salesman/user record (module 14) so
     salary paid per employee is reportable; a salary expense posted from
     the Salesman module writes exactly one row in `expenses` with
     category = Salary and `salesman_id` set — there is no second source of
     truth for money leaving the shop.
   - Monthly expense budget (optional) per category; the expense report
     flags categories over budget for the period.
   - All expenses subtract from gross profit to produce net profit in every
     profit report (daily / weekly / monthly / annual).
   - Expense listing: date-range filter, category filter, payment-method
     filter, text search on vendor/note/reference, paginated, with a
     period total and a per-category breakdown.
   - Expenses are Admin-only: Staff can neither create nor read expenses.
   - Every expense create / edit / delete writes an audit-log entry
     (who, old value, new value, when). Deleting an expense is a soft delete
     so historical profit reports remain explainable.

8. Profit Engine
   - Gross profit per sale line = (sale price - purchase/cost price) *
     quantity - line discount.
   - Net profit (period) = sum(gross profit) - sum(expenses) - sum(salesman
     commission accrued for the period, where commission is not already
     booked as a Salary expense) for that period.
   - Must support Daily, Weekly, Monthly, and Annual rollups, plus
     product-wise profit breakdown and salesman-wise profit contribution.
   - Purchase price must always be persisted per product/purchase batch so
     profit is never estimated — it is always computed from actual cost.
   - Commission must be counted exactly once: either as an accrual line in
     the profit engine or as a posted Salary expense, never both. The
     service enforces this by marking a commission row `settled` with the
     `expense_id` that paid it.

9. Reports
   - Daily sales report, monthly sales report, annual sales report.
   - Total purchases, total sales, total profit (gross & net).
   - Product-wise profit report.
   - Current stock report, low-stock report, full stock movement history
     (in/out with reasons: purchase, sale, return, adjustment).
   - Customer outstanding (receivables) report.
   - Supplier payable report.
   - Expense report: by period, by category, by payment method, with
     budget-vs-actual and a recurring-expense schedule view.
   - Salesman performance report: per salesman — number of invoices,
     total sale value, total gross profit generated, discount given,
     returns attributed, commission earned, commission paid, commission
     outstanding, target vs achieved.

10. Users, Roles & Permissions
    - Admin: full access to all modules, including cost prices, expenses,
      salaries, commissions and profit reports.
    - Staff (salesman): can create sales, view/search products and
      customers, and view only their own performance summary (their sales
      count/value and their own commission) — but cannot view purchase
      price, profit figures, expenses, other salesmen's figures, or any
      financial report.
    - Authentication via JWT; role-based authorization enforced server-side
      on every endpoint, not just hidden in the UI.

11. PDF Invoice / Receipt
    - Branding: "Moiz Mobile & Corporation, Danwran Lodhran" header/logo and
      contact details.
    - Contents: invoice number, date/time, customer name & mobile, salesman
      name, line items (product, qty, rate, discount), subtotal, total,
      paid, balance remaining, and a thank-you footer.
    - Generated server-side as a downloadable/printable PDF for every sale,
      every ledger payment receipt, and every salary/commission payslip.

12. WhatsApp Integration
    - After an invoice, payment receipt, or payslip is generated, a
      "Send on WhatsApp" action opens WhatsApp (wa.me deep link or WhatsApp
      Business API) pre-attached/linked with the PDF or a share link,
      addressed to the stored mobile number.

13. Backup & Restore
    - Automated daily backup of the MySQL database.
    - Manual "Backup Now" and "Restore from Backup" — Admin only.

14. Salesman Management  [NEW IN v2]
    - A salesman is an employee record linked optionally to a login user
      (a salesman may exist without system access; a Staff user must map to
      exactly one salesman record).
    - Salesman record: name, mobile, CNIC (optional), address (optional),
      photo (optional), joining date, leaving date (optional), status
      (Active / Inactive), monthly base salary, commission scheme,
      created-by, created-at.
    - Commission scheme (per salesman, effective-dated so history is
      preserved):
        * None
        * Percent of sale value  — commission = net line total * rate%
        * Percent of gross profit — commission = line gross profit * rate%
        * Fixed amount per invoice
      Rate/amount and effective-from date are stored; a sale always uses the
      scheme effective on the invoice date, never the current scheme.
    - Commission accrues per invoice line at the moment the invoice is
      saved, inside the same DB transaction as the sale, and is stored in a
      `salesman_commissions` table (invoice_id, invoice_item_id,
      salesman_id, basis, rate, amount, status = Accrued | Settled | Reversed).
    - A sale return reverses the commission for the returned quantity
      (a Reversed row, never an edit of the original row).
    - Monthly targets: optional sale-value target per salesman per month;
      the performance report shows achieved vs target and percentage.
    - Attendance is out of scope; salary is paid as a period amount.
    - Salary & commission payout: an Admin "Pay Salesman" action for a
      chosen period creates one Salary expense (module 7) for the amount
      paid, marks the included commission rows Settled with that expense id,
      and produces a payslip PDF (base salary + commission + adjustments =
      net paid) that is WhatsApp-shareable.
    - Salesman ledger: per-salesman running view of earnings (base salary
      accrued, commission accrued) versus payouts, with an outstanding
      payable figure. Shop payable to salesmen is reported alongside
      supplier payables.
    - Deactivating a salesman blocks new sales attribution but preserves all
      history and any outstanding payable.
    - Permissions: full CRUD, schemes, targets and payouts are Admin-only.
      A Staff user may read only their own salesman record's performance
      summary and own commission total.

NON-FUNCTIONAL REQUIREMENTS
- All monetary calculations happen server-side and are re-validated on
  every write (never trust client-computed totals).
- All stock, ledger, commission and expense mutations must be atomic (DB
  transactions) so a crash mid-sale can never leave stock/balances/
  commissions inconsistent.
- Audit trail: every stock, ledger, expense and commission change records
  who, what, when (user id, old value, new value, timestamp).
- API responses paginated for list endpoints (products, customers,
  invoices, stock history, expenses, commissions).
- Input validation and error handling return structured, consistent error
  responses.
- Money is stored as DECIMAL(12,2); percentage rates as DECIMAL(5,2).
  Rounding is half-up to 2 decimals, applied once at the line level.
```

---

## PART 2 — Build Order Rationale

Sale, stock, ledger, commission and profit math all depend on each other, so:

**Foundation → Catalog → Purchasing → Salesman (identity + scheme) → Selling → Customer Ledger → Returns → Shop Expenses → Salary & Commission Payout → Insight (Profit/Reports/Dashboard) → Delivery (PDF/WhatsApp) → Operations (Roles/Backup/Audit)**

Two ordering decisions matter in v2:

1. **Salesman identity comes before POS.** An invoice must carry `salesman_id` and accrue commission inside the sale transaction. Retrofitting that column after the POS phase would mean rewriting the most heavily tested service in the system, which violates constitution principle 6 (don't break previously green tests).
2. **Shop Expenses comes before Salary Payout.** A salary payout *is* an expense row; the expense module must exist and be tested first so there is a single source of truth for cash leaving the shop.

---

## PART 3 — Spec-Kit Prompt Sequence

> Per phase: `/specify` → `/clarify` → `/plan` → `/tasks` → `/implement`.
> `/tasks` must always emit a failing-test task before its implementation task.

### Step 0 — Project Constitution (run once)

```
/constitution
Establish the engineering constitution for the Moiz Mobile POS project.
Principles:
1. Test-Driven Development is mandatory: for every user story, a failing
   unit test (xUnit for backend, Vitest for frontend) must exist before
   the implementation that makes it pass. No task is "complete" without
   a green test suite.
2. Backend: ASP.NET Core 8 Web API, layered architecture
   (Controller -> Service -> Repository), Dapper only (no ORM/EF Core),
   MySQL 8 as the database, FluentValidation for request validation,
   JWT auth with role-based authorization on every endpoint.
3. Frontend: React + TypeScript, component tests for every stateful
   component that contains business logic (totals, discounts, balances,
   commissions).
4. All monetary/stock/ledger/commission/expense mutations are wrapped in DB
   transactions and are re-validated server-side regardless of client input.
5. Every module ships with: migration script, repository, service with
   unit tests, controller with integration tests, and a minimal React
   screen with component tests.
6. Definition of done for any module = unit tests pass + integration test
   for its main API endpoints pass + it does not break previously green
   tests.
7. Money is DECIMAL(12,2), rates DECIMAL(5,2); rounding is half-up to two
   decimals applied once at line level, and every calculation formula lives
   in a pure, directly unit-tested function — never inline in a controller
   or a React component.
8. No money-moving fact has two sources of truth. A salary payout is an
   expense row; a commission is settled by reference to that expense id.
```

### Step 1 — Foundation & Auth

```
/specify
Phase 1 — Foundation: project scaffolding, MySQL schema/migrations for
Users and Roles, JWT authentication, and role-based authorization
middleware (Admin vs Staff). Include a health-check endpoint and a base
API response/error envelope used by all future modules.

/plan
ASP.NET Core 8 Web API with Dapper against MySQL 8. Auth: JWT with role
claim ("Admin"/"Staff"). Migrations via versioned SQL scripts.
Frontend: React app scaffold with a login screen, protected route wrapper,
and role-aware navigation shell.

/tasks
TDD task pairs (failing test -> implementation) for: user repository,
password hashing, login service, JWT issuance, role-based [Authorize]
policy, and a React auth context + login form with component tests.
Include integration tests for POST /api/auth/login.

/implement
```

### Step 2 — Product & Variant Catalog

```
/specify
Phase 2 — Product/Variant catalog: CRUD for products (name, category,
brand, model, image, barcode/SKU, purchase price, wholesale price, retail
price, sale price, quantity, minimum stock threshold, supplier reference).
Each variant is its own product row. Global search across name, brand,
model, category, barcode. Low-stock flag computed from quantity vs
minimum threshold.

/plan
Repository + Service + Controller for Products in ASP.NET/Dapper against a
`products` table with a `suppliers` foreign key. React screens: product
list with search/filter, low-stock badge, product create/edit form with
image upload.

/tasks
TDD pairs for: create product validation (prices >= 0, quantity >= 0),
search-by-any-field repository query, low-stock computation, update
quantity helper (reused by purchase/sale/return), and React component
tests for the product form validation and low-stock badge rendering.

/implement
```

### Step 3 — Purchases & Suppliers

```
/specify
Phase 3 — Purchasing: supplier CRUD with running payable balance; purchase
entry (supplier, product, purchase price, quantity, date) that atomically
increments product stock and increases the supplier's payable balance;
supplier payment recording that decreases the payable balance; purchase
history per supplier.

/plan
New `suppliers`, `purchases`, `supplier_payments` tables. Service method
`RecordPurchase` runs in a single DB transaction: insert purchase row,
increment product stock, increment supplier payable. React: purchase entry
form, supplier ledger screen.

/tasks
TDD pairs for: RecordPurchase transaction (stock +qty, payable +total),
rollback-on-failure test (simulate failure mid-transaction, assert no
partial state), RecordSupplierPayment (payable -amount, cannot go negative
without an explicit overpayment flag), and React tests for the purchase
form total calculation.

/implement
```

### Step 4 — Salesman Master & Commission Scheme  [NEW]

```
/specify
Phase 4 — Salesman master data: CRUD for salesman records (name, mobile,
CNIC, address, photo, joining date, leaving date, status, monthly base
salary), optional one-to-one link to a login user (a Staff user maps to
exactly one salesman), effective-dated commission schemes per salesman
(None | PercentOfSale | PercentOfProfit | FixedPerInvoice, with rate or
amount and an effective-from date), and optional monthly sale targets.
No sales exist yet, so this phase delivers master data plus the pure
commission-calculation function that the POS phase will call.
Admin-only for all write operations and for reading other salesmen.

/clarify
Confirm: can two salesman records share one user account? (No.) Can a
scheme be edited retroactively? (No — a change inserts a new effective-dated
row.) What happens to an active scheme when a salesman is deactivated?
(It remains for history; new attribution is blocked.)

/plan
New tables: `salesmen` (with nullable unique `user_id`), `salesman_schemes`
(salesman_id, basis, rate, fixed_amount, effective_from, created_by),
`salesman_targets` (salesman_id, year, month, target_amount).
Service: SalesmanService (CRUD, activate/deactivate), SchemeService
(AddScheme, GetSchemeEffectiveOn(salesmanId, date)), and a pure
CommissionCalculator with signature
Calculate(scheme, lineNetTotal, lineGrossProfit, isFirstLineOfInvoice)
-> decimal. React: salesman list, salesman form, scheme history panel,
target entry grid.

/tasks
TDD pairs for:
- CommissionCalculator: PercentOfSale (1,100 net at 5% -> 55.00),
  PercentOfProfit (300 profit at 10% -> 30.00), FixedPerInvoice (charged
  once per invoice, not per line — assert a 3-line invoice yields one
  fixed amount), None -> 0, negative/zero rate rejection, half-up rounding
  at 2 decimals (e.g. 1,001 at 2.5% -> 25.03).
- GetSchemeEffectiveOn: returns the scheme effective on the invoice date,
  not the latest one; returns None when no scheme predates the date;
  boundary test where effective_from equals the invoice date (inclusive).
- Salesman create validation: unique mobile, joining date not in the
  future, base salary >= 0, leaving date >= joining date.
- One user maps to at most one salesman (unique constraint + service-level
  error), and a Staff user with no salesman record cannot be attributed.
- Deactivation blocks new attribution but preserves history.
- React tests: salesman form validation, scheme history renders newest
  first, target grid computes achieved% as 0 with no sales yet.

/implement
```

### Step 5 — Sales / POS / Invoicing (with salesman attribution)

```
/specify
Phase 5 — Point of Sale: create an invoice with multiple line items
(product, qty, unit price, line discount), order-level discount, multiple
payment methods including partial/credit, auto-computed totals. Every
invoice records salesman_id — defaulted to the logged-in Staff user's
salesman record and not editable by Staff, selectable by Admin. Saving an
invoice atomically decrements stock per line, accrues salesman commission
per line using the scheme effective on the invoice date, and, if not fully
paid, increases the customer's ledger balance. Quick-create customer
inline if new.

/plan
New `customers`, `invoices` (with salesman_id FK), `invoice_items`, and
`salesman_commissions` tables. Service method `CreateInvoice` in ONE
transaction: insert invoice + items, decrement stock per item (guard
against negative stock), compute paid/remaining, update customer balance
if remaining > 0, insert commission rows (status Accrued) via
CommissionCalculator from Phase 4. React: POS screen with product
search/barcode input, cart, discount fields, payment method selector,
salesman selector (Admin only), running total.

/tasks
TDD pairs for:
- Totals engine as a pure function: subtotal, line discount, order
  discount, total, paid, remaining — edge cases: zero discount,
  over-discount rejection, discount exceeding line total, partial payment,
  full credit, zero-quantity rejection.
- Insufficient-stock rejection before any write.
- Atomic stock decrement + customer balance increase + commission accrual;
  a forced failure at the commission step rolls back stock and balance.
- Commission uses the scheme effective on the invoice date (backdated
  invoice test), and an invoice for a salesman with no scheme accrues zero
  rows rather than failing.
- Staff cannot post an invoice attributed to another salesman (403).
- React tests: cart total/remaining computation, payment-method switching,
  salesman selector hidden for Staff and pre-filled with their own name.

/implement
```

### Step 6 — Customer Ledger (Udhaar)

```
/specify
Phase 6 — Customer ledger: chronological running-balance view per customer
(bill, paid, balance per entry), "Receive Payment" action that reduces
balance and produces a payment record, customer profile summary (total
purchase, total paid, total outstanding).

/plan
New `customer_payments` table linked to `customers`. Running balance =
previous balance + new bill - new payment, persisted per entry for fast
reads. React: customer detail screen with ledger table and "Receive
Payment" modal.

/tasks
TDD pairs for: running-balance calculation across a sequence of
bills/payments (multi-entry test matching the worked example), ReceivePayment
service method, balance cannot go below zero without an explicit
refund/overpayment path, and React tests for ledger table rendering and
payment modal validation.

/implement
```

### Step 7 — Returns (with commission reversal)

```
/specify
Phase 7 — Returns: Sale Return (reduce invoice net amount, increase stock,
adjust customer balance, reverse the salesman commission for the returned
quantity) and Purchase Return (decrease stock, reduce supplier payable).
Both recompute affected profit figures.

/plan
New `sale_returns`, `sale_return_items`, `purchase_returns` tables
referencing the original invoice/purchase. Service methods run in
transactions mirroring the original creation logic inverted, and insert
Reversed rows into `salesman_commissions` rather than editing accrued rows.

/tasks
TDD pairs for:
- SaleReturn stock increment + customer balance adjustment (fully-paid
  original vs credit original).
- Partial-quantity return reverses commission proportionally (return 2 of
  5 units -> 40% of that line's commission reversed).
- Returning a line whose commission was already Settled still produces a
  Reversed row, creating a negative payable recovered on the next payout.
- PurchaseReturn stock decrement + supplier payable reduction, guarding
  against returning more than was purchased.
- React tests for the return forms.

/implement
```

### Step 8 — Shop Expense Management  [EXPANDED]

```
/specify
Phase 8 — Shop expenses: managed expense-category lookup (seeded with Rent,
Electricity, Internet, Transport, Salary, Tea/Refreshment, Repair &
Maintenance, Packaging, Marketing, Bank Charges, Other; Admin can add,
rename, deactivate — never delete a referenced category). Expense record:
category, amount, date, payment method, paid-to/vendor, reference/bill no,
receipt image, note, optional salesman link (for Salary), created-by.
Recurring expenses (Monthly/Weekly) with start/optional end date that a
scheduled job materialises as a draft expense on its due date. Optional
monthly budget per category with over-budget flagging. Soft delete only.
Expense listing with date-range, category, payment-method and text filters,
paginated, with period total and per-category breakdown. Admin-only for
both read and write. Every create/edit/delete writes an audit entry.

/clarify
Confirm: are expenses cash-basis (recorded on payment date)? (Yes.) Does a
draft recurring expense count toward net profit before confirmation? (No —
only confirmed expenses affect profit.) Can an expense be backdated into a
closed reporting period? (Yes, but it is audited and reports recompute.)

/plan
New tables: `expense_categories` (name, is_active, is_system),
`expenses` (category_id, amount, expense_date, payment_method, vendor,
reference_no, attachment_path, note, salesman_id NULL, status
Draft|Confirmed|Deleted, created_by, created_at, updated_at),
`recurring_expenses` (category_id, amount, frequency, start_date,
end_date, next_run_date, is_active), `expense_budgets` (category_id, year,
month, budget_amount). Service: ExpenseService (CRUD + confirm + soft
delete), CategoryService, RecurringExpenseJob (IHostedService with an
injectable clock). Pure functions: SumByPeriod(expenses, from, to) and
BudgetVariance(actual, budget). React: expense list with filters and
totals, expense entry form with receipt upload, category manager, recurring
schedule screen, budget grid.

/tasks
TDD pairs for:
- Expense create validation: amount > 0, date not in the future, category
  must exist and be active, Salary category requires a salesman_id,
  non-Salary category rejects a salesman_id.
- Category deactivate-not-delete: deleting a referenced category is
  rejected; deactivating hides it from new-entry dropdowns but existing
  expenses still render its name.
- SumByPeriod: inclusive date boundaries, excludes Draft and Deleted rows,
  groups correctly by category, returns zero (not null) for an empty period.
- Day/week/month/year boundary and timezone tests (shop timezone
  Asia/Karachi; an expense at 23:59 local belongs to that local day).
- Recurring job with a mockable clock: generates exactly one draft on the
  due date, is idempotent when run twice the same day, advances
  next_run_date, stops at end_date, skips inactive definitions.
- Soft delete: deleted expense is excluded from profit but still readable
  in the audit view; re-deleting is a no-op.
- BudgetVariance flags over-budget only when actual > budget for that
  category/month.
- Authorization: a Staff JWT gets 403 on every expense endpoint including
  GET.
- React tests: expense form validation (salesman field appears only for
  Salary), filter/total recomputation, over-budget row styling.

/implement
```

### Step 9 — Salary & Commission Payout, Salesman Ledger  [NEW]

```
/specify
Phase 9 — Salesman payouts: per-salesman ledger of earnings (base salary
accrued per month + commission accrued) versus payouts, with an
outstanding payable. Admin "Pay Salesman" action for a chosen period and
amount that, in one transaction, creates a Salary expense (Phase 8),
marks the covered commission rows Settled with that expense id, and
records the payout. Payslip PDF generated in Phase 11. Salesman
performance report: invoices, sale value, gross profit generated, discount
given, returns attributed, commission earned/paid/outstanding, target vs
achieved.

/plan
New `salesman_payouts` table (salesman_id, period_from, period_to,
base_salary_amount, commission_amount, adjustment_amount, net_amount,
expense_id FK, paid_on, created_by). Service SalesmanPayoutService with
one transactional method PayForPeriod. Read-optimised Dapper queries for
the ledger and the performance report. React: salesman ledger screen,
"Pay Salesman" modal with a period picker showing computed base +
commission + adjustment = net, and a performance report screen.

/tasks
TDD pairs for:
- Outstanding calculation: base salary accrued for full months in range +
  Accrued commission - previous payouts; Reversed commission rows reduce
  it; Settled rows are excluded from a second payout.
- PayForPeriod is atomic: creates exactly one expense row with category
  Salary and salesman_id set, and marks exactly the commissions in the
  period Settled with that expense_id; a forced failure at the expense
  step leaves every commission still Accrued.
- Double-payout guard: paying the same period twice pays zero commission
  the second time (already Settled) and requires an explicit adjustment.
- A Reversed commission arriving after settlement produces a negative
  outstanding that is recovered from the next payout.
- Performance report aggregation matches hand-computed fixtures for a
  salesman with 3 invoices, 1 partial return and 1 discount.
- Target vs achieved: achieved% = sale value / target * 100; a target of
  zero or null yields null rather than a divide-by-zero.
- Authorization: Staff gets 403 on payout endpoints and on another
  salesman's ledger, and 200 with only their own summary on
  GET /api/salesmen/me/performance (no cost or profit fields in the
  response body — assert on the serialised JSON).
- React tests: payout modal totals, disabled Pay button when outstanding
  is zero, performance table rendering.

/implement
```

### Step 10 — Profit Engine, Dashboard & Reports

```
/specify
Phase 10 — Reporting layer: dashboard KPIs (today/month/year), profit
engine (gross profit per line, net profit per period = gross profit -
expenses - unsettled commission accrual, product-wise profit,
salesman-wise contribution), and the full report set: daily/monthly/annual
sales, total purchases, current stock, low stock, full stock movement
history, customer outstanding, supplier payable, salesman payable, expense
report (by category, by payment method, budget vs actual), salesman
performance.

/plan
Read-optimised Dapper queries (no new write paths) aggregating from
invoices, invoice_items, purchases, returns, expenses, salesman_commissions,
products, customers, suppliers, salesmen. React: dashboard with period
toggle and KPI cards, report screens with date-range filters and
export-friendly tables.

/tasks
TDD pairs for: gross-profit-per-line formula (worked example: sale 1,100 /
cost 800 / profit 300), net-profit rollup (gross profit - expenses -
commission) for day/week/month/year boundaries including Asia/Karachi
timezone edges, no-double-count test (a commission settled into a Salary
expense is subtracted exactly once), product-wise and salesman-wise
aggregation, low-stock query threshold logic, and React tests for KPI card
rendering and period toggling.

/implement
```

### Step 11 — PDF Invoice / Receipt / Payslip & WhatsApp

```
/specify
Phase 11 — Document generation & delivery: branded PDFs for every invoice,
every customer payment receipt, and every salesman payslip ("Moiz Mobile &
Corporation, Danwran Lodhran" header, document number, date, party name,
line items, totals, paid, remaining, thank-you footer; the invoice also
shows the salesman name, the payslip shows base salary + commission +
adjustments = net paid). "Send on WhatsApp" opens WhatsApp addressed to
the stored mobile number with the PDF/share link.

/plan
Backend PDF service (QuestPDF) producing a byte stream served via download
endpoints. Frontend: "Download PDF" and "Send on WhatsApp" buttons on the
invoice, ledger-payment and payout screens, using a wa.me deep link (or
WhatsApp Business API when credentials exist).

/tasks
TDD pairs for: PDF content assembly (all required fields present, totals
match the source document, payslip net = base + commission + adjustments),
WhatsApp link builder (Pakistani number normalisation: 03001234567 ->
923001234567, +92 and 0092 forms, rejection of malformed numbers, message
URL-encoding), and React tests for send-button click handlers and disabled
state when no phone number is on file.

/implement
```

### Step 12 — Roles Enforcement, Backup, Audit

```
/specify
Phase 12 — Operational hardening: enforce that Staff cannot read purchase
price, profit figures, expenses, other salesmen's data, others'
commissions, or financial reports on any endpoint (not just hidden in the
UI); automated daily MySQL backup job; Admin-only manual Backup Now /
Restore; audit log of stock, ledger, expense and commission mutations
(user, old value, new value, timestamp).

/plan
Authorization policies applied per-endpoint (not per-controller) for any
route or field touching cost price, profit, expenses, or another
salesman's data. Scheduled backup job dumping MySQL to a storage location.
Audit table populated by the existing transactional services from prior
phases (retrofit, not rewrite).

/tasks
TDD pairs for: a table-driven authorization test asserting a Staff JWT gets
403 on every cost/profit/expense/payout endpoint and cannot read another
salesman's record; response-shape tests asserting cost and profit fields
are absent from Staff-facing product and invoice payloads; audit-log write
on stock/ledger/expense/commission mutation (correct before/after values);
backup job triggers on schedule (mockable clock); restore-from-backup
integration test against a throwaway test database.

/implement
```

---

## PART 4 — Execution Order & Dependencies

| # | Phase | Depends on |
|---|-------|------------|
| 0 | Constitution | — |
| 1 | Foundation & Auth | — |
| 2 | Product/Variant Catalog | 1 |
| 3 | Purchases & Suppliers | 2 |
| 4 | **Salesman Master & Commission Scheme** | 1 |
| 5 | Sales / POS / Invoicing | 2, 3, 4 |
| 6 | Customer Ledger (Udhaar) | 5 |
| 7 | Returns | 3, 5, 6 |
| 8 | **Shop Expense Management** | 1, 4 (Salary → salesman link) |
| 9 | **Salary & Commission Payout / Salesman Ledger** | 4, 5, 7, 8 |
| 10 | Profit Engine, Dashboard, Reports | 3, 5, 6, 7, 8, 9 |
| 11 | PDF & WhatsApp | 5, 6, 9 |
| 12 | Roles Enforcement, Backup, Audit | all above |

Every module later phases depend on — stock math (2–3), commission math (4),
totals math (5), balance math (6), expense math (8) — has its unit-tested
foundation in place before it is reused.

---

## PART 5 — New Tables Introduced in v2

```
salesmen(id, name, mobile, cnic, address, photo_path, joining_date,
         leaving_date, status, base_salary, user_id UNIQUE NULL,
         created_by, created_at, updated_at)

salesman_schemes(id, salesman_id, basis ENUM('None','PercentOfSale',
         'PercentOfProfit','FixedPerInvoice'), rate DECIMAL(5,2) NULL,
         fixed_amount DECIMAL(12,2) NULL, effective_from DATE,
         created_by, created_at)

salesman_targets(id, salesman_id, year, month, target_amount)

salesman_commissions(id, salesman_id, invoice_id, invoice_item_id NULL,
         basis, rate, amount DECIMAL(12,2),
         status ENUM('Accrued','Settled','Reversed'),
         settled_expense_id NULL, reverses_commission_id NULL, created_at)

salesman_payouts(id, salesman_id, period_from, period_to,
         base_salary_amount, commission_amount, adjustment_amount,
         net_amount, expense_id, paid_on, created_by, created_at)

expense_categories(id, name UNIQUE, is_active, is_system, created_at)

expenses(id, category_id, amount DECIMAL(12,2), expense_date,
         payment_method, vendor, reference_no, attachment_path, note,
         salesman_id NULL, status ENUM('Draft','Confirmed','Deleted'),
         created_by, created_at, updated_at)

recurring_expenses(id, category_id, amount, frequency ENUM('Weekly',
         'Monthly'), start_date, end_date NULL, next_run_date, is_active,
         created_by)

expense_budgets(id, category_id, year, month, budget_amount)
```

Column added to an existing v1 table: `invoices.salesman_id` (FK → `salesmen`,
NOT NULL) — introduced in Phase 5, which is why Phase 4 must precede it.
