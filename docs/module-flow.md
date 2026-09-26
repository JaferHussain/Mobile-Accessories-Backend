# Suppliers → Purchases → Products → Selling

How the four modules that move stock and money fit together: what each one owns, what it changes
when you use it, and exactly where one hands off to the next.

Read `docs/how-it-works.md` first if you want the whole system. This document is the *spine* — the
path a physical item takes from the supplier's van to the customer's hand, and the path the money
takes in the other direction.

---

## 1. The one-paragraph version

A **supplier** is who you buy from. A **purchase** is one delivery from that supplier, and it is
the only thing that raises stock and sets cost. A **product** is the catalogue entry a purchase
raises stock *against* — it must exist first. A **sale** lowers that stock and snapshots the cost,
so profit can never be rewritten later.

```
   SUPPLIER ──buy from──► PURCHASE ──raises stock, sets cost──► PRODUCT ──sold──► INVOICE
       ▲                      │                                    ▲                 │
       │                      │                                    │                 │
       └──owed money──────────┘                          must exist first     snapshots cost
          (payable_balance)                              (created by hand)   (unit_cost_price)
```

**The product is the hub.** Purchases and sales never talk to each other; they both talk to the
product row. That is the whole architecture in one sentence.

---

## 2. What each module owns

| Module | Owns | Who can use it |
|---|---|---|
| **Suppliers** | `suppliers` — name, contact, bank details, and `payable_balance` | Admin only |
| **Purchases** | `purchases`, `supplier_payments`, `purchase_returns` | Admin only |
| **Products** | `products`, `categories`, `brands`, `stock_movements` | Everyone reads; Admin writes |
| **Selling (POS)** | `invoices`, `invoice_items`, `sale_returns` | Everyone |

Purchases and Suppliers are **Admin only** because they reveal **cost** — what you pay for goods.
The salesman sees what things sell for and never what they cost. That is enforced by
`[Authorize(Policy = Policies.AdminOnly)]` on the endpoints *and* by an architecture test that
fails the build if a Staff-reachable endpoint ever declares a cost field.

---

## 3. The setup order, and why it cannot be different

```
1. Categories   ─┐
2. Brands        ├─ a product must name both
3. Suppliers     ─── a purchase must name one
4. Products      ─── a purchase and a sale must name one
5. Purchases     ─── raises stock
6. Selling       ─── lowers stock
```

Each step is blocked by the one above it — not by convention, but by a foreign key:

- A **product** cannot be saved without a `category_id` and a `brand_id`. Both are dropdowns fed
  from the Categories and Brands screens; neither is free text.
- A **purchase** cannot be recorded without an existing `supplier_id` **and** an existing
  `product_id`. You cannot "buy something new" in one step — the catalogue entry comes first.
- A **sale** cannot be recorded without an existing product that has stock.

> **The shortcut that exists:** the Purchases screen has a **Create product** button. When a search
> finds nothing, it opens *the same product form the Products screen uses*, creates the product,
> then drops you straight into the purchase for it. It is a shortcut through the ordering, not an
> exception to it — the product is genuinely created first, in its own call.

---

## 4. Adding a product — what it does and does not do

*Sell → Products → Add product.* Admin only.

You provide: name, **category**, **brand**, model, barcode, cost price, wholesale price, retail
price, sale price, opening quantity, reorder threshold, optional supplier, optional photograph.

**What matters most is what happens on the *second* save:**

| Field | On create | On edit |
|---|---|---|
| Name, category, brand, model, barcode | Saved | **Editable** |
| Sale / wholesale / retail price | Saved | **Editable** |
| Reorder threshold, supplier, picture | Saved | **Editable** |
| **`cost_price`** | Saved as the opening cost | **Ignored — carried through unchanged** |
| **`quantity_on_hand`** | Saved as opening stock | **Ignored — carried through unchanged** |

`ProductService.UpdateAsync` reads the existing row and copies `CostPrice` and `QuantityOnHand`
onto the object it saves. The repository does not write those two columns at all.

**Why:** if the edit form could set stock, every stocktake would be an untraceable edit and the
`stock_movements` trail would stop reconciling. If it could set cost, past profit would change
whenever someone corrected a typo. So after creation there are exactly **four** ways stock moves
and **one** way cost moves:

```
stock_movements.reason ∈ { Purchase, Sale, SaleReturn, PurchaseReturn, Adjustment }
cost_price changes ONLY on Purchase
```

`Adjustment` is the deliberate escape hatch — *Products → adjust stock* — and it demands a note and
records who did it.

---

## 5. Suppliers — a running account, never a typed opinion

*Purchasing → Suppliers.* Admin only.

A supplier row holds contact and bank details (grouped together because they are read as a set when
paying an invoice) plus one number that matters: **`payable_balance`** — what you owe them today.

That number is **derived and asserted, never typed**:

```
payable_balance  =  SUM(purchases.total)
                 −  SUM(supplier_payments.amount)
                 −  SUM(purchase_returns.total)
```

An integration test re-computes this for **every supplier in the database** and fails if any stored
balance disagrees. No screen anywhere lets you set a supplier's balance directly — it moves only
because one of those three things happened.

**Paying a supplier** (*Suppliers → record payment*) subtracts from the payable. Paying **more**
than is owed is refused unless the caller explicitly confirms it: an overpayment is a real thing
(an advance), but it must be deliberate, never a typo that quietly creates a negative balance.

> **There is no supplier "opening balance".** Customers have one; suppliers deliberately do not.
> The payable has the asserted invariant above, and an opening figure belongs to none of its three
> terms — it would break the invariant the moment it was added. Doing it properly needs a ledger
> and correction-by-delta, like customer opening balances. It is its own feature, not a column.

---

## 6. Recording a purchase — the six things one transaction does

*Purchasing → Purchases.* Admin only. You provide: **supplier**, **product**, **unit cost**,
**quantity**, an optional date, and optionally a **new sale price**.

`PurchaseService.RecordPurchaseAsync` locks the product row, then the supplier row — **always in
that order**, so two purchases running at once cannot deadlock — then does six things:

```
1. INSERT the purchase            (supplier, product, unit cost, qty, total)
2. Stock UP        quantity_on_hand += qty
3. Cost REPLACED   cost_price = this purchase's unit cost      ← see §7
4. Payable UP      supplier.payable_balance += total
5. APPEND a stock_movement        reason = Purchase
6. AUDIT every value that moved   old → new, who, when
```

All six, or none. If step 4 fails, step 2 never happened.

The optional **new sale price** is applied here too, and it applies to *everything on the shelf* —
including units bought earlier at a different cost. That is the shop's second rule: old stock sells
at today's price.

---

## 7. The costing rule — the single most important section here

> **The latest purchase cost replaces the cost of every unit on hand. It is not an average.**

The owner's own worked example:

```
Buy 10 at 800   →  10 units on hand, all costed 800
Sell 5          →   5 units on hand, all costed 800
Buy 10 at 850   →  15 units on hand, all costed  850    ← NOT 825
```

`StockRules.NextCostPrice` ignores the previous cost entirely — it takes an argument for it and
explicitly discards it, with a comment saying why. A unit test asserts the answer is **not** 825.

**Why the owner chose it:** profit is measured against what it costs to *replace* the goods today,
not against what was historically paid. If you sell that charger, you must buy another at 850.

**The trap it creates, and the guard.** If past sales' profit were computed against
`products.cost_price`, buying stock today would silently rewrite last month's profit. So **every
sale snapshots the cost at the moment of sale** into `invoice_items.unit_cost_price`, and every
profit report joins to *that*, never to the product. The five sold at 800 stay costed at 800
forever.

```
products.cost_price            →  "what does it cost me to replace this, today?"
invoice_items.unit_cost_price  →  "what did this specific sale actually cost me?"
```

Confusing the two is the most damaging mistake available in this codebase.

**A purchase return does not put the cost back.** If you bought at 850 and sent it back, 850 is
still the last price you were quoted — the shop's replacement cost has not changed just because
this particular box went back to the van.

---

## 8. Making a sale — how it consumes what purchasing produced

*Sell → New sale.* Everyone.

### At the counter
- **Scan a barcode** → straight into the cart. Unambiguous, so no confirming tap.
- **Type a name** → up to 8 result cards, and **nothing is added until Add is pressed** — even when
  only one matched. "The only thing matching what I typed" is not "the thing in the customer's
  hand".
- **Retail or Wholesale** is chosen once at the top of the sale, and **re-prices every line already
  in the cart**.

### Which price is quoted
Decided **on the server**, never by the browser:

```sql
SaleType = Wholesale  →  CASE WHEN wholesale_price > 0 THEN wholesale_price ELSE sale_price END
SaleType = Retail     →  sale_price
```

This is why a Staff user never receives a `wholesalePrice` field at all: the salesman is told the
one price that applies rather than handed the price list. `saleType` must be threaded through
**every** product read the counter makes — search, get-by-id and get-by-barcode. Missing it on any
one of them silently re-quotes the counter price; that bug shipped once.

### What the server does on save
`InvoiceService.CreateAsync` — one transaction, in a deliberate order:

```
 1  refuse an empty sale; refuse the same product twice
 2  quick-create the customer if one was typed   (committed first, on purpose)
 3  BEGIN
 4  replayed idempotency key? → return the original sale, do not sell twice
 5  SELECT ... FOR UPDATE every product row, ORDERED BY ID
 6  recompute every total from scratch          ← the client's figures are discarded
 7  check stock for EVERY line before writing anything
 8  a sale that still owes money must name a customer
 9  only an Admin may leave money outstanding
10  a cash sale may carry no payment reference
11  ── first write ──
    invoice · lines (SNAPSHOTTING COST) · stock down · stock_movements ·
    audit rows · customer balance · ledger entry
12  COMMIT
```

Steps 7–10 all sit **before the first write**, so a refusal leaves no invoice, no stock movement and
no balance change. A test named `A_refused_credit_sale_changes_absolutely_nothing` asserts all four
at once.

Locks are taken **in id order** (step 5), so two tills fighting over the last unit resolve to one
success and one clean refusal rather than a deadlock or an oversell.

---

## 9. The four links, stated exactly

Everything above reduces to four hand-offs. These are the joints where the modules touch.

### Link 1 — Supplier ←→ Purchase
`purchases.supplier_id`. A purchase raises `suppliers.payable_balance` by its total. A supplier
payment lowers it. A purchase return lowers it. Nothing else touches it, and an invariant test
proves it for every supplier.

### Link 2 — Purchase → Product
`purchases.product_id`. The purchase raises `products.quantity_on_hand` and **overwrites**
`products.cost_price`. This is the **only** place cost is ever written.

### Link 3 — Product → Sale
`invoice_items.product_id`. The sale lowers `quantity_on_hand` and **copies** the product's current
`cost_price` into `invoice_items.unit_cost_price`, freezing it. The sale reads the product; it
never writes back to its cost.

### Link 4 — every movement → the audit trail
Every quantity change writes a `stock_movements` row carrying its reason and the resulting quantity,
so the current stock figure can always be explained by replaying the movements.

```
                    products.quantity_on_hand
                              ▲
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
   Purchase (+)          Sale (−)            Adjustment (±)
   PurchaseReturn (−)    SaleReturn (+)
```

---

## 10. Following one charger all the way through

| # | You do | Stock | `cost_price` | Supplier payable | Money |
|---|---|---|---|---|---|
| 1 | Add supplier *Al-Rehman* | — | — | 0 | — |
| 2 | Add category *Chargers*, brand *Vivo* | — | — | 0 | — |
| 3 | Add product *Charger 20W*, opening 0, sale price 1,100 | **0** | 0 | 0 | — |
| 4 | Purchase 10 @ 800 from Al-Rehman | **10** | **800** | **8,000** | — |
| 5 | Sell 5 @ 1,100 cash | **5** | 800 | 8,000 | +5,500 in drawer |
| 6 | Purchase 10 @ 850 | **15** | **850** ← not 825 | **16,500** | — |
| 7 | Pay Al-Rehman 10,000 cash | 15 | 850 | **6,500** | −10,000 from drawer |
| 8 | Customer returns 1 of the 5 sold | **16** | 850 | 6,500 | −1,100 refund |

At step 6, the profit on the five sold at step 5 is **still 1,100 − 800**, because those lines
snapshotted 800. The 850 applies only to sales made from here on. That is the entire reason
`unit_cost_price` exists.

At step 8, the refund is what the customer *paid*, not what the line was billed at — if the sale
carried a discount, the refund is the discounted amount, and the screen shows the customer all
three figures (billed, discount, refunded).

---

## 11. Where the money ends up

The four modules feed several different questions, and it is worth knowing which answers which.

| Question | Read from | Screen |
|---|---|---|
| What did we sell? | `invoices.net_amount` | Dashboard, Reports → Sales |
| What did we make? | `invoice_items` (sale price − **snapshotted** cost) | Reports → Profit |
| What do we owe? | `suppliers.payable_balance` | Reports → Payables |
| What are we owed? | `customers.outstanding_balance` | Reports → Receivables |
| Is the cash right? | Day close, against the physical drawer | Money → Day close |

**Day close is the only one that checks physical reality.** Every other figure reconciles against
itself — a cash sale can be recorded perfectly and the notes still go missing, and no report will
ever disagree. Counting the drawer against what the day took is the only thing that surfaces it.

Note that **cash paid to a supplier** (step 7 above) is one of its lines: hand over 10,000 from the
till and the evening's expected count is 10,000 lower, legitimately. Leaving that out reported a
phantom short — a real bug, fixed by giving supplier cash its own line.

---

## 12. The traps that live at these joints

| Trap | What breaks |
|---|---|
| Joining a profit query to `products.cost_price` | Every past month's profit rewrites itself whenever stock is bought. Use `invoice_items.unit_cost_price`. |
| "Fixing" latest-cost into a weighted average | Breaks the owner's deliberate rule. The answer is 850, not 825 — ask before changing it. |
| Letting the product edit form write stock or cost | The `stock_movements` trail stops reconciling and history becomes editable. Both are carried through unchanged on update. |
| Deciding "is this credit?" from the request | The client controls `amountPaid` and `paymentMethod`. Read the server's recomputed `AmountRemaining`. |
| Two cart lines for one product | Each checks stock against the same locked row and can oversell. The server refuses duplicates; the POS merges them. |
| Omitting `saleType` on any product read | The counter silently re-quotes retail prices on a wholesale sale. |
| Adding a supplier "opening balance" column | It belongs to none of the three terms of the payable invariant, and breaks it immediately. |
| Locking rows in varying order | Concurrent writes deadlock. Always order by id. |

Every one of these caused a real bug during the build. `CLAUDE.md` carries the full table.
