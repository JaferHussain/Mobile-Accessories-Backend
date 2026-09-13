# Phase 1 Data Model: POS, Inventory & Customer Ledger System

**Feature**: `001-pos-inventory-ledger` · **Date**: 2026-09-09

Conventions: `BIGINT UNSIGNED AUTO_INCREMENT` surrogate keys named `id`; money as
`DECIMAL(12,2)`; product cost as `DECIMAL(12,4)`; quantities as `INT`; timestamps as
`DATETIME(6)` in **UTC**; `InnoDB` / `utf8mb4_0900_ai_ci`. Every table carries
`created_at_utc`, and mutable tables also carry `updated_at_utc` and `created_by_user_id`.

---

## Entity overview

```
users ──< refresh_tokens
users ──< audit_entries

suppliers ──< purchases >── products
suppliers ──< supplier_payments
suppliers ──< purchase_returns

products ──< invoice_items
products ──< stock_movements

customers ──< invoices ──< invoice_items
customers ──< customer_payments
customers ──< ledger_entries

invoices ──< sale_returns ──< sale_return_items
purchases ──< purchase_returns

expenses          (standalone)
expense_categories ──< expenses
document_tokens ──> invoices | customer_payments
```

---

## 1. `users`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `username` | VARCHAR(50) | UNIQUE, NOT NULL |
| `full_name` | VARCHAR(100) | NOT NULL |
| `password_hash` | VARCHAR(255) | NOT NULL — ASP.NET Core Identity PBKDF2 format |
| `role` | ENUM('Admin','Staff') | NOT NULL |
| `is_active` | BOOLEAN | NOT NULL DEFAULT TRUE |

**Rules**: username 3–50 chars, unique case-insensitively. Password minimum 8 characters at
creation. Deactivating rather than deleting preserves `created_by_user_id` references and the
audit trail. At least one active Admin must always exist — deactivating the last one is refused.

## 2. `refresh_tokens`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `user_id` | BIGINT UNSIGNED FK → users | |
| `token_hash` | CHAR(64) | SHA-256 of the token; UNIQUE |
| `expires_at_utc` | DATETIME(6) | NOT NULL |
| `revoked_at_utc` | DATETIME(6) NULL | set on rotation or logout |

**Rules**: 30-day lifetime (R9). Rotated on each use — the presented token is revoked as the
replacement is issued. Presenting an already-revoked token revokes the whole chain for that user.

## 3. `suppliers`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `name` | VARCHAR(150) | NOT NULL, INDEX |
| `contact_number` | VARCHAR(20) NULL | |
| `address` | VARCHAR(255) NULL | |
| `payable_balance` | DECIMAL(12,2) | NOT NULL DEFAULT 0 |
| `is_active` | BOOLEAN | NOT NULL DEFAULT TRUE |

**Rules**: `payable_balance` is a running total maintained only inside purchase, supplier-payment
and purchase-return transactions. It must always equal
`SUM(purchases.total) − SUM(supplier_payments.amount) − SUM(purchase_returns.total)`; an
integration test asserts this invariant. It may go negative only when an overpayment was
explicitly confirmed (FR-009).

## 4. `products`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `name` | VARCHAR(150) | NOT NULL |
| `category` | VARCHAR(80) | NOT NULL, INDEX |
| `brand` | VARCHAR(80) NULL | INDEX |
| `model` | VARCHAR(80) NULL | |
| `barcode` | VARCHAR(64) NULL | UNIQUE where not null |
| `image_path` | VARCHAR(255) NULL | relative path (R7) |
| `cost_price` | DECIMAL(12,4) | NOT NULL — **latest purchase cost** |
| `wholesale_price` | DECIMAL(12,2) | NOT NULL |
| `retail_price` | DECIMAL(12,2) | NOT NULL |
| `sale_price` | DECIMAL(12,2) | NOT NULL — the default price at the counter |
| `quantity_on_hand` | INT | NOT NULL DEFAULT 0 |
| `min_stock_threshold` | INT | NOT NULL DEFAULT 0 |
| `supplier_id` | BIGINT UNSIGNED NULL | FK → suppliers |
| `is_active` | BOOLEAN | NOT NULL DEFAULT TRUE |

**Indexes**: `FULLTEXT(name, brand, model, category)` for the unified search box, plus a
`(quantity_on_hand, min_stock_threshold)` covering index for the low-stock query.

**Rules**:
- Each variant is its own row (FR-001). No shared options/variants table.
- All prices ≥ 0; `quantity_on_hand` ≥ 0 enforced by a `CHECK` constraint **and** in service
  logic (FR-006).
- **Low stock** is derived, never stored: `quantity_on_hand <= min_stock_threshold` (FR-004).
- **`cost_price` is overwritten on every purchase** with that purchase's unit cost (FR-011a), and
  applies to all units on hand regardless of what they originally cost.
- Products are deactivated, never deleted, so historical invoices stay readable (FR-002).
- `cost_price` MUST NOT appear in any Staff-facing DTO (FR-040, R10).

## 5. `stock_movements`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `product_id` | BIGINT UNSIGNED FK → products | INDEX |
| `change_qty` | INT | NOT NULL — signed: positive in, negative out |
| `resulting_qty` | INT | NOT NULL — quantity after this movement |
| `reason` | ENUM('Purchase','Sale','SaleReturn','PurchaseReturn','Adjustment') | NOT NULL |
| `reference_id` | BIGINT UNSIGNED NULL | id of the purchase/invoice/return |
| `user_id` | BIGINT UNSIGNED FK → users | |
| `note` | VARCHAR(255) NULL | |

**Rules**: append-only; never updated or deleted. Written inside the same transaction as the
`products.quantity_on_hand` update it explains, so the two can never disagree (FR-005).
`resulting_qty` is persisted so the history reads correctly without replaying every prior row.

## 6. `customers`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `name` | VARCHAR(150) | NOT NULL, INDEX |
| `mobile_number` | VARCHAR(20) NULL | INDEX; required for WhatsApp delivery |
| `address` | VARCHAR(255) NULL | |
| `outstanding_balance` | DECIMAL(12,2) | NOT NULL DEFAULT 0 |
| `is_active` | BOOLEAN | NOT NULL DEFAULT TRUE |

**Rules**: `outstanding_balance` is maintained only inside invoice, payment and return
transactions, and must always equal the `balance_after` of the customer's latest `ledger_entries`
row — asserted by integration test. It may go negative only on a confirmed overpayment (FR-022).
Mobile numbers are stored normalised to digits with a country code for the `wa.me` link.

## 7. `invoices`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `invoice_number` | VARCHAR(20) | UNIQUE, NOT NULL — e.g. `INV-2026-000123` |
| `customer_id` | BIGINT UNSIGNED NULL | NULL only for a fully paid walk-in sale |
| `invoice_date_utc` | DATETIME(6) | NOT NULL, INDEX (period queries) |
| `subtotal` | DECIMAL(12,2) | sum of line totals before order discount |
| `order_discount` | DECIMAL(12,2) | NOT NULL DEFAULT 0 |
| `total` | DECIMAL(12,2) | `subtotal − order_discount` |
| `amount_paid` | DECIMAL(12,2) | NOT NULL |
| `amount_remaining` | DECIMAL(12,2) | `total − amount_paid` |
| `net_amount` | DECIMAL(12,2) | `total` less the value of any sale returns |
| `payment_method` | ENUM('Cash','BankTransfer','JazzCash','EasyPaisa','Raast','Credit','Partial') | NOT NULL |
| `user_id` | BIGINT UNSIGNED FK → users | who rang it up |

**Rules**:
- Every monetary field is recomputed server-side from `invoice_items` — client totals are
  discarded (FR-013, Principle IV).
- `order_discount` ≤ `subtotal`; `total` ≥ 0.
- `amount_paid` ≥ 0 and ≤ `total`.
- `customer_id` is **required** whenever `amount_remaining > 0` (FR-017).
- `net_amount` starts equal to `total` and is reduced by sale returns (FR-024).

## 8. `invoice_items`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `invoice_id` | BIGINT UNSIGNED FK → invoices | INDEX, `ON DELETE RESTRICT` |
| `product_id` | BIGINT UNSIGNED FK → products | |
| `product_name` | VARCHAR(150) | **snapshot** at time of sale |
| `quantity` | INT | NOT NULL, > 0 |
| `unit_sale_price` | DECIMAL(12,2) | as charged, may differ from `products.sale_price` |
| `line_discount` | DECIMAL(12,2) | NOT NULL DEFAULT 0 |
| `unit_cost_price` | DECIMAL(12,4) | **snapshot of `products.cost_price` at sale time** |
| `line_total` | DECIMAL(12,2) | `(unit_sale_price × quantity) − line_discount` |
| `returned_qty` | INT | NOT NULL DEFAULT 0 |

**Rules**:
- `unit_cost_price` is the single most important field for correctness. Captured at sale time so
  a later purchase never rewrites historical profit (FR-011c). It is Admin-only data (FR-040).
- `product_name` is snapshotted so renaming or deactivating a product leaves old invoices
  readable (spec edge case).
- `line_discount` ≤ `unit_sale_price × quantity`.
- `returned_qty` ≤ `quantity`, maintained by sale returns (FR-026).
- **Gross profit for this line** = `(unit_sale_price − unit_cost_price) × (quantity − returned_qty) − line_discount` (FR-031, FR-027).

## 9. `ledger_entries`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `customer_id` | BIGINT UNSIGNED FK → customers | INDEX `(customer_id, entry_date_utc, id)` |
| `entry_date_utc` | DATETIME(6) | NOT NULL |
| `entry_type` | ENUM('Invoice','Payment','SaleReturn','Adjustment') | NOT NULL |
| `reference_id` | BIGINT UNSIGNED NULL | invoice / payment / return id |
| `bill_amount` | DECIMAL(12,2) | NOT NULL DEFAULT 0 |
| `paid_amount` | DECIMAL(12,2) | NOT NULL DEFAULT 0 |
| `balance_after` | DECIMAL(12,2) | NOT NULL — running balance |
| `user_id` | BIGINT UNSIGNED FK → users | |

**Rules**: append-only. `balance_after = previous balance_after + bill_amount − paid_amount`
(FR-020) — the worked example from the spec (bill 3,000 / paid 1,000 → 2,000; then paid 1,500 →
500) is a required unit test. Persisting `balance_after` makes the ledger screen a single indexed
read rather than a replay. The latest row's `balance_after` must always equal
`customers.outstanding_balance`.

## 10. `customer_payments`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `customer_id` | BIGINT UNSIGNED FK → customers | |
| `receipt_number` | VARCHAR(20) | UNIQUE — e.g. `RCP-2026-000045` |
| `amount` | DECIMAL(12,2) | NOT NULL, > 0 |
| `payment_method` | ENUM(...) | as invoices |
| `payment_date_utc` | DATETIME(6) | NOT NULL |
| `is_overpayment` | BOOLEAN | NOT NULL DEFAULT FALSE — explicit confirmation (FR-022) |
| `note` | VARCHAR(255) NULL | |
| `user_id` | BIGINT UNSIGNED FK → users | |

## 11. `purchases`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `supplier_id` | BIGINT UNSIGNED FK → suppliers | INDEX |
| `product_id` | BIGINT UNSIGNED FK → products | |
| `purchase_date_utc` | DATETIME(6) | NOT NULL, INDEX |
| `unit_cost` | DECIMAL(12,4) | NOT NULL, > 0 |
| `quantity` | INT | NOT NULL, > 0 |
| `total` | DECIMAL(12,2) | `unit_cost × quantity` |
| `returned_qty` | INT | NOT NULL DEFAULT 0 |
| `user_id` | BIGINT UNSIGNED FK → users | |

**Transactional effect** (FR-008, FR-011a) — one transaction, all or nothing:
1. Insert the `purchases` row.
2. `products.quantity_on_hand += quantity`.
3. **`products.cost_price = unit_cost`** (overwrite, applies to all stock on hand).
4. `suppliers.payable_balance += total`.
5. Insert `stock_movements` (`reason='Purchase'`).
6. Insert `audit_entries` for the stock and cost changes.

## 12. `supplier_payments`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `supplier_id` | BIGINT UNSIGNED FK → suppliers | |
| `amount` | DECIMAL(12,2) | NOT NULL, > 0 |
| `payment_date_utc` | DATETIME(6) | NOT NULL |
| `payment_method` | ENUM(...) | |
| `is_overpayment` | BOOLEAN | NOT NULL DEFAULT FALSE (FR-009) |
| `note` | VARCHAR(255) NULL | |
| `user_id` | BIGINT UNSIGNED FK → users | |

## 13. `sale_returns` / `sale_return_items`

`sale_returns`: `id`, `invoice_id` FK, `return_number` UNIQUE, `return_date_utc`,
`total_amount`, `refund_due` DECIMAL(12,2), `user_id`, `reason` VARCHAR(255) NULL.

`sale_return_items`: `id`, `sale_return_id` FK, `invoice_item_id` FK, `quantity`,
`unit_sale_price`, `unit_cost_price`, `line_total`.

**Rules** (FR-024, FR-026–028, R11): quantity returned ≤ `invoice_items.quantity − returned_qty`.
Values reverse at the **originally recorded** `unit_sale_price` and `unit_cost_price`, never at
the product's current figures. In one transaction: stock ↑, `invoice_items.returned_qty` ↑,
`invoices.net_amount` ↓, and either the customer's balance ↓ (if the sale was unpaid) or
`refund_due` is recorded (if it was already paid).

## 14. `purchase_returns`

`id`, `purchase_id` FK, `supplier_id` FK, `return_number` UNIQUE, `return_date_utc`, `quantity`,
`unit_cost`, `total`, `user_id`, `reason`.

**Rules** (FR-025–026, R11): quantity ≤ `purchases.quantity − purchases.returned_qty`. One
transaction: stock ↓ (guarded against going negative), `suppliers.payable_balance ↓` at the
**original** purchase cost, `purchases.returned_qty ↑`, stock movement and audit rows written.
`products.cost_price` is **not** reverted.

## 15. `expense_categories` / `expenses`

`expense_categories`: `id`, `name` VARCHAR(80) UNIQUE, `is_active`. Seeded with Rent,
Electricity, Internet, Transport, Salary, Other — extensible (FR-029).

`expenses`: `id`, `category_id` FK, `amount` DECIMAL(12,2) > 0, `expense_date_utc` INDEX,
`note` VARCHAR(255) NULL, `user_id`.

**Rules**: expenses are Admin-only data — they feed net profit (FR-030, FR-040).

## 16. `audit_entries`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `entity_type` | VARCHAR(50) | e.g. `Product`, `Customer`, `Supplier` |
| `entity_id` | BIGINT UNSIGNED | |
| `field_name` | VARCHAR(50) | e.g. `quantity_on_hand`, `outstanding_balance` |
| `old_value` | VARCHAR(100) NULL | |
| `new_value` | VARCHAR(100) NULL | |
| `action` | VARCHAR(50) | e.g. `Sale`, `Purchase`, `Payment`, `Adjustment` |
| `user_id` | BIGINT UNSIGNED FK → users | |
| `occurred_at_utc` | DATETIME(6) | INDEX |

**Rules** (FR-041): append-only, never updated or deleted. Written **inside** the mutating
transaction so a rolled-back change leaves no audit row claiming it happened. Covers every stock
and ledger/balance change.

## 17. `document_tokens`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGINT UNSIGNED PK | |
| `token_hash` | CHAR(64) | SHA-256 of the token; UNIQUE |
| `document_type` | ENUM('Invoice','PaymentReceipt') | NOT NULL |
| `reference_id` | BIGINT UNSIGNED | invoice or customer_payment id |
| `expires_at_utc` | DATETIME(6) | NOT NULL — default issue + 30 days |
| `revoked_at_utc` | DATETIME(6) NULL | |
| `last_accessed_utc` | DATETIME(6) NULL | |
| `access_count` | INT | NOT NULL DEFAULT 0 |

**Rules** (R2, and the plan's justified exception): the token is ≥128 bits of cryptographic
randomness; only its hash is stored. It resolves to exactly one document. Expired, revoked or
unknown tokens return 404 without distinguishing between those cases. This backs the single
unauthenticated endpoint in the system.

---

## Derived values — never stored as columns

| Value | Formula | Requirement |
|---|---|---|
| Low stock flag | `quantity_on_hand <= min_stock_threshold` | FR-004 |
| Line gross profit | `(unit_sale_price − unit_cost_price) × (quantity − returned_qty) − line_discount` | FR-031 |
| Period gross profit | `SUM(line gross profit)` over invoices in the period | FR-032 |
| Period net profit | `period gross profit − SUM(expenses in period)` | FR-032 |
| Customer total purchased / paid | aggregates over `ledger_entries` | FR-023 |
| Total receivables | `SUM(customers.outstanding_balance)` where > 0 | FR-035 |
| Total payables | `SUM(suppliers.payable_balance)` where > 0 | FR-035 |

## Cross-cutting invariants (integration-tested)

1. `products.quantity_on_hand` equals the latest `stock_movements.resulting_qty` for that product.
2. `customers.outstanding_balance` equals the latest `ledger_entries.balance_after`.
3. `suppliers.payable_balance` equals purchases − supplier payments − purchase returns.
4. `quantity_on_hand` is never negative.
5. A failed mutation leaves **no** row in `stock_movements`, `ledger_entries` or `audit_entries`.
6. `invoice_items.unit_cost_price` is immutable once written.
