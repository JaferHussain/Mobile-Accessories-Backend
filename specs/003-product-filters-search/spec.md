# Feature Specification: Product Filters & Forgiving Search

**Feature Branch**: `003-product-filters-search`

**Created**: 2026-09-17

**Status**: Draft

**Input**: User description: "add filter for brands, categories and local brands, individually. we can search product like brand, oppo, categorised, charger, product c type. in product module"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Search the way the shopkeeper types (Priority: P1)

A customer asks for "an Oppo charger" or "a C type cable". The salesman types what they heard
into the search box. Today the box only finds a product if the exact phrase typed appears inside
one field, so "c type" does not find "Type-C Braided Cable", and "oppo charger" finds nothing
because "Oppo" is the brand and "charger" is the category. The shopkeeper has to guess the
spelling the product was saved under.

Search should find a product when every word typed appears somewhere in it — its name, brand,
category or model — regardless of the order the words were typed, and regardless of spaces and
hyphens.

**Why this priority**: This is used on every sale at the counter, by both users, and it is where
time is lost with a customer waiting. The filters in the stories below help narrow a long list;
this story decides whether the right product can be found at all.

**Independent Test**: With "Type-C Braided Cable" (brand Baseus, category Cables) in stock, type
each of "c type", "type c", "typec", "baseus cable" and "cable baseus" — every one must find it.

**Acceptance Scenarios**:

1. **Given** a product named "Type-C Braided Cable", **When** "c type" is searched, **Then** it
   is found.
2. **Given** the same product, **When** "type c", "type-c" or "typec" is searched, **Then** it is
   found each time.
3. **Given** a product of brand Oppo in category Chargers, **When** "oppo" is searched, **Then**
   it is found.
4. **Given** that product, **When** "charger" is searched, **Then** it is found — a singular word
   matches a plural category name.
5. **Given** that product, **When** "oppo charger" or "charger oppo" is searched, **Then** it is
   found, even though the two words live in different details of the product.
6. **Given** an Oppo charger and a Samsung charger, **When** "oppo charger" is searched,
   **Then** only the Oppo charger is found — every word must match, so adding a word narrows the
   results rather than widening them.
7. **Given** any product, **When** the search is typed in capitals or lower case, **Then** the
   results are identical.
8. **Given** a product with a barcode, **When** the full barcode is scanned into the box,
   **Then** that product is found, exactly as today.

---

### User Story 2 - Filter by brand and by category (Priority: P2)

The shopkeeper wants to see everything from one brand ("show me all Oppo stock") or everything
in one category ("show me all chargers") without typing, and to combine the two ("Oppo
chargers").

**Why this priority**: It turns a long catalogue into a short list in one click, for stock checks
and for answering "what have you got in …". It is P2 because search (US1) already reaches the
same products with typing; the filters make it faster and less error-prone.

**Independent Test**: Choose Brand = Oppo — only Oppo products show. Add Category = Chargers —
only Oppo chargers show. Clear the brand — all chargers show.

**Acceptance Scenarios**:

1. **Given** the Products screen, **When** a brand is chosen from the Brand filter, **Then** only
   products of that brand are listed.
2. **Given** the Products screen, **When** a category is chosen from the Category filter,
   **Then** only products in that category are listed.
3. **Given** a brand and a category are both chosen, **When** the list is shown, **Then** only
   products matching both are listed.
4. **Given** a filter is chosen, **When** words are also typed in the search box, **Then** only
   products matching the filter **and** the search are listed.
5. **Given** filters are chosen, **When** "Clear filters" is used, **Then** every filter resets
   and the full list returns, leaving any typed search in place.
6. **Given** a combination that matches nothing, **When** the list is shown, **Then** the screen
   says no products match the chosen filters, rather than showing an empty table with no
   explanation.
7. **Given** the Brand and Category lists, **When** they are opened, **Then** they offer only
   brands and categories currently in use, in alphabetical order.

---

### User Story 3 - Filter local brands on their own (Priority: P3)

The shop stocks both imported brands and locally made or unbranded stock. The owner wants to see
local stock on its own — for example to compare what is selling, or to check local stock levels
— separately from imported brands.

**Why this priority**: Useful for stock and buying decisions, but not needed to complete a sale.
It also depends on the shop first marking which brands are local, which is a one-time setup step.

**Independent Test**: Mark one brand as local and leave another as imported. Choose the local
filter — only products of the local brand show. [Exact behaviour depends on the clarification in
FR-087.]

**Acceptance Scenarios**:

1. **Given** some brands are local and some imported, **When** the Local brands filter is chosen,
   **Then** only products of local brands are listed.
2. **Given** the Local brands filter is chosen, **When** a category is also chosen, **Then** only
   local products in that category are listed.
3. **Given** the Local brands filter is chosen, **When** words are typed in the search box,
   **Then** only local products matching the search are listed.
4. **Given** the owner is maintaining brands, **When** a brand is created or edited, **Then** the
   owner can mark whether it is local.

---

### Edge Cases

- **Punctuation and extra spaces.** "type - c", "  type   c  " and "type_c" behave like
  "type c". Symbols and repeated spaces never prevent a match.
- **A single letter.** Searching just "c" would match most of the catalogue. A one-letter word
  still narrows the results when combined with a longer word ("c type"), but a search made only
  of one-letter words is not run — see FR-079.
- **Numbers inside words.** "18w" finds "Charger 18W", and "20 w" finds "20W". Digits and letters
  are compared the same way as any other word.
- **A word that matches nothing.** "oppo xyz" finds nothing, because every word must match; the
  screen says so rather than silently dropping the unmatched word.
- **A product with no brand.** It can still be found by name, category and model, and appears
  when no brand filter is chosen.
- **A retired brand or category.** It is not offered in the filter lists, but products already
  filed under it still appear in search results and when no filter is chosen.
- **Filters and wholesale pricing.** Filtering never changes which price is shown; a product
  appears at the price that applies to the sale being made, exactly as today.
- **A salesman using the filters.** Filters and search behave identically for both users; they
  change which products are listed, never which details of a product a salesman may see.

## Requirements *(mandatory)*

### Functional Requirements

#### Search (US1)

- **FR-074**: Search MUST treat the typed text as separate words and find a product only when
  **every** word matches that product.
- **FR-075**: A word MUST be allowed to match any of the product's name, brand, category or model,
  and different words MAY match different details of the same product.
- **FR-076**: Search MUST ignore the order in which words are typed.
- **FR-077**: Search MUST ignore letter case, hyphens, spaces and other punctuation when comparing,
  so that "c type", "type-c" and "typec" all find a product named "Type-C".
- **FR-078**: Search MUST match a singular word against a plural name and vice versa for the
  ordinary English "s" plural, so "charger" finds category "Chargers".
- **FR-079**: A search consisting only of one-letter words MUST NOT be run against the catalogue;
  the screen MUST ask for more letters instead.
- **FR-080**: Scanning a complete barcode into the search box MUST continue to find that exact
  product.
- **FR-081**: Search results MUST appear as the user types, without requiring a button press.

#### Brand and category filters (US2)

- **FR-082**: The Products screen MUST offer a Brand filter and a Category filter, each usable on
  its own.
- **FR-083**: When more than one filter is chosen, and when a search is also typed, a product MUST
  be listed only if it satisfies all of them.
- **FR-084**: The Products screen MUST offer a single action that clears every filter at once,
  without clearing the search box.
- **FR-085**: The Brand and Category filter lists MUST offer only active brands and categories,
  in alphabetical order.
- **FR-086**: When no product satisfies the chosen filters and search, the screen MUST say so
  explicitly.

#### Local brands (US3)

- **FR-087**: The Products screen MUST offer a Local brands filter that lists, on their own, the
  products whose brand the owner has marked as Local. Every brand MUST be either Local or
  Imported; products with no brand are not included in the Local filter.
- **FR-087a**: The owner MUST be able to mark a brand as Local or Imported when creating or
  editing it, and a brand MUST default to Imported until marked otherwise.
- **FR-088**: The local filter MUST combine with the Category filter and the search box on the same
  terms as FR-083.

#### Both users

- **FR-089**: Filters and search MUST be available to both the owner and the salesman, and MUST
  NOT change which product details either user is permitted to see.
- **FR-090**: Filtering and searching MUST NOT change the price shown for a product; the price
  continues to follow the retail or wholesale sale being made.

### Key Entities

- **Product**: An item in stock. Has a name, an optional model and barcode, a category, and an
  optional brand. Found by search through these details.
- **Brand**: The maker a product is sold under. Maintained by the owner. Carries whether it is Local or Imported,
  set by the owner (FR-087a).
- **Category**: The kind of product — charger, cable, earbuds. Maintained by the owner.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-022**: For a product named "Type-C Braided Cable", 100% of the spellings "c type",
  "type c", "type-c", "typec" and "Type C" find it.
- **SC-023**: A search combining a brand and a category ("oppo charger") lists only products
  matching both, with no products matching just one of the words.
- **SC-024**: At the counter, a shopkeeper can narrow the catalogue to one brand in one category
  in no more than two selections, with no typing.
- **SC-025**: Search and filter results appear within one second of typing or choosing a filter,
  with the shop's full catalogue of up to 5,000 products.
- **SC-026**: Every combination of filters and search that matches nothing produces an explicit
  "no products match" message — never an unexplained empty screen.
- **SC-027**: The owner can mark a brand as local, or change it back, in under 30 seconds, and the
  change is reflected in the local filter immediately.

## Assumptions

- **Local is a property of the brand, chosen by the owner** (clarified 2026-09-17, option A).
  Existing brands start as Imported, so nothing appears under Local until the owner marks a brand;
  this avoids guessing which of today's brands are local. Unbranded products are deliberately not
  treated as local — an imported item saved without a brand would otherwise be misreported.
- **This extends the existing Products screen** rather than adding a new one. Categories and
  Brands already exist as modules the owner maintains (feature 001's taxonomy work), so the
  filters choose from those lists rather than introducing new ones.
- **Every search word must match (AND, not OR).** The description asks to find a product "like
  brand oppo … charger"; returning every Oppo product *and* every charger for "oppo charger" would
  widen the list as the shopkeeper adds words, which is the opposite of what they are doing when
  they type more.
- **Singular/plural handling covers the ordinary trailing "s" only** ("charger"/"chargers",
  "cable"/"cables"). Irregular plurals and misspellings ("chrger") are out of scope — tolerating
  typos is a much larger change with a real risk of false matches at the counter.
- **Urdu and Roman-Urdu search terms are out of scope.** Product details are entered in English
  today, and search matches what was entered.
- **Search covers name, brand, category and model**, plus exact barcode — the same details
  searched today. Supplier name and price are not searched.
- **Filters are not remembered** between visits to the Products screen; each visit starts
  unfiltered. Remembering them would hide stock from the next person to open the screen.
- **The existing low-stock filter stays** as it is and combines with the new filters on the same
  terms.
- **The point-of-sale lookup box is out of scope for the filters** but benefits from the improved
  search automatically if it uses the same product search, which is expected to be confirmed in
  planning.
