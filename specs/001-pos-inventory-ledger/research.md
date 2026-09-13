# Phase 0 Research: POS, Inventory & Customer Ledger System

**Feature**: `001-pos-inventory-ledger` · **Date**: 2026-09-09

The constitution fixes most of the stack (ASP.NET Core 8, Dapper, MySQL 8, React + TypeScript,
xUnit, Vitest, FluentValidation, JWT). This document resolves what the constitution and spec
leave open, and records the findings that change how the system must be built.

---

## R1. Migration tool: DbUp vs Flyway

**Decision**: DbUp, with numbered `.sql` scripts embedded as resources in a dedicated
`Migrator` console project, executed on startup in Development and as an explicit deploy step
in Production.

**Rationale**: The constitution requires version-controlled SQL migrations but names either
tool. DbUp is a NuGet package with no runtime outside .NET, so the shop's server needs only the
ASP.NET runtime. Flyway's standalone distribution requires a JVM on the deployment machine —
a second runtime to install, patch, and explain to a non-technical owner. DbUp also runs
in-process, which makes it trivial to spin up a throwaway schema inside integration tests
(needed for the restore test in FR-047).

**Alternatives considered**: Flyway (rejected: JVM dependency for a single-shop deployment);
EF Core Migrations (prohibited by Constitution Principle II); hand-run SQL (rejected: violates
the same principle).

---

## R2. WhatsApp delivery — a real constraint on FR-044

**Decision**: Use a `wa.me` deep link carrying a **tokenized public download URL** for the PDF,
not an attached file. The backend exposes `GET /api/public/documents/{token}` returning the PDF
for an unguessable, expiring token.

**Rationale**: This is the most important finding in this phase. **The `wa.me` deep link scheme
cannot attach a file.** It supports only a prefilled text message (`?text=`). No browser-based
integration can push a PDF into WhatsApp on the user's behalf. Therefore "Send on WhatsApp with
the PDF attached" as literally worded in the source brief is not achievable through deep links,
and the spec's FR-044 wording ("the receipt document **or its link**") is the achievable form.

Two viable routes exist:

| Route | What the customer gets | Cost / friction |
|---|---|---|
| `wa.me` deep link + hosted PDF link | A message with a tappable link to their receipt | Free; operator taps Send |
| WhatsApp Business Cloud API | A true PDF attachment, sent automatically | Meta business verification, approved message templates, per-conversation fees |

The Business API also forbids unsolicited free-form messages outside a 24-hour customer-service
window, so receipts would need pre-approved templates. For a single shop that already has
WhatsApp on the counter phone, the deep link is the proportionate choice.

**Consequence for the plan**: the tokenized document endpoint is the only intentionally
unauthenticated endpoint in the system. It MUST use a high-entropy token (≥128 bits), expire
(default 30 days), be revocable, and expose nothing but that one document. It is called out
explicitly in the Constitution Check below because it is a deliberate exception to
"authenticate before any business data".

**Alternatives considered**: WhatsApp Business Cloud API (deferred to a later feature — recorded
as out of scope in the spec's assumptions); emailing the PDF (the shop's customers transact by
phone number, not email); `navigator.share` with a File (Web Share API Level 2 is unreliable on
desktop Chrome/Windows, which is the counter's primary target).

---

## R3. PDF generation library

**Decision**: QuestPDF, Community licence.

**Rationale**: QuestPDF's fluent layout API handles the invoice's repeating line-item table with
page breaks without manual coordinate maths, and renders deterministically enough to unit-test
the content-assembly layer (Constitution Principle I requires testing that all required fields
are present). **Licensing check: QuestPDF's Community licence is free for organisations with
annual gross revenue below USD 1M** — a single mobile-accessories shop is comfortably inside
that. This must be re-checked if the software is ever resold to other shops, which would make it
a commercial product needing a paid licence.

**Alternatives considered**: iText7 (AGPL — would require open-sourcing the shop's system or
buying a commercial licence); PdfSharpCore (lower-level, manual table layout); client-side
generation via jsPDF (rejected: puts monetary rendering on the untrusted client, against
Principle IV, and produces no server-side artifact to link from WhatsApp).

---

## R4. Preventing oversell under concurrent sales

**Decision**: Pessimistic row locking. Inside the sale transaction, lock every affected product
row with `SELECT ... FOR UPDATE` ordered by product id, verify stock, then decrement. Use
`READ COMMITTED` isolation.

**Rationale**: The spec's edge case "two sales for the last unit" and FR-006 ("stock MUST NOT go
negative") demand a guarantee, not a best effort. `FOR UPDATE` gives it directly and is simple to
reason about. Locking in a consistent id order prevents deadlocks when two multi-line sales
overlap on the same products. `READ COMMITTED` (rather than MySQL's default `REPEATABLE READ`)
avoids gap locking on the ledger insert paths and matches the read-your-own-write behaviour the
services expect. A single counter with one or two terminals will never see meaningful lock
contention.

**Alternatives considered**: Optimistic concurrency with a row version (rejected: pushes retry
logic into every caller and still needs a guard); `UPDATE ... WHERE quantity >= @qty` and
checking rows-affected (viable and lock-free, but composes badly across a multi-line sale that
must also touch customer balance and stock movements atomically); application-level mutex
(rejected: does not survive multiple app instances).

---

## R5. Money and quantity representation

**Decision**: `DECIMAL(12,2)` for all monetary columns, `DECIMAL(12,4)` for the product cost
column, `INT` for quantities. C# `decimal` throughout. No floating point anywhere in the money
path.

**Rationale**: Binary floating point cannot represent 0.10 exactly and would make ledger balances
fail to reconcile — directly violating SC-004 and SC-005, which demand *zero* discrepancy against
hand calculation. `DECIMAL(12,2)` holds up to 9,999,999,999.99 PKR, far beyond this shop's needs.
The cost column carries four decimal places purely as headroom; under the latest-cost rule
(FR-011a) it always receives a clean purchase price, but the extra scale costs nothing and
protects against a future costing change.

**Alternatives considered**: storing paisa as integers (defensible, but adds conversion at every
boundary and makes ad-hoc SQL reports harder for a future maintainer to read); `DOUBLE`
(rejected outright).

---

## R6. Time, timezones and reporting-period boundaries

**Decision**: Store all timestamps as UTC in `DATETIME(6)` columns. Convert to `Asia/Karachi`
(fixed UTC+05:00) at the reporting boundary. Inject an `IClock` abstraction so period logic is
unit-testable (Constitution Principle I names timezone edge cases explicitly).

**Rationale**: FR-034 requires every transaction to land in exactly one period under shop-local
time. Pakistan currently observes **no daylight saving**, so the offset is a constant +05:00 —
which removes the usual ambiguity of local-time storage. Storing UTC nonetheless keeps the data
correct if that policy ever changes, and keeps ordering unambiguous. "Today" on the dashboard
means 00:00:00–23:59:59.999999 Karachi time, converted to a UTC half-open range
`[startUtc, endUtc)` for querying.

**Alternatives considered**: storing local time directly (simpler today, silently wrong if DST is
ever introduced — Pakistan has trialled it before); storing `DATETIMEOFFSET` (not a MySQL type).

---

## R7. Product image storage

**Decision**: Store image files on disk under a configured content root; persist only the
relative path in `products.image_path`. Serve through a static file route. Validate content type
and cap size at 2 MB, re-encoding to JPEG on upload.

**Rationale**: FR-001 requires an image per variant, and a variant-heavy catalogue of 5,000 items
(SC-002) means thousands of images. BLOBs in MySQL bloat the table, slow every `SELECT *`, and —
critically here — inflate the daily backup (FR-045) that must remain small enough to be practical
for a shop server. Files on disk are backed up separately and cheaply.

**Alternatives considered**: `LONGBLOB` columns (rejected for backup weight and query cost);
external object storage such as S3 (rejected: adds a cloud dependency and running cost to a
single-shop deployment that may run entirely on a local machine).

---

## R8. Backup and restore mechanism

**Decision**: A hosted background service inside the API triggers `mysqldump` on a daily
schedule, writing a timestamped, compressed dump to a configured backup directory, with a
retention policy of 30 daily copies. Manual `Backup Now` and `Restore` are Admin-only endpoints
wrapping the same routine. Restore runs `mysql` against the target schema.

**Rationale**: Keeps the whole system to one deployable unit — no separate cron configuration for
a shopkeeper to maintain, which serves FR-045's "without manual action". The scheduled trigger is
driven by the same injected `IClock` used for reporting, satisfying the constitution's
requirement that the backup schedule be testable with a mockable clock.

**Risk to accept**: a logical dump is not point-in-time. Losing the machine loses up to 24 hours
of sales. SC-012 only requires that a restore reproduces everything committed *before the backup
was taken*, so this satisfies the spec — but the owner should be told plainly, and the backup
directory should sit on a different physical device or a synced folder. Binary-log-based
point-in-time recovery is the upgrade path if the shop later finds 24 hours unacceptable.

**Alternatives considered**: Windows Task Scheduler / cron invoking `mysqldump` (rejected: config
lives outside the repo, and the manual Backup Now action would need a second code path); managed
cloud database backups (rejected: presumes cloud hosting).

---

## R9. Authentication token lifetime

**Decision**: Short-lived JWT access token (60 minutes) plus a long-lived, rotating,
server-persisted refresh token (30 days). Refresh tokens are revocable per user.

**Rationale**: A salesman stands at a counter for a full shift; a 15-minute token would force
re-login mid-sale and invite password sharing, which would destroy the audit trail's meaning
(FR-041). A 60-minute access token with silent refresh keeps the shift uninterrupted while
keeping the window short if a token leaks. Persisting refresh tokens gives the owner a way to
revoke a departed employee immediately — otherwise a stateless JWT stays valid until it expires.

**Alternatives considered**: long-lived access token with no refresh (rejected: unrevocable);
cookie-based sessions (viable, but the constitution mandates JWT).

---

## R10. Enforcing cost/profit confidentiality (FR-040)

**Decision**: Separate response DTOs per role rather than conditional field nulling. Products
serve as `ProductStaffDto` (no cost, no profit) or `ProductAdminDto`. Cost- and profit-bearing
endpoints carry an `[Authorize(Policy = "AdminOnly")]` attribute. An architecture test asserts
that no DTO reachable from a Staff-accessible endpoint exposes a cost or profit property.

**Rationale**: FR-040 requires refusal "wherever that data is requested", and the constitution
forbids UI-only hiding. Nulling a field on a shared DTO is one forgotten `if` away from a leak,
and the leak is invisible in the UI — exactly the failure the requirement exists to prevent.
Distinct types make the leak a compile-time impossibility for the fields that simply do not
exist on the Staff type. The architecture test catches the case where someone later adds a cost
field to the wrong DTO.

**Alternatives considered**: a JSON serialization filter driven by role claims (rejected: a
runtime behaviour that fails open and is easy to bypass by adding a new endpoint); nulling
fields in the service (rejected as above).

---

## R11. Returns and the latest-cost rule interaction

**Decision**: A sale return credits stock back at the cost recorded on the original sale line,
not the product's current cost. A purchase return reduces the supplier payable at the cost of
the purchase being returned. Neither return recalculates the product's current cost price.

**Rationale**: FR-011c fixes each sale line's cost at the moment of sale so history never
rewrites itself; a return is a reversal of that specific line and must use the same figure, or
FR-027 ("returned goods no longer contribute profit") would leave a residue equal to the cost
drift. The spec's edge case "a purchase return recorded after the cost has since changed"
resolves the same way, and explicitly states the current cost is not silently reverted.

**Open item flagged for the owner, not blocking**: after a purchase return, the product's current
cost still reflects the returned (possibly erroneous) purchase. If a purchase is entered at a
wrong cost and then returned, the owner must correct the cost via a stock/price adjustment. This
is a deliberate simplification consistent with the latest-cost model.

---

## R12. Frontend data fetching and state

**Decision**: Axios with a single configured client instance (interceptors for the auth header
and the error envelope) plus TanStack Query for server state. React Hook Form with Zod for form
validation. Local component state for the POS cart, with the cart's arithmetic extracted into
pure functions in a `lib/` module.

**Rationale**: Constitution Principle III requires that totals/discounts/balances logic be
component-tested and prefers pure functions. Keeping cart maths as pure functions makes the
heavy edge-case suite (zero discount, over-discount, partial payment, full credit) fast unit
tests with no rendering. TanStack Query removes hand-rolled loading/refetch state, which matters
for the dashboard's period toggle and the low-stock badge staying current after a sale.

**Alternatives considered**: Redux Toolkit (disproportionate for a single-shop app whose shared
state is mostly server state); raw `fetch` with `useEffect` (rejected: reinvents caching and
invalidation, and the constitution values testable logic over bespoke plumbing).

---

## Summary of decisions

| Ref | Area | Decision |
|---|---|---|
| R1 | Migrations | DbUp, embedded numbered SQL scripts |
| R2 | WhatsApp | `wa.me` deep link + tokenized public PDF URL (attachment not possible) |
| R3 | PDF | QuestPDF, Community licence (revenue < USD 1M) |
| R4 | Oversell | `SELECT ... FOR UPDATE`, id-ordered, `READ COMMITTED` |
| R5 | Money | `DECIMAL(12,2)`, C# `decimal`, no floats |
| R6 | Time | UTC storage, `Asia/Karachi` (+05:00) boundaries, injected `IClock` |
| R7 | Images | Files on disk, path in DB, 2 MB cap |
| R8 | Backup | In-process scheduled `mysqldump`, 30-day retention, Admin manual actions |
| R9 | Auth | 60-min JWT + 30-day rotating revocable refresh token |
| R10 | Confidentiality | Role-separated DTOs + AdminOnly policy + architecture test |
| R11 | Returns costing | Reverse at the originally recorded cost |
| R12 | Frontend | Axios + TanStack Query + React Hook Form/Zod, pure-function cart maths |

**No NEEDS CLARIFICATION items remain.**
