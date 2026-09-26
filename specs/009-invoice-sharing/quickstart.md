# Quickstart: Invoice and Receipt Sharing

**Feature**: 009-invoice-sharing · **Date**: 2026-09-23

How to prove this feature works end to end. Each scenario maps to a user story in
[spec.md](./spec.md) and can be run on its own.

## Prerequisites

```bash
# schema (no new migrations for this feature, but the database must be current)
dotnet run --project backend/src/MoizPos -- migrate --whatif   # expect: no pending scripts

# run
dotnet run --project backend/src/MoizPos      # http://localhost:5080
cd frontend && npm run dev                    # http://localhost:5173
```

Sign in as the owner. Have at least one customer **with** a mobile number and one **without**.

## Automated checks

```bash
cd backend  && dotnet test
cd frontend && npm run test && npx tsc --noEmit
```

Both suites must be green before and after. See [contracts/documents.md](./contracts/documents.md)
for endpoint shapes and [data-model.md](./data-model.md) for what a shared document may contain.

---

## Scenario 1 — Give the customer their bill at the counter (US1)

1. Sell one item to a customer who **has** a mobile number.
2. On the receipt that appears, use **Print**, **Download PDF**, and **Send on WhatsApp**.

**Expect**: the PDF carries the shop name, the invoice number, the items, and the totals. WhatsApp
opens addressed to that customer's number, with a **link** in the message body — not a file
attachment, which the channel cannot carry.

3. Press **New sale**.

**Expect**: the receipt and its sharing actions are gone. Nothing from the previous sale can be
sent by accident.

### 1b — A walk-in with no customer

Sell without attaching a customer.

**Expect**: Print and Download work; **Send on WhatsApp is disabled** with a stated reason. It must
never open a message addressed to nobody (FR-121).

---

## Scenario 2 — Reproduce a bill nobody sent (US2)

1. Open **Invoices**, find a sale from an earlier day, produce its PDF.

**Expect**: the same document as at the counter, reachable in well under 30 seconds (SC-002).

2. Find a **walk-in** sale from an earlier day and produce its PDF.

**Expect**: it is reachable. This is the case the customer ledger cannot cover — a walk-in belongs
to no customer and appears in no ledger, which is why the Invoices screen exists (research R2).

3. Take a return against an invoice, then produce that invoice's document.

**Expect**: the figures match the invoice's **current** recorded value, not the original.

---

## Scenario 3 — Acknowledge a payment against udhaar (US3)

1. Open a customer with a balance, receive a payment.
2. From that payment's row in the ledger, produce and send the receipt.

**Expect**: the receipt shows the amount paid and the balance remaining after it, and sending goes
to that customer's stored number.

---

## Scenario 4 — Withdraw a link (US4)

1. Share an invoice. Copy the link from the WhatsApp message.
2. Open the link in a **private window** (no sign-in).

**Expect**: the bill opens in the browser, inline — no download prompt, no login, nothing to
install (SC-004).

3. As the owner, list the share links for that invoice.

**Expect**: the link is listed with its expiry and **when it was last opened** — the open you just
made (FR-119). The token itself is **not** shown; only an id (contracts).

4. Revoke it. Reload the private window.

**Expect**: not found. Revoke it again.

**Expect**: the same success, with the original revocation time unchanged — idempotent.

5. Share the same invoice again.

**Expect**: a new working link. Revoking the first did not poison the document (FR-120).

---

## Scenario 5 — What the customer must never see (FR-113, FR-114)

This is the one to be most careful with, because failures here are invisible and permanent.

1. Sell an item whose cost differs clearly from its price.
2. Attach a **payment proof** screenshot to a non-cash sale.
3. Share that invoice and open the public link signed out.

**Expect**, in the served document:

- no purchase cost, no profit, no margin
- no supplier name
- **no payment proof**, and no route to one
- no other customer's details
- no list, search, or navigation — one document, nothing else

Verified by test rather than by eye: a screen check proves only that today's layout happens not to
show cost.

### 5b — Probing

1. Request `/api/public/documents/definitely-not-a-token`.
2. Request an **expired** token, and a **revoked** one.

**Expect**: three identical 404s. Nothing distinguishes "never existed" from "withdrawn"
(FR-118). Repeated attempts are rate-limited and logged.

### 5c — The anonymous surface has not grown

```bash
cd backend && dotnet test --filter "FullyQualifiedName~Architecture"
```

**Expect**: green. A test fails the build if a second anonymous endpoint appears — this feature
must reuse the one that exists, never add another.

---

## Scenario 6 — Failure does not damage the sale (FR-122)

1. Stop the backend. With the counter still open on a saved receipt, press **Download PDF**.

**Expect**: a plain message that the document could not be produced. The sale stays saved, the
receipt stays on screen, and nothing suggests the sale failed.

2. Restart the backend and press it again.

**Expect**: it works, with no repair step.
