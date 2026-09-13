# Quickstart: Credit Controls & Customer Opening Balances

**Feature**: 002-credit-controls-opening-balances

Runnable checks proving the feature works end to end against a live system. Each maps to a
success criterion. Run them after `/speckit-implement` and before calling the feature done.

Prerequisites: MySQL running, migrations applied, API on `http://localhost:5080`, UI on
`http://localhost:5173`. Sign-ins used below: `admin` (Admin) and `salesman` (Staff).

> **Stop the other API first.** Visual Studio and `dotnet run` cannot both hold the build output.
> `taskkill /IM MoizPos.Api.exe /F`

---

## Setup

```bash
dotnet run --project backend/src/MoizPos.Migrator     # applies 0015
dotnet run --project backend/src/MoizPos.Api
```

Confirm the migration landed:

```sql
SHOW COLUMNS FROM moizpos.customers LIKE 'opening_balance';
SHOW COLUMNS FROM moizpos.ledger_entries LIKE 'note';
SHOW COLUMNS FROM moizpos.ledger_entries LIKE 'entry_type';   -- must list OpeningBalance
```

---

## V1 — A salesman cannot sell on credit (SC-015, FR-051)

Sign in as `salesman`. Create a sale worth Rs 5,000 for a customer, paying **nothing**.

**Expect**: HTTP **403**, error code `CREDIT_REQUIRES_ADMIN`, message naming the unpaid amount.

Then confirm nothing was written:

```sql
-- All three must be unchanged from before the attempt.
SELECT COUNT(*) FROM moizpos.invoices;
SELECT quantity_on_hand FROM moizpos.products WHERE id = <product>;
SELECT outstanding_balance FROM moizpos.customers WHERE id = <customer>;
```

## V2 — A part-paid sale is credit too (SC-015, FR-052)

As `salesman`, the same Rs 5,000 sale paying Rs 3,000.

**Expect**: **403**, same code. A part-paid sale still leaves the shop's money with the customer.

## V3 — A salesman can still sell for full payment (SC-016, SC-017, FR-053)

As `salesman`, the same sale paying the full Rs 5,000.

**Expect**: **201**, stock decremented, `amount_remaining = 0`.

```sql
-- Invariant 5: no Staff user has ever created a credit sale.
SELECT i.id FROM moizpos.invoices i
JOIN moizpos.users u ON u.id = i.user_id
WHERE u.role = 'Staff' AND i.amount_remaining > 0;
-- must return zero rows
```

## V4 — The owner can sell on credit (FR-051)

As `admin`, the Rs 5,000 sale paying Rs 2,000.

**Expect**: **201**, `amountRemaining` 3,000, the customer's balance up by 3,000, and a
`Invoice` ledger entry recording it.

## V5 — A salesman can still collect money owed (SC-017, FR-054)

As `salesman`, receive Rs 1,000 from the customer who owes 3,000.

**Expect**: **201**, balance now 2,000. Collecting a debt is not extending one — this must not
have been broken by V1.

## V6 — The POS hides credit from a salesman (FR-056)

Open `http://localhost:5173/pos` signed in as `salesman`.

**Expect**: the Payment list offers no **Credit (udhaar)** and no **Part paid** option, and the
amount-paid field cannot be set below the total. Sign in as `admin` and both reappear.

## V7 — Recovering in instalments (SC-018, FR-058 … FR-060)

Take a customer owing Rs 3,000. Receive **1,000**, then **1,500**, then **500**.

**Expect** balances of **2,000 → 500 → 0**, three separate payments in the ledger, and the
customer gone from receivables at the end.

## V8 — Bad recovery amounts are refused (FR-061, FR-062)

| Attempt | Expect |
|---|---|
| Payment of `0` | **400** |
| Payment of `-100` | **400** |
| Payment of 800 against a 500 balance, without confirmation | **400**, overpayment warning |
| The same with `confirmOverpayment: true` | **201** |

## V9 — Recording what a customer already owed (SC-019, FR-065 … FR-067)

As `admin`, `PUT /api/customers/{id}/opening-balance` with `{ "amount": 12000 }`.

**Expect**: `openingBalance` 12,000, `wasCorrection` **false**, `outstandingBalance` 12,000.

```sql
SELECT entry_type, bill_amount, paid_amount, balance_after
FROM moizpos.ledger_entries WHERE customer_id = <id> ORDER BY id;
-- first row: OpeningBalance, 12000.00, 0.00, 12000.00
```

Open **Reports → Money owed to the shop**: the 12,000 must be in the total (FR-066).

## V10 — It behaves like real money afterwards (SC-020, FR-066)

On that same customer: sell **3,000** on credit as `admin`, then receive **5,000**.

**Expect**: balance **15,000** then **10,000**, and the ledger reading

```
OpeningBalance  12,000          → 12,000
Invoice          3,000          → 15,000
Payment                  5,000  → 10,000
```

Replaying `bill − paid` down the column must equal `customers.outstanding_balance` exactly.

## V11 — Correcting a mistyped figure never doubles it (SC-021, FR-070, FR-071)

Same customer, now owing 10,000 with an opening balance of 12,000. As `admin`, `PUT` again with
`{ "amount": 10000, "reason": "Mistyped from the register." }`.

**Expect**: `wasCorrection` **true**, `previousOpeningBalance` 12,000, and the outstanding balance
**8,000** — down by exactly the 2,000 difference, **not** up by 10,000.

```sql
SELECT entry_type, bill_amount, paid_amount, note FROM moizpos.ledger_entries
WHERE customer_id = <id> ORDER BY id;
-- The original OpeningBalance row is still there, unaltered.
-- A new Adjustment row carries paid_amount 2000.00 and the reason.
```

## V12 — A correction demands a reason (FR-071)

The same `PUT` without `reason`.

**Expect**: **422** — whether a reason is required depends on whether a figure already exists,
which request validation cannot see. Changing a figure about money must be explicable.

## V13 — A negative opening balance is refused (FR-069)

`PUT` with `{ "amount": -500 }` → **400**.

## V14 — A salesman cannot touch opening balances (FR-068)

As `salesman`, `PUT /api/customers/{id}/opening-balance` → **403**. The UI must not offer it
either.

## V15 — A customer with no history starts clean (FR-073)

Create a customer without an opening balance.

**Expect**: `outstanding_balance` 0.00, `opening_balance` **NULL**, and an empty ledger — no
opening row.

## V16 — It is all audited (SC-021, FR-072)

```sql
SELECT entity_type, entity_id, field_name, old_value, new_value, action, user_id, occurred_at_utc
FROM moizpos.audit_entries
WHERE entity_type = 'Customer' AND field_name = 'opening_balance'
ORDER BY id DESC;
```

**Expect**: one row for V9 (`old_value` NULL → `12000.00`) and one for V11
(`12000.00` → `10000.00`), each naming the user and time.

---

## Regression gate (constitution VI)

Neither suite may lose a test:

```bash
cd backend  && dotnet test
cd frontend && npm run test && npx tsc --noEmit
```

Baseline entering this feature: **490 backend** (192 unit + 290 integration + 8 architecture) and
**250 frontend**.

## Cleanup

The checks above write real rows. On a live shop database, remove the sales and payments they
created and reset the test customer's `opening_balance` to `NULL`, or run them against
`moizpos_test`.
