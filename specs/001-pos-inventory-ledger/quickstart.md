# Quickstart & Validation Guide

**Feature**: `001-pos-inventory-ledger` · **Date**: 2026-09-09

How to run the system locally and prove, by hand, that the money rules in
[spec.md](./spec.md) actually hold. Schema details are in [data-model.md](./data-model.md);
endpoint shapes in [contracts/openapi.yaml](./contracts/openapi.yaml).

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 8.0+ | `dotnet --version` |
| Node.js | 20 LTS+ | `node --version` |
| MySQL | 8.0+ | Local service or Docker; `mysqldump` and `mysql` must be on `PATH` for backup/restore |

## Setup

```bash
# 1. Databases — one for the app, one the integration tests can freely drop.
#    Either run docs/create-databases.sql in MySQL Workbench (File > Open SQL Script,
#    then Execute All), or from a shell:
mysql -u root -p < docs/create-databases.sql

# 2. Local secrets — never commit these
cd backend/src/MoizPos.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Database=moizpos;Uid=root;Pwd=<password>;"
dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>"

# 3. Schema
cd ../../../backend
dotnet run --project src/MoizPos.Migrator

# 4. Frontend
cd ../frontend
npm install
cp .env.example .env      # sets VITE_API_BASE_URL=http://localhost:5080/api
```

## Run

```bash
# Terminal 1
cd backend && dotnet run --project src/MoizPos.Api      # http://localhost:5080

# Terminal 2
cd frontend && npm run dev                              # http://localhost:5173
```

Seeded first Admin: `admin` / `Admin@123` — **change this before the shop uses it.**

## Test

```bash
cd backend  && dotnet test                    # unit + integration + architecture
cd frontend && npm run test                   # Vitest
cd frontend && npm run test -- --coverage
```

Integration tests create and drop their own schema in `moizpos_test`. They will fail fast if
MySQL is unreachable — that is intentional; the transactional guarantees in Principle IV cannot
be verified against a fake.

---

## Validation scenarios

Each scenario proves a specific spec requirement by hand. Run them in order against a fresh
database — later ones depend on earlier state.

### V1 — Cash sale decrements stock (US1, FR-015, FR-016)

1. Sign in as `admin`. Create a supplier "Ali Traders".
2. Create a product: *Type-C Braided 2m*, category Cables, cost **800**, sale price **1,100**,
   quantity **10**, minimum threshold **3**.
3. Open POS, search "braided", add it, quantity **2**, no discount, method **Cash**.
4. Save.

**Expect**: total **2,200**, remaining **0**, product quantity now **8**, one `Sale` row in the
product's stock movement history.

**Then attempt to sell 20 units.** Expect rejection with `INSUFFICIENT_STOCK`, quantity still
**8**, and no new invoice — proving nothing partially committed.

### V2 — Credit sale and ledger running balance (US2, FR-020, FR-021)

1. In POS, add goods totalling **3,000**. Choose **Partial**, amount paid **1,000**.
2. Leave the customer blank and save → expect `CUSTOMER_REQUIRED`.
3. Quick-create customer "Bilal" with a mobile number, save again.
4. Open Bilal's ledger.

**Expect**: one entry — bill **3,000**, paid **1,000**, balance **2,000**; profile outstanding
**2,000**.

5. Receive a payment of **1,500**.

**Expect**: a second entry — paid **1,500**, balance **500**. This is the worked example from the
spec and must match exactly.

6. Attempt a payment of **10,000** without confirmation → expect
`OVERPAYMENT_NOT_CONFIRMED`, balance unchanged at **500**.

### V3 — Purchase overwrites cost for all stock (US3, FR-011a, FR-011d)

This is the rule the owner specifically chose. Verify it deliberately.

1. Note the product's current quantity (**8** after V1) and cost (**800**).
2. Sell **3** more so **5** remain.
3. Record a purchase: same product, supplier Ali Traders, **10** units at unit cost **850**,
   and supply a new sale price of **1,200**.

**Expect**:
- quantity **15**
- **`costPrice` is now 850 — not 825, and not a blend.** All 15 units carry 850.
- supplier payable **8,500**
- sale price **1,200**, applying to every remaining unit including the 5 bought at 800
- a `Purchase` stock movement and audit rows for both the quantity and cost changes

4. Open an earlier invoice from V1 and check its profit.

**Expect**: unchanged, still computed against **800**. A later purchase must never rewrite
historical profit (FR-011c).

### V4 — Low stock (FR-004)

Sell down until quantity ≤ the minimum threshold.

**Expect**: the low-stock badge appears in the product list, the item shows in the dashboard
low-stock panel, and `GET /api/products?lowStockOnly=true` returns it.

### V5 — Sale return (US5, FR-024, FR-027)

1. Return **1** unit from the V2 credit invoice.

**Expect**: stock **+1**; that invoice's `netAmount` down by the line value; Bilal's balance down
by the same amount with a `SaleReturn` ledger entry; the period's profit no longer counts the
returned unit.

2. Attempt to return more units than were sold → expect `RETURN_EXCEEDS_ORIGINAL`.

### V6 — Profit and net profit (US4, FR-031, FR-032)

1. Record an expense: Electricity, **12,000**, dated today.
2. Open the dashboard on **Today**.

**Expect**: gross profit equals the sum of `(sale price − recorded line cost) × qty − discount`
across today's sales; net profit equals gross profit minus **12,000**. Check one line by hand —
a unit sold at 1,100 whose recorded cost was 800 contributes **300**.

3. Toggle to **This Month** and **This Year**.

**Expect**: every figure recalculates; today's sale appears in exactly one bucket per period.

### V7 — Staff cannot see cost or profit (US6, FR-040)

1. As Admin, create a Staff user. Sign in as them in a private window.
2. Confirm the UI shows no cost, profit, reports, purchases, suppliers or expenses.
3. **Now bypass the UI** — this is the part that matters:

```bash
TOKEN=<staff access token>
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5080/api/dashboard?period=Today
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5080/api/reports/profit
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5080/api/purchases
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5080/api/products/1
```

**Expect**: the first three return **403 `FORBIDDEN`**. The fourth returns **200** but the JSON
must contain **no `costPrice` key at all** — absent, not null. Grep it:

```bash
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5080/api/products/1 | grep -i cost
# must produce no output
```

4. Confirm a Staff user can still create a sale and receive a customer payment.

### V8 — Atomicity under failure (FR-050, SC-006)

The integration suite covers this; verify it ran:

```bash
cd backend && dotnet test --filter "FullyQualifiedName~Transaction"
```

**Expect**: tests that force a failure midway through `CreateInvoice`, `RecordPurchase` and
`SaleReturn` assert that afterwards stock, balances, `stock_movements`, `ledger_entries` and
`audit_entries` are **all** unchanged. No partial state, and no audit row claiming a change that
was rolled back.

### V9 — PDF and WhatsApp (US7, FR-042, FR-044)

1. Open an invoice → **Download PDF**.

**Expect**: header "Moiz Mobile & Corporation, Danwran Lodhran", invoice number, date/time,
customer name and mobile, line items with qty/rate/discount, subtotal, total, paid, remaining,
thank-you footer.

2. Click **Send on WhatsApp**.

**Expect**: WhatsApp opens addressed to the customer's number, with a message containing a
**link** to the receipt. It will not carry an attached file — `wa.me` cannot do that (see R2 in
[research.md](./research.md)). Open the link in a private window and confirm the PDF loads
without signing in.

3. Open an invoice for a customer with no mobile number.

**Expect**: the send action is disabled with a stated reason.

### V10 — Backup and restore (US8, FR-045–047)

```bash
curl -X POST -H "Authorization: Bearer $ADMIN_TOKEN" http://localhost:5080/api/admin/backups
curl -H "Authorization: Bearer $ADMIN_TOKEN" http://localhost:5080/api/admin/backups
```

**Expect**: a timestamped compressed dump appears in the configured backup directory.

Then restore into a scratch database and compare row counts for `products`, `invoices`,
`invoice_items`, `customers`, `ledger_entries`, `purchases` and `expenses`. All must match.

**Expect** a Staff token calling either backup endpoint to receive **403**.

### V11 — Audit trail (FR-041)

After running V1–V5, open `GET /api/admin/audit`.

**Expect**: an entry for every stock and balance change, each naming the user, the field, the
value before, the value after, and the time. Cross-check one sale against its stock movement.

---

## Definition of done for this feature

Per Constitution Principle VI, all three must hold:

- [ ] `dotnet test` green — unit, integration and architecture suites
- [ ] `npm run test` green
- [ ] No previously passing test broken
- [ ] V1–V11 above all pass by hand
- [ ] `curl … | grep -i cost` in V7 produces no output for a Staff token
