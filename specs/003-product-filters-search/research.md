# Phase 0 Research: Product Filters & Forgiving Search

**Feature**: 003-product-filters-search
**Date**: 2026-09-17

## What already exists

| Need | Current state | Work |
|---|---|---|
| Brand / category filter (US2) | `GET /api/products` already accepts `categoryId` and `brandId`; the Products screen never sends them | Screen work plus tests |
| Forgiving search (US1) | One `LIKE '%phrase%'` per field, so the whole phrase must appear in one field | Rewrite the search predicate |
| Local brands (US3) | No such concept | New column, filter, and a control on the Brands screen |

**Found while researching: product search is shared by three screens** — Products, the POS
lookup (`PosPage.findProduct`) and the Purchases product picker. Changing search changes all
three. That is desirable (a salesman typing "c type" at the counter benefits most), but the plan
must verify the POS and Purchases behaviour rather than assume it (see R6).

---

## R1. How words are matched

**Decision**: Split the typed text into words on anything that is not a letter or digit. A product
matches when **every** word appears, as a substring, inside **at least one** of its normalised
name, model, brand name or category name. Normalising a field means lower-casing it and deleting
every character that is not a letter or digit.

**Worked through against the spec's own examples** ("Type-C Braided Cable", brand Baseus, category
Cables — normalised name `typecbraidedcable`):

| Typed | Words | Result |
|---|---|---|
| `c type` | `c`, `type` | both in `typecbraidedcable` ✔ |
| `type-c` | `type`, `c` | ✔ |
| `typec` | `typec` | in `typecbraidedcable` ✔ |
| `baseus cable` | `baseus` → brand, `cable` → name | ✔, different words matching different fields |
| `oppo charger` vs a Samsung charger | `oppo` fails on the Samsung product | only the Oppo charger ✔ |
| `20 w` vs "Charger 20W" | `20`, `w` | ✔ |

**Rationale**: Deleting punctuation *inside the field* is what lets `typec` match `Type-C`;
splitting the *typed text* on punctuation is what lets `type-c` and `c type` match the same thing.
Doing both covers every spelling in SC-022 with one rule and no special cases.

**Fields are normalised separately, not concatenated.** Squashing name and brand into one string
would let a word match across the boundary between them — `leba` would match "Cab**le** + **Ba**seus".
Rare, but a false match at the counter is worse than a slightly longer query.

**Alternatives considered**:

- *MySQL `FULLTEXT` / `MATCH … AGAINST`.* Rejected. It tokenises on word boundaries, so `typec`
  would never match `Type-C`, and its default minimum word length (3 for InnoDB) silently drops
  `c` from `c type`. Feature 001's research reached the same conclusion for partial words.
- *A stored, pre-normalised `search_text` column on `products`.* Rejected for now. Brand and
  category names live in other tables, so a rename in the Brands module would leave every
  product's stored text stale until re-indexed. At 5,000 products, normalising at query time is
  well within budget (R4) and cannot go stale.
- *Fuzzy / typo-tolerant matching.* Out of scope by the spec's assumptions.

## R2. Singular and plural

**Decision**: Before matching, strip one trailing `s` from any typed word of four or more
characters. Matching is by substring, so the stored side needs no treatment.

**Rationale**: Substring matching already makes `charger` find `Chargers`. The only gap is the
reverse — typing `chargers` against a product named `Charger 18W`. Stripping the trailing `s` from
the *typed* word closes it: `chargers` → `charger` ✔.

The four-character floor stops short words being damaged: `bus`, `gas`, `abs` and model numbers
ending in `s` are left alone. A word that genuinely ends in `s` but is not plural
(`wireless` → `wireles`) still matches, because `wireles` is a substring of `wireless`. Stripping
can only ever *widen* a substring match, never break one.

## R3. One-letter searches (FR-079)

**Decision**: If every typed word is a single character, the search is not run. The server
returns `400 VALIDATION_FAILED` with the message "Type at least 2 letters to search."; the
Products and Purchases screens show that hint without calling the server.

**Rationale**: `c` alone matches most of the catalogue, so the result is noise. But a one-letter
word *alongside* a longer one is the whole point of `c type`, so the rule is about the search as a
whole, not about each word. Enforcing on the server as well follows constitution II — the rule
holds however the request arrives.

**The POS is the exception on screen.** A barcode scanner types then presses Enter, and a
one-character scan is not a real scan, so the POS simply shows its existing "No product found"
message rather than a validation hint. See R6.

## R4. Performance

**Decision**: Normalise in SQL with `REGEXP_REPLACE(LOWER(col), '[^a-z0-9]', '')`, one
`LIKE CONCAT('%', @wordN, '%')` per word per field, and cap a search at **8 words**.

**Rationale**: Verified against the live server — MySQL 8.0.40 supports `REGEXP_REPLACE` and
normalises `Type-C Braided Cable` to `typecbraidedcable`. A leading-wildcard `LIKE` cannot use an
index, but it could not before either; this is a scan of at most 5,000 rows, which the existing
`PerformanceTests.Searching_a_five_thousand_item_catalogue_returns_within_two_seconds` already
guards. SC-025 tightens that to one second, so the plan adds a multi-word performance test at the
same catalogue size.

The word cap bounds the query's size against a pasted paragraph. Eight is far above anything typed
at a counter.

**Injection safety**: typed words are reduced to `[a-z0-9]` *before* being bound as parameters, so
they cannot carry `%`, `_`, quotes or anything else meaningful to SQL or to `LIKE`. They are still
passed as parameters, never concatenated.

## R5. Where the word-splitting lives

**Decision**: A pure static class `ProductSearchTerms` in `MoizPos.Application/Calculations`
turns the typed text into the list of normalised words (split, lower-case, strip non-alphanumerics,
de-plural, cap at 8, detect the one-letter case). The repository only builds SQL from its output.

**Rationale**: Every rule in R1–R3 is decided here, and every one is unit-testable without a
database — including the SC-022 spellings. This mirrors `StockRules` and `OpeningBalanceRules`,
and keeps the repository free of business decisions (constitution II).

## R6. Effect on the POS and Purchases screens

**Decision**: Accept the improved search on both, and add a test for each proving it.

- **POS** calls search with `pageSize: 1` and adds the first result to the cart. A looser search
  can match more products than before, so the *first* match matters more. Results are already
  ordered by name, which is deterministic. The barcode path runs first and is exact, so scanning is
  unaffected (FR-080).
- **Purchases** picker lists up to 25 results; more matches is simply more choice.

**Risk accepted and stated**: at the POS, a vague search such as `cable` now adds the
alphabetically first cable, exactly as it did before for a matching phrase. The difference is only
that more spellings now reach a match. The plan does not change the POS to show a pick-list —
that would be a separate feature.

## R7. Local brands (FR-087, FR-087a)

**Decision**: `brands.is_local BOOLEAN NOT NULL DEFAULT FALSE` in migration `0016`. The product
query gains `LocalOnly`; when set, it requires `b.is_local = TRUE`, which by construction excludes
unbranded products.

**Rationale**: Directly encodes clarification answer A. `DEFAULT FALSE` makes every existing brand
Imported, so nothing is misreported as local before the owner decides (spec Assumptions).
Unbranded products drop out naturally because an inner requirement on `b.is_local` cannot be
satisfied by a `NULL` brand — the behaviour the spec states, with no special case.

**Combination with the Brand filter**: choosing a specific brand *and* Local only applies both.
If the chosen brand is Imported, the result is empty, and FR-086's "no products match" message
explains it. No attempt is made to disable one control based on the other — that couples the two
filters the spec asks to keep individual.

**Not cost-sensitive**: whether a brand is local is not cost, margin or profit, so it may be
shown to Staff. The architecture test's forbidden list is unaffected.

## R8. Screen design for the filters

**Decision**: On the Products screen, beside the existing search box and low-stock tick: a
**Brand** select, a **Category** select, a **Local brands only** tick, and a **Clear filters**
button that appears only when a filter is set.

**Rationale**: A tick for Local matches its nature — it is on or off, not a choice from a list —
and matches the existing low-stock tick beside it. Selects populate from the existing
`categoryApi` / `brandApi`, which already return active rows ordered by name (FR-085).

Search runs as the user types (FR-081), as it does today; the existing TanStack Query key gains the
filter values so every combination is cached separately.

## Open questions

None.
