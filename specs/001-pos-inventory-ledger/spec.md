# Feature Specification: POS, Inventory & Customer Ledger System

**Feature Branch**: `001-pos-inventory-ledger`

**Created**: 2026-09-09

**Status**: Draft

**Input**: User description: "Build a full-stack Point-of-Sale, Inventory, and Customer Ledger Management System for 'Moiz Mobile & Corporation, Danwran Lodhran,' a mobile accessories retail shop that sells items such as chargers, cables, earbuds, covers, and similar variant-heavy products."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ring up a counter sale and hand over a receipt (Priority: P1)

A walk-in customer brings three items to the counter. The salesman searches or scans each item, the system adds it to a cart with its selling price, applies any per-item or whole-bill discount the shopkeeper agrees to, and shows the running total. The customer pays cash. The salesman saves the sale; stock for each item drops immediately and a printable receipt is produced.

**Why this priority**: This is the shop's daily lifeblood. Without it there is no reason to adopt the system at all, and every other module (profit, ledger, reports) is downstream of a recorded sale.

**Independent Test**: Load a handful of products with known stock and prices, ring up a multi-line cash sale with discounts, and verify the displayed total, the saved invoice, and the reduced stock counts. Delivers a working cash register on its own.

**Acceptance Scenarios**:

1. **Given** a product with 10 units in stock at a sale price of 1,100, **When** the salesman sells 2 units for cash with no discount, **Then** the invoice total is 2,200, the amount remaining is 0, and the product's stock becomes 8.
2. **Given** a cart with a subtotal of 5,000, **When** a line discount of 100 and an order discount of 400 are applied, **Then** the payable total is 4,500.
3. **Given** a product with 3 units in stock, **When** the salesman attempts to sell 5 units, **Then** the sale is rejected with a clear insufficient-stock message and no stock or invoice is written.
4. **Given** a saved invoice, **When** the salesman opens it, **Then** a branded receipt showing shop details, line items, totals, amount paid, and amount remaining is available to print or download.

---

### User Story 2 - Sell on credit and track what a customer owes (Priority: P1)

A regular customer takes goods worth 3,000 and pays only 1,000 today. The salesman records the sale as a partial payment against that customer. The customer's outstanding balance rises by 2,000. Days later the customer pays 1,500; the shopkeeper records the payment, the balance falls to 500, and the customer gets a payment receipt.

**Why this priority**: Udhaar is how this shop actually trades. Cash-only sales alone would leave the largest source of financial risk untracked, which is the main pain the system exists to solve.

**Independent Test**: Create a customer, record a credit sale and a later payment, and verify the ledger shows each entry with a correct running balance and the profile totals reconcile. Delivers a usable receivables book on its own.

**Acceptance Scenarios**:

1. **Given** a customer with a zero balance, **When** a 3,000 sale is saved with 1,000 paid, **Then** the customer's outstanding balance is 2,000 and a ledger entry records bill 3,000, paid 1,000, balance 2,000.
2. **Given** that customer at a balance of 2,000, **When** a payment of 1,500 is received, **Then** the new ledger entry shows paid 1,500 and balance 500, and a payment receipt is produced.
3. **Given** a sale to a person not yet on file, **When** the salesman saves a credit sale, **Then** the system requires a customer record and offers to create one inline from name and mobile number.
4. **Given** a customer with several invoices and payments, **When** the shopkeeper opens the customer profile, **Then** total purchased, total paid, and total outstanding are shown and equal the sum of the ledger entries.

---

### User Story 3 - Keep the shelf stocked and know what to reorder (Priority: P1)

The shopkeeper receives a delivery of 50 cables from a supplier. Recording the purchase raises the stock of that exact cable variant and increases what the shop owes that supplier. Each distinct variant is its own catalogue item with its own photo, cost, price, and count, so a "Type-C braided 2m" is never confused with a "Type-C plain 1m". Items at or below their minimum threshold are flagged for reorder.

**Why this priority**: Sales cannot be recorded against products that do not exist, and cost price captured at purchase time is what makes profit real rather than estimated.

**Independent Test**: Create supplier and product records, record a purchase, and verify stock rose, supplier payable rose, and a below-threshold item appears in the low-stock list. Delivers a standalone stock book.

**Acceptance Scenarios**:

1. **Given** a product with 5 units in stock, **When** a purchase of 50 units at cost 800 each is recorded against a supplier, **Then** stock becomes 55 and the supplier's payable increases by 40,000.
2. **Given** a product with a minimum threshold of 10, **When** its quantity falls to 10 or below, **Then** it is flagged as low stock and appears in the low-stock list.
3. **Given** five different USB cable variants, **When** the shopkeeper views the catalogue, **Then** each appears as its own item with its own image, cost, price, and quantity.
4. **Given** a search term matching a product's name, brand, model, category, or barcode, **When** entered in the single search box, **Then** matching products are returned.
5. **Given** a supplier with an outstanding payable, **When** a payment to that supplier is recorded, **Then** the payable decreases by that amount and the payment appears in the supplier's history.
6. **Given** stock of a cable bought earlier at a lower cost, **When** a new purchase arrives at a higher cost and the shopkeeper raises the sale price, **Then** all remaining units of that cable sell at the new price, including the units bought earlier.

---

### User Story 4 - See whether the shop actually made money (Priority: P2)

At close of day the shopkeeper opens the dashboard and switches between Today, This Month, and This Year. Each view shows sales, purchases, gross profit, expenses, net profit, cash versus credit sales, outstanding receivables, supplier payables, and items sold. Profit is computed from the cost actually paid for the goods sold, not an estimate, and expenses recorded for the period are subtracted to give net profit.

**Why this priority**: This is the insight the owner is buying, but it is only meaningful once sales, purchases, ledger, and expenses are being captured accurately.

**Independent Test**: With a known set of sales, purchases, and expenses in place, open the dashboard and reports and verify every figure against hand-calculated values. Delivers decision-grade reporting on top of existing data.

**Acceptance Scenarios**:

1. **Given** an item sold at 1,100 that cost 800, **When** gross profit is computed for that line, **Then** it is 300 per unit sold, less any discount applied to that line.
2. **Given** 10 units purchased at 800 of which 5 have been sold, **When** 10 more units are purchased at 850, **Then** all 15 units on hand carry a cost of 850 and subsequent sales compute profit against 850.
3. **Given** a sale recorded when the cost was 800, **When** a later purchase raises the cost to 850, **Then** the earlier sale's recorded profit is unchanged.
2. **Given** a month with 50,000 gross profit and 12,000 of recorded expenses, **When** net profit for the month is viewed, **Then** it is 38,000.
3. **Given** the dashboard, **When** the period toggle is switched between Today, This Month, and This Year, **Then** every figure recalculates for the selected period.
4. **Given** sales across several products, **When** the product-wise profit report is opened, **Then** each product shows total sale value, total cost, and total profit.
5. **Given** any stock change, **When** the stock movement history is viewed, **Then** each movement shows its direction and reason (purchase, sale, return, or adjustment).

---

### User Story 5 - Correct mistakes and returns without breaking the books (Priority: P2)

A customer returns a faulty charger. The shopkeeper records a sale return: the item goes back into stock, the invoice's net amount drops, the customer's outstanding balance is adjusted, and the period's profit no longer counts that sale. The same applies in reverse when the shop returns defective stock to a supplier.

**Why this priority**: Returns happen routinely, and without them stock and balances drift out of true, which destroys trust in every report.

**Independent Test**: Record a sale, then return part of it, and verify stock, invoice net, customer balance, and profit all move by exactly the expected amounts.

**Acceptance Scenarios**:

1. **Given** a credit sale of 2 units where the customer still owes the full amount, **When** 1 unit is returned, **Then** stock increases by 1, the invoice net drops by that line's value, and the customer's balance falls by the same amount.
2. **Given** a fully paid cash sale, **When** an item is returned, **Then** stock increases and the amount owed back to the customer is recorded rather than silently discarded.
3. **Given** a purchase of 50 units, **When** a purchase return of 10 units is recorded, **Then** stock falls by 10 and the supplier payable falls by the cost of those 10 units.
4. **Given** a return of more units than were originally sold or purchased, **When** it is submitted, **Then** it is rejected with a clear message.
5. **Given** a return recorded in a period, **When** that period's profit is viewed, **Then** the returned goods no longer contribute profit.

---

### User Story 6 - Let staff sell without exposing cost and profit (Priority: P2)

The owner hires a salesman. The salesman signs in and can search products, ring up sales, and look up customers, but nowhere — on screen or by any other means — can he see what the shop paid for goods, what it earns, or any financial report. The owner signs in with full access to everything.

**Why this priority**: Cost and margin confidentiality is a firm business requirement, and it must be enforced by the system rather than by hiding buttons.

**Independent Test**: Sign in as each role and verify the permitted and forbidden areas, including direct attempts to reach restricted data outside the normal screens.

**Acceptance Scenarios**:

1. **Given** a user signed in as Staff, **When** they attempt to view purchase price, profit figures, or financial reports by any route, **Then** access is refused.
2. **Given** a user signed in as Staff, **When** they create a sale and look up a customer, **Then** both succeed.
3. **Given** a user signed in as Admin, **When** they open any module, **Then** access is granted, including cost and profit data.
4. **Given** an unauthenticated request for any business data, **When** it is made, **Then** it is refused.

---

### User Story 7 - Get the paperwork to the customer's phone (Priority: P3)

After saving a sale or receiving a ledger payment, the shopkeeper taps "Send on WhatsApp". WhatsApp opens addressed to the customer's stored mobile number with the receipt attached or linked, so the customer has a record without paper.

**Why this priority**: A real convenience and a trust-builder for udhaar customers, but the shop can operate on printed receipts until it exists.

**Independent Test**: Generate an invoice and a payment receipt for a customer with a stored number and verify the share action opens addressed to that number carrying the correct document.

**Acceptance Scenarios**:

1. **Given** an invoice for a customer with a stored mobile number, **When** "Send on WhatsApp" is used, **Then** WhatsApp opens addressed to that number with the receipt document or its link included.
2. **Given** a customer with no mobile number on file, **When** the invoice is viewed, **Then** the send action is unavailable and the reason is shown.
3. **Given** a ledger payment receipt, **When** it is shared, **Then** it carries the payment amount, the remaining balance, and the shop's branding.

---

### User Story 8 - Never lose the shop's records (Priority: P3)

The owner's records are backed up automatically every day without anyone remembering to do it, and the owner can take a backup on demand or restore from one after a hardware failure. Every change to stock or to a customer's balance is traceable to who made it and when.

**Why this priority**: Protects everything the other stories create, but has no value until there is data worth protecting.

**Independent Test**: Trigger a manual backup, restore it into a clean environment, and verify all records are present; then change stock and a balance and verify the audit entries.

**Acceptance Scenarios**:

1. **Given** the system is running, **When** a day passes, **Then** a backup of all business data has been taken without manual action.
2. **Given** an Admin user, **When** they choose "Backup Now", **Then** a backup is produced and confirmed.
3. **Given** a Staff user, **When** they look for backup or restore actions, **Then** they are unavailable.
4. **Given** a valid backup, **When** it is restored, **Then** products, stock, invoices, customers, balances, purchases, and expenses match the backed-up state.
5. **Given** any stock or ledger change, **When** the audit trail is inspected, **Then** it records the user, the change, the value before and after, and the time.

---

### Edge Cases

- A sale is saved at the same moment as another for the last unit in stock: only one can succeed, and stock must never go negative.
- Saving a sale fails partway through: no stock, invoice, or balance change may persist — the shop must never see goods deducted for a sale that was not recorded.
- A discount exceeds the line or order value: the payable total must not go below zero and the entry must be rejected with a clear message.
- A customer pays more than they owe: the overpayment must be handled explicitly (recorded as credit or refund) rather than producing a negative balance by accident.
- A supplier is paid more than is owed: same explicit handling as customer overpayment.
- A sale is entered near midnight or at a month or year boundary: it must fall in exactly one reporting period, consistently, under the shop's local time.
- The same product is purchased at different costs over time: the latest purchase cost replaces the product's cost for all stock on hand, and each sale carries the cost in force when it was made, so a later purchase never alters an earlier sale's recorded profit.
- A purchase is recorded at a cost *lower* than the current cost: the rule applies unchanged and the cost falls for all stock on hand, reporting lower profit on units bought dearer.
- A purchase return is recorded after the cost has since changed: the returned goods leave stock and reduce the supplier payable at the cost of the purchase being returned, and the product's current cost is not silently reverted.
- A cost changes while an unsaved sale is open at the counter: the sale must settle on one cost figure and record it, rather than pricing some lines before and some after the change.
- A product with recorded sales is deleted or renamed: historical invoices and profit figures must remain intact and readable.
- A barcode is scanned that matches no product, or matches more than one: the operator is told clearly rather than silently adding the wrong item.
- A return is attempted against an invoice or purchase that was already fully returned.
- A customer's mobile number is missing or malformed when sharing a receipt.
- A restore is attempted from a corrupt or incomplete backup.

## Requirements *(mandatory)*

### Functional Requirements

**Catalogue and stock**

- **FR-001**: System MUST store each product variant as its own catalogue item with name, category, brand, model, image, barcode/SKU, cost price, wholesale price, retail price, sale price, quantity on hand, minimum stock threshold, and supplier reference.
- **FR-002**: System MUST allow creating, viewing, editing, and deactivating catalogue items, preserving historical records that reference them.
- **FR-003**: System MUST return matching products from a single search input across name, brand, model, category, and barcode.
- **FR-004**: System MUST flag a product as low stock when its quantity is at or below its minimum stock threshold, and MUST provide a low-stock list.
- **FR-005**: System MUST record every stock movement with its direction, quantity, reason (purchase, sale, return, adjustment), resulting quantity, responsible user, and time.
- **FR-006**: System MUST prevent stock from becoming negative through any operation.

**Suppliers and purchasing**

- **FR-007**: System MUST store suppliers with name, contact, address, and a running payable balance.
- **FR-008**: System MUST record purchases (supplier, product, cost price, quantity, date, computed total) and, as a single indivisible operation, increase the product's stock and the supplier's payable balance.
- **FR-009**: System MUST record payments to suppliers, decreasing the payable balance, and MUST require explicit confirmation for any payment exceeding the outstanding payable.
- **FR-010**: System MUST show, per supplier, the history of purchases and payments with the running payable balance.
- **FR-011**: System MUST persist the cost price and quantity of every purchase so cost is always traceable to actual purchase records rather than estimated.
- **FR-011a**: System MUST, on recording a purchase, replace the product's current cost price with that purchase's cost price, and that new cost MUST apply to every unit on hand regardless of what earlier units actually cost. Worked example: 10 units purchased at 800, 5 sold leaving 5 on hand, then 10 units purchased at 850 — all 15 units on hand now carry a cost of 850.
- **FR-011b**: System MUST apply this latest-cost rule to every product; per-product costing methods and batch-level cost tracking are not supported.
- **FR-011c**: System MUST record, against each sale line, the cost price in effect at the moment of sale, so that a later purchase never retroactively changes the profit of an already-recorded sale.
- **FR-011d**: When a purchase is recorded at a cost different from the product's current cost, System MUST prompt the operator to update that product's sale price, and the updated sale price MUST then apply to all remaining stock of that product regardless of what that stock originally cost.

**Selling**

- **FR-012**: Users MUST be able to build a sale from multiple line items, each with product, quantity, unit sale price, and an optional line discount, plus an optional order-level discount.
- **FR-013**: System MUST compute subtotal, total discount, payable total, amount paid, and amount remaining authoritatively from the recorded line items and discounts, ignoring any totals supplied by the operator device.
- **FR-014**: System MUST support the payment methods Cash, Bank Transfer, JazzCash, EasyPaisa, Raast, Credit (udhaar), and a partial split of cash plus credit.
- **FR-015**: System MUST, on saving a sale, as a single indivisible operation, record the invoice and its lines, decrease stock for each line, and increase the customer's outstanding balance by any unpaid amount.
- **FR-016**: System MUST reject a sale whose line quantity exceeds available stock, saving nothing.
- **FR-017**: System MUST require a customer record for any sale that is not fully paid, and MUST allow creating one inline from name and mobile number during the sale.
- **FR-018**: Users MUST be able to look up products during a sale by search text or by scanning a barcode.

**Customer ledger**

- **FR-019**: System MUST store customers with name, mobile number, and optional address.
- **FR-020**: System MUST present, per customer, a chronological ledger of entries showing bill amount, paid amount, and running balance, where each entry's balance equals the previous balance plus the new bill minus the new payment.
- **FR-021**: Users MUST be able to record a payment received from a customer, decreasing the outstanding balance and producing a payment receipt.
- **FR-022**: System MUST require explicit confirmation for any customer payment exceeding the outstanding balance, and MUST NOT silently produce a negative balance.
- **FR-023**: System MUST show, per customer, total purchased, total paid, total outstanding, and the full history of invoices and payments.

**Returns**

- **FR-024**: System MUST record sale returns that, as a single indivisible operation, increase stock, reduce the original invoice's net amount, and adjust the customer's outstanding balance.
- **FR-025**: System MUST record purchase returns that, as a single indivisible operation, decrease stock and reduce the supplier's payable balance.
- **FR-026**: System MUST reject returns exceeding the originally sold or purchased quantity.
- **FR-027**: System MUST exclude returned goods from the profit of the affected period.
- **FR-028**: System MUST record, for a return against a fully paid sale, the amount owed back to the customer.

**Expenses**

- **FR-029**: System MUST record expenses with category, amount, date, and note, using an extensible category list initially containing rent, electricity, internet, transport, salary, and other.
- **FR-030**: System MUST subtract expenses recorded within a period from that period's gross profit when reporting net profit.

**Profit and reporting**

- **FR-031**: System MUST compute gross profit per sale line as (sale price minus the cost price recorded against that line) times quantity, less that line's discount.
- **FR-032**: System MUST compute net profit for a period as total gross profit for the period minus total expenses for the period.
- **FR-033**: System MUST report profit rolled up by day, week, month, and year, and broken down by product showing total sale value, total cost, and total profit.
- **FR-034**: System MUST assign every transaction to exactly one reporting period based on the shop's local time.
- **FR-035**: System MUST provide a dashboard with a Today / This Month / This Year toggle showing total sales, total purchases, gross profit, expenses, net profit, cash sales, credit sales, total customer receivables outstanding, total supplier payables outstanding, count of items sold, and the low-stock list.
- **FR-036**: System MUST provide reports for daily, monthly, and annual sales; total purchases; gross and net profit; product-wise profit; current stock; low stock; stock movement history; customer outstanding; supplier payable; and expenses.
- **FR-037**: System MUST allow list and report views to be filtered by date range.

**Users, roles and access**

- **FR-038**: System MUST authenticate users before granting access to any business data.
- **FR-039**: System MUST support an Admin role with access to all modules including cost prices and profit, and a Staff role able to create sales and view products and customers.
- **FR-040**: System MUST refuse Staff access to cost prices, profit figures, and financial reports wherever that data is requested, not merely by hiding it in the interface.
- **FR-041**: System MUST record an audit entry for every stock and ledger change capturing the user, the field changed, the value before and after, and the time.

**Documents and sharing**

- **FR-042**: System MUST produce a branded printable and downloadable receipt for every sale, carrying the shop name "Moiz Mobile & Corporation, Danwran Lodhran" and contact details, invoice number, date and time, customer name and mobile, line items with quantity, rate and discount, subtotal, total, amount paid, amount remaining, and a thank-you footer.
- **FR-043**: System MUST produce an equivalent branded receipt for every customer ledger payment.
- **FR-044**: Users MUST be able to send an invoice or payment receipt to the customer's stored mobile number via WhatsApp, and the action MUST be unavailable with a stated reason when no number is on file.

**Continuity**

- **FR-045**: System MUST take a complete backup of all business data automatically once per day without manual action.
- **FR-046**: Admin users MUST be able to take a backup on demand and restore from a backup; these actions MUST be unavailable to Staff.
- **FR-047**: System MUST restore products, stock, invoices, customers, balances, purchases, expenses, and audit history to the state captured in the backup.

**Data handling**

- **FR-048**: System MUST return list results in pages for products, customers, invoices, and stock history.
- **FR-049**: System MUST validate all input and return consistent, structured messages describing what was rejected and why.
- **FR-050**: System MUST ensure that any operation touching stock, balances, or invoices either completes fully or leaves no trace of a partial change.

### Key Entities

- **Product**: One distinct sellable variant. Identity (name, category, brand, model, barcode/SKU, image), pricing (cost, wholesale, retail, sale), stock (quantity on hand, minimum threshold), and a reference to its usual supplier.
- **Supplier**: A party the shop buys from. Name, contact, address, and running payable balance; owns purchases and supplier payments.
- **Purchase**: Goods received from a supplier. Supplier, product, cost price, quantity, date, total; raises stock and payable.
- **Supplier Payment**: Money paid to a supplier; reduces payable.
- **Customer**: A person the shop sells to. Name, mobile number, optional address, running outstanding balance; owns invoices and payments.
- **Invoice**: One sale. Number, date and time, customer, payment method, order discount, subtotal, total, amount paid, amount remaining, net amount after returns.
- **Invoice Line**: One product on an invoice. Product, quantity, unit sale price, line discount, and the cost attributed to the goods sold.
- **Customer Payment**: Money received from a customer against their balance; produces a receipt.
- **Ledger Entry**: One chronological movement in a customer's account showing bill, paid, and resulting running balance.
- **Sale Return / Purchase Return**: A reversal against an original invoice or purchase, with quantities and values returned.
- **Expense**: An outgoing cost with category, amount, date, and note.
- **Stock Movement**: An in or out change to a product's quantity with its reason, resulting quantity, user, and time.
- **User**: Someone who signs in, with a role of Admin or Staff.
- **Audit Entry**: A record of who changed what stock or ledger value, from what to what, and when.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A salesman can complete a typical three-item counter sale — from first product lookup to a produced receipt — in under 60 seconds.
- **SC-002**: Finding any product by name, brand, model, category, or barcode takes a single search action and returns results in under 2 seconds with a catalogue of at least 5,000 items.
- **SC-003**: Recorded stock counts match a physical shelf count for 100% of audited items over a one-month trial, given all movements were entered.
- **SC-004**: Every customer's outstanding balance shown by the system equals an independent hand calculation of their bills minus payments, for 100% of customers.
- **SC-005**: Reported gross and net profit for any period reconcile to hand-calculated figures from the same underlying sales, costs, and expenses, with zero discrepancy.
- **SC-006**: No sale, purchase, return, or payment ever leaves stock or balances partially updated, across 100% of interrupted or failed attempts.
- **SC-007**: A Staff user cannot obtain cost price, profit, or financial report data through any available route in 100% of attempts.
- **SC-008**: The owner can see the day's sales, profit, receivables, and low-stock items within 10 seconds of opening the dashboard.
- **SC-009**: The shopkeeper can identify every item needing reorder in a single view without inspecting products individually.
- **SC-010**: A customer receives their invoice or payment receipt on WhatsApp within 30 seconds of the sale being saved.
- **SC-011**: A backup exists for every calendar day the system was in use, with no missing days over a one-month period.
- **SC-012**: Restoring from the most recent backup reproduces all business records with zero loss of transactions committed before the backup was taken.
- **SC-013**: Every stock and balance change can be traced to the user who made it and the time it happened, for 100% of changes.
- **SC-014**: Staff require no more than 30 minutes of instruction to independently ring up sales and record customer payments.

## Assumptions

- The shop operates from a single location; multi-branch stock and consolidated multi-shop reporting are out of scope.
- All amounts are in Pakistani Rupees and all reporting periods follow Pakistan local time; multi-currency is out of scope.
- The system is used on desktop and tablet at the counter; a dedicated phone-sized layout is not required for v1.
- Two roles (Admin and Staff) are sufficient; finer-grained custom permission sets are out of scope for v1.
- Barcode scanning is performed by hardware that enters the code as keyboard input into the search field; no special scanner integration is required.
- WhatsApp delivery uses the customer's stored mobile number and the operator's own WhatsApp session, so the operator confirms each send; unattended automated messaging is out of scope for v1.
- Backups are taken and stored by the hosting environment's scheduled job; off-site replication policy is the owner's operational choice.
- Standard retail-domain retention applies: all transactional records are retained indefinitely; nothing is auto-purged.
- Sale prices may be overridden per line at the counter within the shopkeeper's discretion; the entered price is what the sale records.
- Tax and GST invoicing are out of scope for v1; totals are pre-tax amounts as the shop trades today.
- Existing paper records are not migrated automatically; opening balances for customers and suppliers are entered manually at go-live.
- Costing method is latest purchase cost, confirmed by the owner: the most recent purchase cost replaces the cost of all stock on hand and is used for profit on every subsequent sale. Weighted average and batch-specific (FIFO) costing are both out of scope.
- The owner accepts that during a price rise this reports less profit than the shop actually banked on units bought at the older, lower cost, and correspondingly more profit during a price fall. This is the intended behaviour: profit is measured against what it costs to replace the goods sold today.
- Old stock is sold at the current sale price, not the price that applied when it was bought. When a purchase cost rises, the shopkeeper repricing that product applies to all remaining units of it.
- Sale price is set by the shopkeeper rather than derived automatically from cost by a fixed margin; the system prompts on a cost change but does not compute the new price.
