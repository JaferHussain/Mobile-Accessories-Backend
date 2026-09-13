# Phase 1 Data Model: Credit Controls & Customer Opening Balances

**Feature**: 002-credit-controls-opening-balances
**Date**: 2026-09-11

All changes go through one new DbUp migration, `0015_customer_opening_balance.sql`. Migration
numbering continues from `0014_sale_type.sql`. No existing migration is edited (constitution:
schema changes are new numbered scripts only).

---

## §1. `customers` — new column

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `opening_balance` | `DECIMAL(12,2)` | NULL | `NULL` | What this customer already owed before the software was in use. `NULL` means none was ever recorded, which is distinct from a recorded `0.00`. |

**Constraint**: `ck_customers_opening_balance_non_negative` — `opening_balance IS NULL OR
opening_balance >= 0`.

Enforces FR-069. A negative figure would mean the shop owes the customer, which is out of scope
per the spec's Assumptions.

**Why nullable rather than `NOT NULL DEFAULT 0`**: "never recorded" and "recorded as zero" are
different facts about a customer, and the second recording of an opening balance is a correction
(FR-070) which the service decides by asking whether one exists yet. A default of `0.00` would
make every customer look like they had been through the paper register.

`outstanding_balance` is unchanged in shape and continues to be the single authoritative figure
for what the customer owes; the opening balance is included in it, not added on top at read time.

## §2. `ledger_entries` — new entry type and reason

### `entry_type` gains `OpeningBalance`

```
ENUM('Invoice', 'Payment', 'SaleReturn', 'Adjustment')
  →
ENUM('Invoice', 'Payment', 'SaleReturn', 'Adjustment', 'OpeningBalance')
```

Requires `ALTER TABLE ledger_entries MODIFY COLUMN entry_type ...`. Appending to the end of a
MySQL `ENUM` preserves the ordinal of every existing value, so stored rows are unaffected.

Satisfies FR-067: the carried-forward amount is distinguishable from a sale, a payment or a
return.

### New column

| Column | Type | Null | Notes |
|---|---|---|---|
| `note` | `VARCHAR(255)` | NULL | Why this entry exists. Mandatory on a correction (FR-071), optional otherwise. |

FR-071 requires the reason to be *visible*; the audit trail alone would not put it in front of
whoever reads the customer's ledger.

### Entry shapes for this feature

| Entry | `entry_type` | `bill_amount` | `paid_amount` | `reference_id` | `note` |
|---|---|---|---|---|---|
| Opening balance recorded | `OpeningBalance` | the amount carried forward | `0` | `NULL` | optional |
| Opening balance increased | `Adjustment` | the increase | `0` | `NULL` | **required** |
| Opening balance decreased | `Adjustment` | `0` | the decrease | `NULL` | **required** |

A correction is expressed as the **difference**, never the new total (research R6). Correcting
12,000 to 10,000 writes `paid_amount = 2000`, reducing the balance by 2,000 — it must never add
10,000 on top.

`balance_after` continues to follow the existing invariant:
`balance_after = previous balance_after + bill_amount − paid_amount`.

## §3. Domain

### `LedgerEntryType` enum

Gains `OpeningBalance = 5`. Appended, so no existing persisted value is renumbered. Serialised by
name (`JsonStringEnumConverter` is registered — see the traps table in `CLAUDE.md`).

### `Customer` entity

Gains `decimal? OpeningBalance`.

### No new entity

An opening balance is an attribute of a customer plus an entry in their ledger. Introducing an
"OpeningBalance" entity would add a table whose only row per customer duplicates the column.

## §4. Invariants

These hold after every operation this feature introduces, and integration tests assert them.

1. **The ledger reproduces the balance.** For any customer, replaying `bill_amount − paid_amount`
   over their entries in order equals `customers.outstanding_balance` (SC-020). This already holds
   and must survive the new entry types.
2. **`balance_after` on the last entry equals `outstanding_balance`.**
3. **A correction never compounds.** After correcting an opening balance from A to B, the
   customer's balance differs from its pre-correction value by exactly `B − A` (FR-070).
4. **`opening_balance` is never negative**, enforced by both the service and the check constraint.
5. **No invoice exists with `amount_remaining > 0` created by a Staff user** (FR-051). Asserted
   directly against the database, not only through the API.
6. **Stock is untouched by a refused credit sale** (FR-055).

## §5. Transaction boundaries

| Operation | Locks | Writes |
|---|---|---|
| Record opening balance | `customers` row `FOR UPDATE` | `customers.opening_balance`, `customers.outstanding_balance`, one `ledger_entries` row, one `audit_entries` row |
| Correct opening balance | `customers` row `FOR UPDATE` | same, with an `Adjustment` entry for the difference |
| Refused credit sale | product rows already locked by `InvoiceService` | **nothing** — the refusal happens before the first write |

Each runs in a single unit of work at READ COMMITTED via `IUnitOfWorkFactory`, matching
`ReceivePaymentAsync` (constitution Principle IV, research R7).

## §6. Migration safety

- **Existing customers** get `opening_balance = NULL` and are unaffected: their outstanding
  balance already reflects everything the system knows about.
- **Existing ledger entries** are unaffected; the `ENUM` gains a value and `note` arrives `NULL`.
- **Reversibility**: the column and enum value can be dropped without data loss for any customer
  whose opening balance was never set. This is not an automated down-migration — DbUp is
  forward-only in this project — but it constrains nothing that existed before.
