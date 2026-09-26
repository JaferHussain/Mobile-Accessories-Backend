# Feature Specification: Invoice and Receipt Sharing

**Feature Branch**: `009-invoice-sharing`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "Invoice and receipt sharing from the app. The backend already has PDF rendering for invoices and customer payment receipts, share-link minting with a wa.me deep link, and a token-based public document endpoint; the ShareButtons React component exists but is mounted on no screen, so a shopkeeper has no way to reach a PDF, print a bill, or send a customer their invoice. Cover: reaching Print/Download PDF/Share-on-WhatsApp from the POS receipt after a sale, from the invoice history/detail, and from a customer's ledger payment; who may share (Staff vs Admin); what the customer receives on an unauthenticated link and what must never appear there (cost, profit, payment proof screenshots); expiry and revocation of a share link; and behaviour when the customer has no mobile number on file."

## Why This Feature Exists

Every capability described below was **already built and tested** — and none of it can be reached.
The shop can produce a branded invoice PDF, mint an expiring share link, and compose a WhatsApp
message pointing at it. No screen offers any of these, so in practice a customer leaves the counter
with nothing, and the owner cannot reproduce a bill when one is disputed.

This feature is therefore almost entirely about **surfacing** existing behaviour. That framing
matters: the risk here is not building the wrong thing, it is exposing a customer-facing surface
carelessly and leaking what must never leave the shop.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Give the customer their bill at the counter (Priority: P1)

A sale has just been saved. The salesman needs to hand the customer something: a printed slip, or
a message on their phone. Today the confirmation names the invoice number and nothing else can be
done with it.

**Why this priority**: This is the moment the customer is standing there. Every other route to a
bill is a later correction of having missed it. It is also the only story that changes what the
shop looks like to a customer, and it is reachable the instant a sale completes.

**Independent Test**: Complete a sale — once to a known customer, once as a walk-in — then print,
download and send the bill without leaving the counter screen. Delivers the whole "customer walks
away with a receipt" outcome on its own.

**Acceptance Scenarios**:

1. **Given** a sale has just been saved, **When** the salesman opens the receipt actions, **Then**
   Print, Download PDF, Send on WhatsApp and Send by SMS are all offered.
2. **Given** the customer has a mobile number on file, **When** the salesman chooses either send
   action, **Then** a message is prepared to that number containing a link to the bill, and the
   shopkeeper taps Send in their own messaging app.
3. **Given** the sale was made to a walk-in, **When** the salesman chooses a send action, **Then**
   they are asked for a number, and sending proceeds to it without creating a customer record.
4. **Given** a customer already has a number on file, **When** the receipt actions are shown,
   **Then** no number field is offered — the stored number is used, and is the shop's record of
   where the bill went.
5. **Given** the salesman starts the next sale, **When** the counter resets, **Then** the previous
   receipt's actions are gone and nothing from that sale can be sent by accident.

---

### User Story 2 - Reproduce a bill that was never sent (Priority: P2)

A customer returns days later disputing a price, or asks for a copy. The owner opens the invoice
and produces the same document that was issued at the time.

**Why this priority**: This is the fallback for every bill that left without one, and it is what
makes a dispute settleable. It is second only because the counter moment prevents the need.

**Independent Test**: Open any past invoice from history and produce its PDF. Valuable even if
nothing at the counter changes.

**Acceptance Scenarios**:

1. **Given** any past invoice, **When** it is opened, **Then** the same sharing actions are offered
   as at the counter.
2. **Given** an invoice against which goods were later returned, **When** its document is produced,
   **Then** the figures shown match the invoice's current recorded value, not the original.
3. **Given** a share link was already created for this invoice, **When** another is requested,
   **Then** the customer can still open both and neither shows anything the other does not.

---

### User Story 3 - Acknowledge a payment against udhaar (Priority: P2)

A customer pays down their balance. They want proof of what they handed over and what is left.

**Why this priority**: Credit is the part of this business where memory and paper disagree most
often. A payment without an acknowledgement is the single most disputed event in the shop. Equal
priority to Story 2 because it is the same act against a different record.

**Independent Test**: Record a ledger payment, then produce and send its receipt.

**Acceptance Scenarios**:

1. **Given** a payment has been recorded against a customer's balance, **When** the receipt is
   produced, **Then** it shows the amount paid and the balance remaining after it.
2. **Given** the customer has a mobile number, **When** the receipt is sent, **Then** the message
   goes to that number.
3. **Given** a payment of 500 against a bill of 1,000, **When** the acknowledgement is sent,
   **Then** the message names the customer and states the 500 received and the 500 remaining, in
   the body — readable without opening anything.
4. **Given** the ledger recorded a different balance from what the screen showed, **When** the
   message is composed, **Then** it carries the ledger's figure, not the screen's.

---

### User Story 4 - Withdraw a link that should not have gone out (Priority: P3)

A bill was sent to the wrong number, or a customer's phone was lost. The owner stops the link
working without waiting for it to expire.

**Why this priority**: It is the containment that makes an unauthenticated link acceptable at all.
P3 because the link already expires by itself and the exposure is one customer's own bill — but the
owner should not have to wait a month to close a mistake.

**Independent Test**: Create a link, open it successfully, revoke it, confirm it no longer opens.

**Acceptance Scenarios**:

1. **Given** a live share link, **When** the owner revokes it, **Then** opening it afterwards fails.
2. **Given** a revoked link, **When** someone opens it, **Then** they cannot tell it was revoked
   rather than never existing.
3. **Given** a revoked link, **When** the shopkeeper shares that document again, **Then** a new
   working link is produced.

---

### Edge Cases

- **The customer has no mobile number.** Print and Download remain available; sending is offered
  but unavailable, and the screen says a number is needed and where to add it. It must never open a
  message addressed to nobody.
- **The stored number cannot be dialled** (letters, too short, a landline). Treated the same as no
  number: the action is refused with a reason rather than composing a message that fails silently.
- **A walk-in sale has no customer at all.** There is no stored number, so the shopkeeper is
  offered a field to type one for this send only. Nothing is created on the customer list — a
  one-off buyer is not a customer relationship, and recording them as one would fill the list with
  people who will never return.
- **The link has expired.** The customer sees the same "not found" as an unknown link — no hint
  that something existed, and no way to enumerate.
- **Someone guesses at links.** Repeated failures are limited and logged; a probe learns nothing
  from the response.
- **The document is opened on a phone.** It opens in the browser rather than forcing a download, so
  a customer with no PDF app can still read it.
- **The invoice was fully returned.** The document still exists and shows the invoice's recorded
  value; a bill is not deleted because the goods came back.
- **The same document is shared twice.** Both links work; sharing is not a one-shot action.

## Requirements *(mandatory)*

### Functional Requirements

Numbering continues the project's existing sequence.

**Reaching the documents**

- **FR-106**: Users MUST be able to print, download, and send an invoice from the counter
  immediately after a sale is saved, without leaving the sale screen.
- **FR-107**: Users MUST be able to produce the same document for any past invoice from the
  invoice history or an invoice's detail.
- **FR-108**: Users MUST be able to produce a payment receipt for any recorded customer ledger
  payment.
- **FR-109**: Printing MUST produce the same document the customer would receive, not a
  screen-styled approximation of it.

**Who may share**

- **FR-110**: Any signed-in user MUST be able to produce and send a document for a sale or payment,
  including Staff. The salesman is who serves the customer, and a bill the customer is entitled to
  is not privileged information.
- **FR-111**: Revoking a share link MUST be restricted to Admin.

**What the customer receives**

- **FR-112**: A shared document MUST show only what the customer is entitled to see: the shop's
  identity and contact details, the document number, date and time, their own name and number, the
  items with quantity, rate and discount, the totals, what was paid and what remains.
- **FR-113**: A shared document MUST NOT contain purchase cost, profit, margin, supplier
  information, or any other customer's details — consistent with the rule that cost never reaches a
  non-owner by any route.
- **FR-114**: A payment proof screenshot MUST NEVER be reachable through a share link. It is
  internal evidence held for the shop's own disputes, not part of the customer's bill.
- **FR-115**: A share link MUST resolve to exactly one document and MUST NOT expose any listing,
  search, or navigation to other records.

**Link lifetime and containment**

- **FR-116**: A share link MUST expire automatically, and the expiry MUST be visible to the
  shopkeeper at the moment of sharing so they know what they are handing over.
- **FR-117**: Admin MUST be able to revoke a live share link before it expires.
- **FR-118**: Unknown, expired, and revoked links MUST be indistinguishable from one another to
  whoever opens them.
- **FR-119**: Every access to a shared document MUST be recorded, so the owner can answer "was this
  ever opened, and when".
- **FR-120**: Creating a new link for a document MUST NOT invalidate links already issued for it.

**When sending is not possible**

- **FR-121**: When a sale HAS a customer on file but no usable mobile number, sending MUST be
  unavailable with a stated reason pointing at the customer record; printing and downloading MUST
  remain available.
- **FR-123**: When a sale has NO customer at all — a walk-in — the shopkeeper MUST be able to enter
  a mobile number at the moment of sharing and send to it. That number MUST NOT create a customer
  record, and MUST NOT be stored against the sale.
- **FR-124**: A number entered for a walk-in MUST NOT override a number already held for a
  customer. A stored number is the shop's own record of where a bill was sent, and silently
  sending somewhere else would make that question unanswerable.
- **FR-125**: Users MUST be able to send a document by SMS as well as WhatsApp, through the
  counter device's own messaging app. The shop MUST NOT be required to hold a messaging account or
  pay per message for this to work.

**What the message says**

- **FR-126**: A payment acknowledgement message MUST state the customer's name, the amount just
  received, and the balance still outstanding — in the message body itself, not only behind a
  link. A customer who never opens the link must still learn what they paid and what they owe.
- **FR-127**: Those figures MUST be the ones the server recorded on the ledger entry. The message
  MUST NOT be composed from numbers the counter screen was holding, because a message that
  disagrees with the ledger is worse than no message.
- **FR-128**: A sale that leaves a balance — udhaar or part paid — MUST be able to send the same
  kind of message: what was billed, what was paid, and what remains.
- **FR-129**: A message MUST contain only that customer's own figures. No cost, no profit, no
  other customer, and no running commentary on their history.
- **FR-130**: The SMS body MUST be kept as short as the figures allow. Every 160 characters is a
  separately charged message, so the link is included only where it fits within a reasonable
  number of parts.
- **FR-122**: A failure to produce or send a document MUST NOT affect the underlying sale or
  payment, and the screen MUST say so plainly.

### Key Entities

- **Shared document**: One invoice or one payment receipt, rendered for a customer. Carries no
  cost, profit, or evidence held for the shop's own purposes.
- **Share link**: A single-document, expiring, revocable means of reaching one shared document
  without signing in. Knows what it points at, when it stops working, and when it was last opened.
- **Share event**: A record that a document was made reachable — by whom, when, and for which
  document — and each time it was subsequently opened.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A salesman can give a customer their bill — printed or sent — within 15 seconds of
  saving the sale, without navigating away from the counter.
- **SC-002**: Any invoice from the last 12 months can be reproduced by the owner in under 30
  seconds, starting from the invoice list.
- **SC-003**: 100% of sales to a customer with a mobile number on file can be sent without the
  shopkeeper typing a phone number.
- **SC-008**: A walk-in customer who asks for their bill on their phone can be sent it, by WhatsApp
  or SMS, without a customer record being created for them.
- **SC-009**: Sending costs the shop nothing per message and requires no messaging account — both
  channels hand off to the counter device's own app.
- **SC-010**: A customer who receives a payment acknowledgement can tell what they paid and what
  they still owe without opening any link or asking anyone.
- **SC-011**: No acknowledgement message ever states a balance that differs from the customer's
  ledger.
- **SC-004**: A customer opening a shared link on a phone can read their bill without installing
  anything or signing in.
- **SC-005**: No shared document, under any combination of actions, reveals purchase cost, profit,
  a payment proof screenshot, or another customer's details — verified by test, not by inspection.
- **SC-006**: A link revoked by the owner stops working immediately, and the owner can confirm
  whether it had been opened before revocation.
- **SC-007**: Disputes that cannot be settled because no bill exists fall to zero for sales made
  after this feature ships.

## Assumptions

These are reasonable defaults taken rather than asked about. Each is cheap to change.

1. **Sharing is open to Staff; revoking is not.** A salesman serving a customer must be able to
   hand over that customer's bill. Revocation is a containment action over something already
   released, which fits the owner's existing authority over destructive and corrective actions.
2. **Printing goes through the same document the customer receives.** Producing the document and
   printing it from the browser avoids a second rendering that could drift from the first, and
   avoids the classic failure where the printed bill and the sent bill disagree.
3. **The existing 30-day link lifetime is retained.** Long enough for a customer to find the
   message weeks later, short enough that an abandoned link does not live forever. Shown to the
   shopkeeper rather than assumed.
4. **WhatsApp carries a link, not a file.** This is a constraint of the channel, not a choice, and
   is unchanged from the existing behaviour. SMS carries a link for the same reason.
5. **The counter runs on a phone or tablet with a SIM.** Confirmed by the owner. This is what makes
   SMS free: the system prepares the message and the device's own app sends it. On a desktop with
   no SIM the SMS action would have nothing to hand off to, and would need a paid gateway instead.
6. **No new document types.** Invoices and customer payment receipts only. Purchase documents,
   supplier statements, and customer account statements are out of scope.
7. **Existing documents keep their current content and layout.** This feature makes them reachable;
   redesigning them is separate work.
8. **Message wording is English, with the customer's name.** The shop's customers read Roman Urdu
   comfortably too; a Roman Urdu variant is a small change and worth asking the owner about before
   the first send rather than after.
9. **A walk-in's typed number is used once and not kept.** It is not stored on the sale and does
   not create a customer. The shop's record of the send is the share link itself, which already
   records who created it and when.

## Out of Scope

- Sending by email.
- **Server-side sending of any kind.** Both WhatsApp and SMS hand off to the counter device's own
  app with the message prepared; the shopkeeper taps Send. Nothing is dispatched by the system, so
  no messaging account, sender-ID registration or per-message cost is involved. A paid SMS gateway
  remains out of scope and would be a separate decision about money.
- Attaching the PDF itself to a WhatsApp message (the channel cannot carry one).
- Bulk sending — statements, reminders, or "send all today's bills".
- Customer-facing history: a link resolves to one document, never to a list.
- Any change to what the invoice or receipt document contains.
- Sharing purchase orders or supplier records.

## Dependencies

- Invoice and payment receipt rendering, share-link minting, and the single public document
  endpoint already exist and are tested; this feature depends on them unchanged.
- A customer's stored mobile number is what sending uses. Quality of that data determines how often
  FR-121 is hit.
- The shop's deployed address must be reachable from a customer's phone for a link to be openable.

## Risks

- **This feature creates the shop's only customer-facing surface.** Everything else is behind a
  sign-in. The containment requirements (FR-113 to FR-119) are the whole reason that is acceptable,
  and weakening any of them changes the system's exposure rather than just this feature's.
- **A bill sent to a wrong number cannot be unsent**, only revoked. FR-117 limits the window; it
  does not close the mistake.
