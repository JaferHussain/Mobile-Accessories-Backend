# Research: Invoice and Receipt Sharing

**Feature**: 009-invoice-sharing · **Date**: 2026-09-23

This feature is unusual: almost all of its behaviour already exists and is tested. Research here
is therefore less "which technology" and more "where does this belong, and what is genuinely
missing". Each decision below was checked against the code rather than assumed.

---

## R1 — What already exists, verified

| Capability | State | Where |
|---|---|---|
| Invoice PDF | Built, tested | `GET /api/documents/invoices/{id}/pdf` |
| Payment receipt PDF | Built, tested | `GET /api/documents/customer-payments/{id}/pdf` |
| Share link + `wa.me` deep link | Built, tested | `POST /api/documents/share-link` |
| Public document by token | Built, tested | `GET /api/public/documents/{token}` |
| Token expiry (30 days), revocation, access log | In the data model | `document_tokens`, `StoredDocumentToken.IsUsable` |
| `ShareButtons` component | Written, **mounted nowhere** | `frontend/src/features/documents/ShareButtons.tsx` |

**Decision**: Treat all of the above as fixed. This feature mounts it and closes the gaps.

**Rationale**: The rendering, tokenisation and containment were designed together and are covered
by tests. Re-opening them would risk the one customer-facing surface in the system for no gain.

---

## R2 — Where does "invoice history" live?

**Decision**: Two surfaces, not one.

1. **The customer ledger** already lists every invoice and payment for a customer, and each row
   carries `entryType` and `referenceId` — exactly the two values a share needs. Sharing mounts
   directly on the ledger row.
2. **A new minimal Invoices screen** (`/invoices`) is still required, because a **walk-in sale has
   no customer and therefore appears in no ledger**. Without it, a cash sale becomes unreachable
   the moment the counter resets, which fails SC-002.

**Rationale**: The ledger gives Stories 2 and 3 almost for free and is where the owner already
looks when a customer disputes something. But roughly every walk-in sale would be invisible, and
those are the majority of counter sales.

**Alternatives considered**:

- *Ledger only.* Rejected: walk-in invoices unreachable, SC-002 unmet.
- *A full invoice-management screen* with editing, voiding, filters by user and sale type.
  Rejected as scope creep — this feature needs "find a past bill and produce it", nothing more.
  `GET /api/invoices` already supports customer and date filtering, which is enough.

---

## R3 — How is printing produced?

**Decision**: Open the existing PDF in a new tab and let the browser print it. No print
stylesheet, no second rendering path.

**Rationale**: A separate print view is a second rendering of the same document, and the two
drift. The failure mode is the worst kind — the printed bill and the sent bill disagree, and
nobody notices until a customer holds both. The PDF is already branded and already the artifact
the customer receives.

**Alternatives considered**:

- *CSS `@media print` on a receipt component.* Rejected for the drift reason above, and because
  it would need the invoice re-rendered in HTML that nothing else uses.
- *Direct-to-thermal-printer integration.* Out of scope; the shop's printer setup is unknown and
  browser printing works with whatever is installed.

---

## R4 — Downloading a PDF through the API client

**Decision**: Fetch document bytes with an explicit `responseType: 'blob'`, and build the download
from an object URL.

**Rationale**: The shared axios client sets `Content-Type: application/json` and its response
interceptor unwraps the standard `ApiResponse` envelope. A PDF is neither — it is raw bytes with
no envelope. Left alone, the client would try to read a binary body as JSON and fail in a way that
looks like a server error.

**This is the same class of trap the codebase already documented**: posting `FormData` needed the
JSON `Content-Type` header deleted, or multipart uploads lost their boundary and the server
answered 415. Binary *responses* need the mirror-image handling.

**Alternatives considered**:

- *A plain `window.open` on the PDF URL.* Rejected: the endpoint requires a bearer token, and a
  new tab carries no Authorization header. It would 401.
- *Making the PDF endpoints anonymous.* Rejected outright — it would create a second
  unauthenticated surface, and an architecture test fails the build if one appears.

---

## R5 — Revocation: what is actually missing

**Decision**: Two new endpoints, both Admin-only.

- List the live share links for one document, so the owner can see what is outstanding.
- Revoke one link.

**Rationale**: `IDocumentTokenRepository.RevokeAsync` and the `revoked_at_utc` column both exist,
but **no endpoint calls them** — revocation is reachable only by editing the database. FR-117 is
therefore genuinely new work, not wiring. Listing is needed because revocation without knowing
what exists is not an action the owner can take: they cannot revoke a link they cannot see.

**Rationale for Admin-only**: revoking is a containment action over something already released,
which matches the owner's existing authority over corrective and destructive operations.

**Alternatives considered**:

- *Revoke-all-for-this-document.* Kept as a possible convenience but not required; revoking one
  link at a time is the precise action and the list makes it obvious.
- *Letting Staff revoke.* Rejected: a salesman who mis-sent a bill should tell the owner. The
  exposure is one customer's own bill, so the urgency does not justify widening authority.

---

## R6 — Enforcing "the customer never sees cost"

**Decision**: Assert FR-113 and FR-114 at the **architecture and integration level**, not through
the UI.

- An integration test fetches a real shared document through the public endpoint and asserts the
  bytes contain no cost, profit, or supplier text.
- The existing architecture test that fails the build when a second anonymous endpoint appears
  stays untouched and must keep passing.
- Payment proofs already live in their own directory (`content/payment-proofs`) precisely so that
  no rule meant for receipts can serve one. This feature adds nothing that could reach them.

**Rationale**: A screen test proves only that today's layout happens not to show cost. The
requirement is that it can never be reached by any route, which is a property of the endpoint and
the document, not of a component.

**Alternatives considered**:

- *Trusting the PDF template.* Rejected: the template is one edit away from including a cost
  column, and nothing would fail.

---

## R7 — When the customer has no usable mobile number

**Decision**: Keep the existing behaviour exactly — the send action is visible but disabled, with
a stated reason, while print and download stay available.

**Rationale**: `ShareButtons` already does this, and `CreateShareLinkAsync` already returns a null
`whatsAppUrl` when the stored number cannot be made into a dialable one. Hiding the button would
leave the shopkeeper wondering whether sending is supported at all; disabling it with a reason
tells them what to fix.

**Alternatives considered**:

- *Prompting for a number at share time.* Rejected **for a customer who has a record**: a number
  typed once and not stored helps this send and nothing afterwards, and the fix belongs on the
  customer record.

### R7a — Correction: a walk-in has no record to fix

**The rule above was drawn too broadly.** It is right for a known customer whose number is
missing. It is wrong for a **walk-in**, where there is no record to correct and never will be —
and walk-ins are the majority of counter sales. Applied unchanged, it would have left most
customers unable to receive their own bill.

**Decision**: when a sale has no customer at all, offer a number field at share time. Send to it.
Store nothing.

**Rationale**: the alternative — quick-creating a customer for every walk-in — would fill the
customer list with one-off buyers and undo the duplicate-customer problem this project has only
just fixed. The customer list should mean "someone with an ongoing relationship or a balance", not
everyone who ever bought a cable.

**A typed number never overrides a stored one** (FR-124). A stored number is the shop's own record
of where a bill was sent; silently sending somewhere else would make that question unanswerable.
So the field appears only when there is nothing on file.

---

## R8 — Does the counter need to hold the receipt longer?

**Decision**: No change. The POS receipt already outlives the cart and is cleared only by the
explicit **New sale** button.

**Rationale**: That behaviour was introduced to fix the "frozen screen", and it happens to be
exactly what sharing needs — a window in which the just-saved sale is still addressable. Sharing
mounts inside the existing receipt block.

---

## R9 — SMS, and an earlier answer that was too pessimistic

**Decision**: offer SMS alongside WhatsApp, through an `sms:` link that hands off to the counter
device's own messaging app. No gateway, no account, no per-message cost.

**This corrects an earlier assessment.** SMS was first called out as needing a paid gateway. That
is true of only one of the two approaches:

| Approach | Cost | Requires |
|---|---|---|
| `sms:` deep link — prepares the message, shopkeeper taps Send | none | the counter device has a SIM |
| SMS gateway (Twilio or a local provider) | per message | account, registered sender ID |

The first is **the same pattern `wa.me` already uses**. Neither sends anything from the server;
both prepare a message in an app the shopkeeper already has. Confirmed with the owner: the counter
runs on a phone/tablet with a SIM, so the deep link has somewhere to hand off to.

**Known caveat, accepted**: an SMS is 160 characters and a share link is long, so a send will
likely split into two or three messages and cost accordingly on a normal SIM plan. WhatsApp has no
such limit, which is part of why the system was built around it. SMS is the fallback for a customer
without WhatsApp, not the default.

**Alternatives considered**:

- *A paid SMS gateway.* Rejected for now — it introduces a running cost and a sender-ID
  registration to serve a case the deep link already covers. Still available later if the shop
  ever wants server-side sending.
- *A link shortener to fit 160 characters.* Rejected: a third-party dependency in the path of the
  shop's only customer-facing surface, in exchange for saving a message or two.

---

## R10 — The message carries the figures, not just a link

**Decision**: the server composes the message body from the **recorded ledger entry**, and it
states the customer's name, the amount received and the balance remaining. The link goes in as
well, for the formal receipt.

**Rationale**: a link is a request for the customer to do something. Most will not. Putting the
figures in the body means the acknowledgement works even if the link is never opened — which is
the whole point of acknowledging a payment against udhaar, the most disputed event in the shop.

**Composed on the server, never on the client** (FR-127). The figures must be the ones written to
the ledger. A message assembled from whatever the counter screen was holding can disagree with the
ledger, and a customer holding a message that contradicts the shop's own book is worse off than a
customer holding nothing. This is the same reasoning that makes the server recompute sale totals.

**Shape** (illustrative, not a format contract):

```
Moiz Mobile & Corporation
Hello Akhlaq — payment received.
Received: Rs 500.00
Remaining: Rs 500.00
Receipt: <link>
```

**Alternatives considered**:

- *Link only, as today.* Rejected: it acknowledges nothing to a customer who does not tap it.
- *Composing the text in the browser from the screen's numbers.* Rejected for the disagreement
  risk above, and because two places would then decide what a balance is.
- *A full statement of recent history in the message.* Rejected: it is a receipt, not an account
  statement, and every extra line costs an SMS part.

**SMS length** (FR-130): name, two figures and a link will exceed 160 characters and split into
two or three parts. The figures matter more than the link, so they come first — a truncated
message that still shows the balance has done its job.

---

## Open risks carried into design

1. **This is the shop's only customer-facing surface.** Every containment requirement exists for
   that reason; none should be relaxed for convenience.
2. **A bill sent to the wrong number cannot be unsent**, only revoked. The window is bounded by
   revocation, not closed by it.
3. **Link lifetime is configurable** (`ShareLinkExpiryDays`, default 30). The shopkeeper must be
   shown the actual expiry rather than a hardcoded "30 days" string, or the two will diverge the
   moment the setting changes.
