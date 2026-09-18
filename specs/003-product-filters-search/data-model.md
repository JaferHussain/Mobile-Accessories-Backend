# Phase 1 Data Model: Product Filters & Forgiving Search

**Feature**: 003-product-filters-search
**Date**: 2026-09-17

One new migration: `0016_brand_is_local.sql`, continuing from `0015_customer_opening_balance.sql`.
No existing migration is edited. **Search needs no schema change** — it normalises at query time
(research R1, R4).

---

## §1. `brands` — new column

| Column | Type | Null | Default | Notes |
|---|---|---|---|---|
| `is_local` | `BOOLEAN` | NOT NULL | `FALSE` | True when the owner has marked this brand as locally made (FR-087a). |

- **`DEFAULT FALSE`** makes every existing brand Imported on migration, so nothing appears under
  the Local filter until the owner decides. Guessing which of today's brands are local would
  misreport stock (spec Assumptions).
- **`NOT NULL`**: every brand is exactly one of Local or Imported (FR-087). A nullable flag would
  introduce a third "unknown" state the filter would have to interpret.

**Index**: none. The flag is read through the existing `brand_id` join from `products`, and the
`brands` table holds tens of rows.

## §2. Domain and contracts

| Type | Change |
|---|---|
| `Brand` entity | gains `bool IsLocal` |
| `BrandDto` | gains `bool IsLocal` |
| `BrandUpsertRequest` | gains `bool IsLocal`, defaulting to `false` |
| `ProductQuery` | gains `bool LocalOnly` |
| `ProductRow`, `ProductStaffDto` | gain `bool BrandIsLocal`, so the list can label local stock |

`Category` is **unchanged**. Local is a property of the maker, not of the kind of product.

**Staff visibility**: `BrandIsLocal` is not cost, margin or profit, so it belongs on
`ProductStaffDto`. It contains none of the architecture test's forbidden fragments
(`cost`, `profit`, `margin`, `wholesale`, `payable`, `expense`).

## §3. Search terms — a value, not a table

Search is not stored. `ProductSearchTerms` (research R5) is a pure transformation of the typed
text:

| Step | Input `"  Chargers  TYPE-c!"` |
|---|---|
| Split on non-alphanumerics | `Chargers`, `TYPE`, `c` |
| Lower-case | `chargers`, `type`, `c` |
| Strip one trailing `s` from words of ≥ 4 chars | `charger`, `type`, `c` |
| Drop empties, de-duplicate, cap at 8 | `charger`, `type`, `c` |
| **One-letter-only?** | no — `charger` is longer → search runs |

**Rules the value enforces**, each a unit test:

1. Words contain only `[a-z0-9]` — nothing that means anything to SQL or `LIKE`.
2. At most 8 words.
3. Duplicates removed (`cable cable` is one word), so a repeat never adds a redundant predicate.
4. `IsTooShort` is true when every word is one character (FR-079).
5. Empty input yields no words and no search predicate — the unfiltered list, as today.

## §4. Matching predicate

For each word `w`, a product matches when:

```
normalise(p.name) LIKE %w%   OR  normalise(p.model) LIKE %w%
OR normalise(b.name) LIKE %w% OR normalise(c.name) LIKE %w%
```

and a product is listed only when that holds **for every word** (FR-074). Separately, the whole
trimmed input still matches `p.barcode` exactly (FR-080), as an alternative to the word match.

`normalise(x)` = `REGEXP_REPLACE(LOWER(x), '[^a-z0-9]', '')`. A `NULL` model or brand normalises to
`NULL`, and `NULL LIKE …` is not true, so a missing detail simply cannot be the one that matches —
the other fields still can (spec edge case: product with no brand).

## §5. Filter combination

All present conditions are joined with `AND` (FR-083, FR-088):

| Condition | Present when | Predicate |
|---|---|---|
| Search words | text typed | §4, per word |
| Brand | `brandId` set | `p.brand_id = @brandId` |
| Category | `categoryId` set | `p.category_id = @categoryId` |
| Local only | `localOnly = true` | `b.is_local = TRUE` |
| Low stock | `lowStockOnly = true` | existing, unchanged |
| Active | always, unless Admin asks for inactive | existing, unchanged |

`b.is_local = TRUE` cannot be satisfied through the `LEFT JOIN` when a product has no brand, so
unbranded products are excluded from the Local filter with no special case (research R7).

## §6. Invariants

1. **Adding a word never widens the result.** For any search `S` and word `w`, results for
   `S + w` ⊆ results for `S`. This is what "every word must match" means, and a test asserts it.
2. **Word order is irrelevant.** Results for `a b` equal results for `b a`.
3. **Spelling variants agree.** `c type`, `type c`, `type-c`, `typec` and `Type C` return the same
   set for the same catalogue (SC-022).
4. **Filters never change price or visibility.** The price and fields returned for a product are
   identical with and without any filter (FR-089, FR-090).
5. **A barcode scan is exact.** Scanning a full barcode returns that product regardless of the new
   word matching.
