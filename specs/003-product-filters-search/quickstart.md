# Quickstart: Product Filters & Forgiving Search

**Feature**: 003-product-filters-search

End-to-end checks against a live system, run after `/speckit-implement`. Each maps to a success
criterion or requirement.

Prerequisites: MySQL running, API on `http://localhost:5080`, UI on `http://localhost:5173`.
Sign-ins: `admin` (Admin) and `salesman` (Staff).

> **Stop the other API first** — Visual Studio and `dotnet run` cannot both hold the build output.
> `taskkill /IM MoizPos.Api.exe /F`

---

## Setup

```bash
dotnet run --project backend/src/MoizPos.Migrator     # applies 0016
dotnet run --project backend/src/MoizPos.Api
```

```sql
SHOW COLUMNS FROM moizpos.brands LIKE 'is_local';   -- tinyint(1), NOT NULL, default 0
SELECT COUNT(*) FROM moizpos.brands WHERE is_local = TRUE;   -- 0: nothing is local until marked
```

### Test catalogue

As `admin`, create these so every check below has something to find. Use the Products,
Categories and Brands screens, or the API.

| Product | Brand | Category | Brand is local |
|---|---|---|---|
| Type-C Braided Cable | Baseus | Cables | no |
| Charger 20W Fast | Oppo | Chargers | no |
| Charger 18W | Samsung | Chargers | no |
| Earbuds Basic | Faster | Earbuds | **yes** |
| Micro USB Cable | *(none)* | Cables | — |

---

## Search (US1)

### V1 — Every spelling of Type-C (SC-022, FR-077)

Search each of: `c type`, `type c`, `type-c`, `typec`, `Type C`, `TYPE-C`.

**Expect**: every one lists **Type-C Braided Cable**.

### V2 — Words matching different details (FR-075, FR-076)

| Search | Expect |
|---|---|
| `oppo` | Charger 20W Fast |
| `charger` | both chargers — singular finds category "Chargers" (FR-078) |
| `chargers` | both chargers — plural finds name "Charger …" |
| `oppo charger` | **only** Charger 20W Fast |
| `charger oppo` | the same — order is irrelevant |
| `baseus cable` | Type-C Braided Cable |

### V3 — Adding a word narrows, never widens (SC-023, FR-074)

`charger` lists two products. `charger samsung` lists **one**. `charger xyz` lists **none**, with
the "no products match" message.

### V4 — Numbers and punctuation (edge cases)

`20w`, `20 w` and `20-W` each find Charger 20W Fast. `  type   c  ` (extra spaces) finds the
Type-C cable.

### V5 — One-letter searches (FR-079)

On the Products screen type `c`. **Expect**: a hint "Type at least 2 letters to search", and the
list is not replaced. Type `c type`. **Expect**: the cable is found.

Via the API:

```
GET /api/products?search=c        → 400 VALIDATION_FAILED
GET /api/products?search=c%20t    → 400 (every word is one letter)
GET /api/products?search=c%20type → 200
```

### V6 — Barcode still exact (FR-080)

Scan (or type) a product's full barcode into the search box. **Expect**: exactly that product.

### V7 — The POS and Purchases benefit too (research R6)

- **POS**, as `salesman`: type `c type` and press Enter. **Expect**: Type-C Braided Cable is added
  to the cart. Type `c` and press Enter. **Expect**: "No product found", not an error.
- **Purchases**, as `admin`: search the product picker for `oppo charger`. **Expect**: Charger 20W
  Fast offered.

---

## Filters (US2)

### V8 — Each filter on its own (FR-082)

| Filter | Expect |
|---|---|
| Brand = Oppo | Charger 20W Fast only |
| Category = Chargers | both chargers |
| Category = Cables | Type-C Braided Cable and Micro USB Cable |

### V9 — Combined (FR-083, SC-024)

Brand = Samsung **and** Category = Chargers → **Charger 18W** only, reached in two selections with
no typing.

Then type `20w` as well → **no products**, with the explicit message (FR-086, SC-026).

### V10 — Clear filters (FR-084)

With Brand, Category and a search set, press **Clear filters**. **Expect**: both filters reset, the
full list returns, and the typed search **stays** in the box.

### V11 — Lists offer only active names, alphabetically (FR-085)

Retire a category on the Categories screen. Open the Category filter. **Expect**: it is gone from the
list, and the rest are A→Z. Products already in it still appear with no filter chosen.

---

## Local brands (US3)

### V12 — Nothing local until marked (FR-087a)

Tick **Local brands only** before marking anything. **Expect**: no products, with the explicit
message.

### V13 — Marking a brand local (SC-027)

On **Brands**, edit **Faster** and mark it Local. Time it — under 30 seconds.

Return to Products and tick **Local brands only**. **Expect**: Earbuds Basic only, immediately.

### V14 — Unbranded is not local (FR-087)

With **Local brands only** ticked, **Micro USB Cable** (no brand) is **not** listed.

### V15 — Local combines with the rest (FR-088)

| Local only + | Expect |
|---|---|
| Category = Earbuds | Earbuds Basic |
| Category = Chargers | none, with message |
| search `earbuds` | Earbuds Basic |
| Brand = Oppo (an imported brand) | none, with message |

---

## Both users (FR-089, FR-090)

### V16 — Salesman sees the same list, and no more

As `salesman`, repeat V2 and V9. **Expect**: identical products to the admin, and no cost column.

```bash
curl -s "http://localhost:5080/api/products?search=charger&localOnly=false" \
  -H "Authorization: Bearer $STAFF" | grep -c costPrice      # must print 0
```

### V17 — Filters never change the price

As `salesman`, note Charger 20W Fast's price with no filter, then with Brand = Oppo, then with
`saleType=Wholesale`. **Expect**: the price changes only with the sale type, never with a filter.

---

## Performance (SC-025)

With the 5,000-product performance catalogue, the backend suite's new multi-word test must pass:
a three-word search completes in under **one second**.

## Regression gate (constitution VI)

```bash
cd backend  && dotnet test
cd frontend && npm run test && npx tsc --noEmit
```

Baseline entering this feature: **550 backend** (214 unit + 328 integration + 8 architecture) and
**263 frontend**.

## Cleanup

Remove the test catalogue from the live database afterwards, and set any brand marked local
during V13 back to Imported if it was only for the check.
