-- ============================================================================
--  Applying migrations 0020 and 0021 to the LIVE database by hand.
--
--  Normally you would run:
--      dotnet run --project backend/src/MoizPos -- migrate
--  which does all of this and keeps DbUp's journal in step automatically.
--  This file exists for when you are working directly in phpMyAdmin / a SQL
--  console on the server instead.
--
--  BACK UP FIRST. These statements alter live tables.
--
--  IMPORTANT — the last block of each section writes a row into DbUp's journal
--  table (`schemaversions`). Do NOT skip it. If you apply the columns without
--  journalling them, the next `migrate` run will try to add them a second time
--  and fail with "Duplicate column name".
-- ============================================================================


-- ----------------------------------------------------------------------------
-- STEP 0 — What is already applied? Run this FIRST and read the output.
-- ----------------------------------------------------------------------------

SELECT scriptname, applied
FROM schemaversions
ORDER BY schemaversionsid DESC
LIMIT 10;

-- You should see 0019_customer_sale_type as the newest. If 0020 and 0021 are
-- already listed, STOP — nothing below needs running and the error is
-- something else.
--
-- This also confirms the columns really are missing:

SELECT table_name, column_name
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND ((table_name = 'sale_return_items'
        AND column_name IN ('unit_refund_price', 'discount_total'))
    OR (table_name = 'invoices'
        AND column_name IN ('payment_account_number', 'payment_transaction_id')))
ORDER BY table_name, column_name;

-- Expect ZERO rows before you start, and FOUR rows after you finish.


-- ----------------------------------------------------------------------------
-- STEP 1 — 0020: record the discount a return adjusts for
--
-- A charger listed at 600 and sold for 575 after a 25 discount comes back worth
-- 575 — the customer is refunded what they paid. The 25 is recorded, not thrown
-- away, so the shopkeeper can explain the figure and the owner can see months
-- later how it was reached.
-- ----------------------------------------------------------------------------

ALTER TABLE sale_return_items
    ADD COLUMN unit_refund_price DECIMAL(12,2) NOT NULL DEFAULT 0 AFTER unit_sale_price,
    ADD COLUMN discount_total    DECIMAL(12,2) NOT NULL DEFAULT 0 AFTER unit_cost_price;

-- Returns recorded before today carry no discount: each was refunded at the
-- price it was billed at, so the refund price IS the sale price. Stated
-- explicitly rather than left as a column of zeroes that reads like missing data.
UPDATE sale_return_items
SET unit_refund_price = unit_sale_price
WHERE unit_refund_price = 0;

ALTER TABLE sale_return_items
    ADD CONSTRAINT ck_sale_return_items_refund_non_negative
        CHECK (unit_refund_price >= 0 AND discount_total >= 0);

INSERT INTO schemaversions (scriptname, applied)
VALUES ('MoizPos.Migrator.Scripts.0020_sale_return_discount.sql', UTC_TIMESTAMP());


-- ----------------------------------------------------------------------------
-- STEP 2 — 0021: where a non-cash payment came from
--
-- The invoice already records WHICH method was used and can carry a screenshot.
-- Neither answers the question asked in a dispute: which account did the money
-- come from, and what was the reference?
--
-- Both NULL by design, permanently — they are prompted for, never required.
-- A CASH sale is refused both by the application: money counted into the drawer
-- came from no account.
-- ----------------------------------------------------------------------------

ALTER TABLE invoices
    ADD COLUMN payment_account_number VARCHAR(50) NULL AFTER payment_method,
    ADD COLUMN payment_transaction_id VARCHAR(64) NULL AFTER payment_account_number;

INSERT INTO schemaversions (scriptname, applied)
VALUES ('MoizPos.Migrator.Scripts.0021_invoice_payment_reference.sql', UTC_TIMESTAMP());


-- ----------------------------------------------------------------------------
-- STEP 3 — Confirm. Re-run the checks from STEP 0.
-- ----------------------------------------------------------------------------

SELECT scriptname, applied
FROM schemaversions
ORDER BY schemaversionsid DESC
LIMIT 5;

-- Expect 0021 newest, then 0020, then 0019.

SELECT table_name, column_name
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND ((table_name = 'sale_return_items'
        AND column_name IN ('unit_refund_price', 'discount_total'))
    OR (table_name = 'invoices'
        AND column_name IN ('payment_account_number', 'payment_transaction_id')))
ORDER BY table_name, column_name;

-- Expect exactly four rows. Then restart the backend and make a test sale.
