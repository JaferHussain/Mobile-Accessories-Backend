# Data Model: Invoice and Receipt Sharing

**Feature**: 009-invoice-sharing · **Date**: 2026-09-23

## Schema changes

**None.** This feature adds no tables and no columns.

That is worth stating explicitly rather than leaving as an absence: every piece of state this
feature needs was created by migration `0012_document_tokens.sql` and has been carrying it since.
A plan that quietly added a column here would be duplicating what is already stored.

---

## Existing entities this feature reads

### `document_tokens` (migration 0012)

One share link. The table was designed for exactly this use and nothing about it changes.

| Column | Meaning | Why it matters here |
|---|---|---|
| `id` | Identity | What a revoke request names |
| `token_hash` | SHA-256 of the token | **Only the hash is stored.** A database leak does not yield working links, and a link cannot be recovered from the table — it exists only in the message that carried it |
| `document_type` | `Invoice` or `PaymentReceipt` | Decides which renderer serves the bytes |
| `reference_id` | The one invoice or payment | A link resolves to exactly one document (FR-115) |
| `expires_at_utc` | When it stops working | Shown to the shopkeeper at share time (FR-116) |
| `revoked_at_utc` | Set when withdrawn, else NULL | The new revoke endpoint writes this (FR-117) |
| `last_accessed_at_utc` | Updated on each successful open | Answers "was it ever opened" (FR-119) |
| `created_by_user_id` | Who shared it | Accountability for a customer-facing release |

**Usability rule** (already implemented, unchanged):

```
usable = revoked_at_utc IS NULL AND expires_at_utc > now
```

Unknown, expired and revoked are **indistinguishable to the caller** — all three return the same
404 (FR-118). This is a deliberate property, not an oversight: a differentiated response would let
a prober learn that a token once existed.

### `invoices`, `invoice_items`, `ledger_entries`

Read only, to render a document. No new columns, no new writes.

`ledger_entries` matters for navigation rather than content: each row already carries `entry_type`
and `reference_id`, which is precisely the `(documentType, referenceId)` pair a share needs. The
customer ledger can therefore offer sharing per row with no lookup and no new field.

---

## What must never appear in a shared document

These are constraints on the *rendered output*, enforced by test (see research R6). They are part
of the data model in the sense that they describe what may cross the boundary.

| Must never appear | Why |
|---|---|
| `products.cost_price`, `invoice_items.unit_cost_price` | Cost never reaches a non-owner by any route (FR-040, FR-113) |
| Any profit or margin figure | Same rule |
| Supplier names, payables | The customer's bill is not a window into the shop's buying |
| `invoices.payment_proof_path` | Internal evidence for the shop's own disputes, not part of the bill (FR-114). Proofs live in their own directory so no rule meant for receipts can serve one |
| `payment_account_number`, `payment_transaction_id` | The customer supplied these; echoing them back on a public link is needless exposure |
| Any other customer's details | A link resolves to one document (FR-115) |

---

## Entity relationships

```
customer ──< invoice ──────────┐
   │                            ├──> document_token ──> share link ──> customer's phone
   └──< ledger_entry (Payment) ─┘         (hash only)
```

A token points *at* a document; a document knows nothing about its tokens. That direction is what
makes "share the same bill twice" safe — a second token is simply another row, and neither affects
the other (FR-120).

---

## Lifecycle of a share link

```
created ──(30 days elapse)──> expired ─┐
   │                                    ├──> 404, identical response
   └──(owner revokes)────────> revoked ─┘

created ──(customer opens)──> last_accessed_at_utc updated, still usable
```

No state is ever deleted. A revoked or expired token stays in the table so the owner can still
answer "was this opened before I withdrew it" (FR-119).

---

## Derived values (not stored)

| Value | Derived from | Why not stored |
|---|---|---|
| `shareUrl` | Public base address + the raw token | The raw token is never persisted; storing the URL would store the token |
| `whatsAppUrl` | `wa.me` + the customer's number + `shareUrl` | Depends on the customer's *current* number; a stored copy would go stale on the next edit |
| `expiresAtUtc` shown to the shopkeeper | `now + ShareLinkExpiryDays` at creation | Configurable — a hardcoded "30 days" in the UI would diverge the moment the setting changes (research R8) |
| "is usable" | `revoked_at_utc` and `expires_at_utc` | Two facts already recorded; a third boolean would be a third thing to keep in step |
