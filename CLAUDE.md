# Working in this repository

Runtime guidance for anyone — human or agent — changing this codebase. The authority is
[.specify/memory/constitution.md](.specify/memory/constitution.md); this file is the practical
summary plus the traps that are easy to fall into.

## What this is

A point-of-sale, inventory and customer-credit (udhaar) system for **Moiz Mobile & Corporation,
Danwran Lodhran** — a single mobile-accessories shop. Two users: the owner (Admin) and a
salesman (Staff).

```
backend/   ASP.NET Core 8 · Dapper · MySQL 8        frontend/  React 18 · TypeScript · Vite
specs/     the specification, plan and task list    docs/      deployment notes
```

The backend is **one project**, `backend/src/MoizPos`, holding every layer in its own folder —
`Domain/`, `Application/`, `Infrastructure/`, `Api/`, `Migrator/` — plus one test project per kind
of test. Folders map to the namespaces they always had, so `MoizPos.Domain.Entities` is
`Domain/Entities`.

## Commands

```bash
# database (once)
mysql -u root -p < docs/create-databases.sql
dotnet run --project backend/src/MoizPos -- migrate

# run
dotnet run --project backend/src/MoizPos           # http://localhost:5080
cd frontend && npm run dev                         # http://localhost:5173

# test — both must pass before anything is merged
cd backend  && dotnet test
cd frontend && npm run test && npx tsc --noEmit
```

Integration tests need MySQL running. They create and drop their own schema in `moizpos_test`
and refuse to run against a database whose name lacks "test".

## Product search

Search splits what is typed into words; a product is listed only when **every** word appears in its
name, model, brand or category. `c type`, `type-c` and `typec` all find "Type-C", and `oppo charger`
finds only Oppo chargers (feature 003).

- **The rules live in `ProductSearchTerms`** (Application, pure): split on non-alphanumerics,
  lower-case, strip one trailing `s` from words of **4+ characters**, de-duplicate, cap at 8 words.
  A search of only one-letter words throws `SearchTooShortException` (400 `VALIDATION_FAILED`).
- **Each field is normalised on its own** in SQL — `REGEXP_REPLACE(LOWER(col), '[^a-z0-9]', '')` —
  and **never concatenated first**. Concatenating would let a word match across the boundary
  between two fields.
- A full barcode is still matched exactly and whole, as an alternative to the word match.
- **Search is shared by the Products, POS and Purchases screens.** A change to it is a change to
  all three. The POS takes the first result, so result order (by name) matters there.
- It normalises at query time rather than from a stored column so a brand or category rename can
  never leave search stale. `A_multi_word_search_across_five_thousand_products_is_under_a_second`
  guards the cost; if it ever fails, the stored-column fallback is documented in
  `specs/003-product-filters-search/plan.md` — do not loosen the bound.

## Credit authority

**Only an Admin may complete a sale that leaves any amount outstanding** (FR-051, FR-052). A
Staff user selling for full payment is unaffected, and can still record recovery payments — 
collecting a debt does not create one.

The rule lives in `InvoiceService.CreateAsync`, applied to the **server-recomputed**
`totals.AmountRemaining`, after pricing and the stock check and **before the first write**.

- **It cannot be an `[Authorize]` policy on the endpoint.** Whether a sale is credit is a property
  of the recomputed total, which does not exist until `InvoiceCalculator` has run. A policy would
  have to trust the client's own `amountPaid` — the one number an attacker controls. A sale
  labelled `Cash` whose payment falls short is still credit, and a test asserts exactly that.
- **Never key it off `PaymentMethod`.** `Credit` and `Partial` are descriptive labels only.
- Because the check sits before any write, a refusal leaves no invoice, no stock movement and no
  balance change. `A_refused_credit_sale_changes_absolutely_nothing` asserts all four.
- `CreateAsync` takes the caller's `UserRole` as an argument rather than reading ambient context,
  so the rule stays unit-testable and cannot be bypassed by a caller that forgets to set one.

## Opening balances

A customer can carry an `opening_balance` — what they owed on paper before this software
(FR-065). Recorded via `PUT /api/customers/{id}/opening-balance`, Admin only.

**Recording it a second time is a CORRECTION, never a second debt.** The balance moves by
`new − old` (`OpeningBalanceRules.Delta`). Applying the requested amount instead would double what
the customer owes — the single most damaging way this feature can fail, and invisible until the
customer disputes it. `Recording_it_again_corrects_the_figure_and_never_doubles_the_debt` guards it.

- `opening_balance` is **nullable on purpose**: "never recorded" and "recorded as zero" are
  different facts, and only the first makes the next save a correction.
- A first recording writes an `OpeningBalance` ledger entry; a correction appends an `Adjustment`
  carrying the old and new figures in `note` and leaves the original entry untouched.
- The ledger's invariant still holds: replaying `bill_amount − paid_amount` reproduces
  `outstanding_balance`.
- The opening entry is **not back-dated** ahead of existing entries — `balance_after` is
  persisted, and rewriting it to improve display order would trade a real invariant for a
  cosmetic one. For a customer entered from the register before trading, it is first anyway.

## Retail and wholesale sales

Every invoice carries a `sale_type` of `Retail` or `Wholesale` (migration `0014`, defaulting
existing rows to Retail). The owner reads the day split between the two, and clicking either
total opens the sales behind it — invoice, time, customer, and the salesman who sold it.

- **The type is recorded, never inferred.** A discounted retail sale and a wholesale sale can
  reach the same figure, so the price cannot tell them apart afterwards.
- **Pricing is resolved server-side.** `ProductQuery.SaleType` decides which price comes back as
  `SalePrice`; a wholesale sale is quoted `wholesale_price`, falling back to `sale_price` where no
  wholesale price is set. This is why Staff never receive a `wholesalePrice` field — `wholesale`
  is on the architecture test's forbidden-for-Staff list (FR-040), so the salesman is told the one
  price that applies rather than handed the price list.
- **`saleType` must be threaded through every read the counter makes** — search, get-by-id and
  get-by-barcode. Omitting it on any one of them silently re-quotes the counter price; that bug
  shipped once and `Reading_one_product_honours_the_sale_type` now guards it.
- Reports: `GET /api/reports/sales-by-type` (both halves, zeroes included) and
  `GET /api/reports/sales-list` (the drill-down; omit `saleType` for the whole day).

## Categories and Brands

A product is filed against a row in the `categories` table (required) and optionally one in
`brands`. Both are modules the owner maintains at `/categories` and `/brands`; neither is free
text any more. Migration `0013` converted the old `products.category`/`products.brand` columns
into foreign keys, migrating every distinct value that existed rather than discarding it.

- Read is open to any signed-in user (the product list shows both); every write is `AdminOnly`.
- Names are unique, and the collation is case- and accent-insensitive, so "Cables" and "cables"
  collide by design — that is the point of the module.
- Nothing is hard-deleted. The foreign keys are `ON DELETE RESTRICT` and the service retires a
  row instead: products keep their label, and the retired row simply stops being offered.
- `ProductRow.Category` and `.Brand` are the joined **names**, for display; `CategoryId` and
  `BrandId` are what a write accepts.
- **Local brands** (feature 003): `brands.is_local` defaults to **Imported**, so nothing is local
  until the owner marks it. The Products `localOnly` filter requires `b.is_local = TRUE`, which
  through the `LEFT JOIN` deliberately excludes **unbranded** products — local is a property of a
  brand, and an imported item saved without one must not be misreported as local.

## The two business rules that drive the design

**1. Latest purchase cost.** When stock is bought, that purchase's unit cost *replaces* the
product's cost for every unit on hand — not a weighted average. Buying 10 at 800, selling 5,
then buying 10 at 850 leaves all 15 units costed at **850, not 825**. The owner chose this
deliberately: profit is measured against what it costs to replace the goods today.

**2. Current sale price.** Old stock sells at today's price, not the price in force when it was
bought.

`StockRules.NextCostPrice` is where rule 1 lives, and
`PurchaseCostRuleTests.Latest_purchase_cost_replaces_the_cost_of_all_stock_on_hand` asserts the
owner's own worked example, explicitly checking the answer is *not* 825. If you find yourself
"fixing" that into an average, stop and ask.

## Traps

Each of these caused a real bug during the build.

| Trap | What happens | Guard |
|---|---|---|
| Joining profit queries to `products.cost_price` | Every past month's profit silently rewrites itself whenever stock is bought | Always use `invoice_items.unit_cost_price` — the cost snapshotted at sale |
| Returning `ProductAdminDto` typed as `ProductStaffDto` | System.Text.Json serialises the *declared* type, so the Admin silently loses cost data | `[JsonDerivedType]` on the base — do not remove it |
| Dapper + positional records | Ids are `BIGINT UNSIGNED` (ulong) but the domain uses `long`; Dapper matches records by exact constructor signature and will not convert | Materialised types use init-only properties, never `record Foo(...)` |
| A new validator in the `Api` project | Never runs unless its assembly is scanned | `Program.cs` scans **both** Application and Api |
| A new enum on a request | Rejected as a 400 unless serialised by name | `JsonStringEnumConverter` is registered; keep it |
| Deciding "is this a credit sale?" from the request | The client controls `amountPaid` and `paymentMethod`, so the rule is evadable | Read `totals.AmountRemaining` after the server recomputes, never the request |
| Sending a search the server now refuses | A one-letter search returns 400; the POS surfaced it as an error at the counter | `PosPage` treats `VALIDATION_FAILED` from search as "no product found"; `ProductsPage` shows a hint and doesn't send it |
| Selecting `products.category` directly | The column no longer exists — it is `category_id`, joined to `categories` | Join `categories c ON c.id = p.category_id` and select `c.name` |
| Adding a folder outside the five layers | It is silently unchecked by the layering tests | `Every_source_file_sits_in_a_known_layer` fails until you add the layer to `LayeringTests.Allowed` and say what it may depend on |
| Two cart lines for one product | Each checks stock against the same locked row and can oversell | `InvoiceService` refuses duplicates; the POS merges them |

## Non-negotiables

- **Money is `decimal`.** Never `double` or `float`, anywhere near a price, total or balance.
  `DECIMAL(12,2)` in MySQL, `DECIMAL(12,4)` for cost. A test asserts no entity or calculator
  exposes a floating-point amount.
- **Every stock/balance/invoice mutation runs in one transaction** via `IUnitOfWorkFactory`, and
  locks affected rows with `SELECT ... FOR UPDATE` **ordered by id** before checking stock.
  Partial state is worse than no system.
- **The server recomputes all totals** from the line items it stored. Client totals are discarded.
- **Cost and profit never reach a Staff principal.** Separate DTOs, `AdminOnly` per endpoint, and
  architecture tests that fail the build if a Staff-reachable endpoint declares cost data.
- **TDD.** A failing test comes first. A phase is done only when unit tests pass, integration
  tests pass, and nothing previously green broke.
- **Schema changes go through DbUp**, as a new numbered script in
  `backend/src/MoizPos/Migrator/Scripts/`. Never edit an applied script; never hand-edit a
  shared database.

## Layering

`Domain` → nothing. `Application` → `Domain`. `Infrastructure` → `Application`. `Api` → both.

**Enforced by `LayeringTests`, not by the compiler.** The backend was five projects until
2026-09-19, and project references made these rules impossible to break. It is now one project, so
`LayeringTests` reads every file's namespace and its `using` directives and fails the build on a
violation instead. The guard is verified: adding `using MoizPos.Application.Services;` to a domain
entity reddens two tests.

**If you need SQL inside a service, put it behind an interface in `Application/Abstractions` and
implement it in `Infrastructure`** — that is what the `*WriteRepository` types are, and
`A_service_never_opens_its_own_database_connection` enforces it by refusing `using Dapper` or
`using MySqlConnector` anywhere under `Application/`.

## Time

Stored in UTC. Reporting periods resolve against `Asia/Karachi` (+05:00, no DST) through
`PeriodResolver`, which returns half-open ranges so a sale lands in exactly one bucket. Inject
`IClock`; never call `DateTime.UtcNow` in a service.

## The one unauthenticated endpoint

`GET /api/public/documents/{token}` serves a customer their own receipt. A shop customer holds no
credential, and `wa.me` cannot attach a file — so the receipt travels as a link. Containment:
≥256-bit token stored only as a SHA-256 hash, resolving to one document, expiring, revocable,
rate-limited, access-logged. An architecture test fails the build if a second anonymous endpoint
appears.

## Where to look

| Question | File |
|---|---|
| What is the shop supposed to do? | `specs/001-pos-inventory-ledger/spec.md` |
| Why was it built this way? | `specs/001-pos-inventory-ledger/research.md` |
| What does the schema look like? | `specs/001-pos-inventory-ledger/data-model.md` |
| What does the API accept? | `specs/001-pos-inventory-ledger/contracts/` |
| How do I verify it works? | `specs/001-pos-inventory-ledger/quickstart.md` |
| How do I deploy it? | `docs/deployment.md` |
