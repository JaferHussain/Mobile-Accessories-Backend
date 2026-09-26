---

description: "Task list for 009-invoice-sharing"
---

# Tasks: Invoice and Receipt Sharing

**Input**: Design documents from `/specs/009-invoice-sharing/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/documents.md](./contracts/documents.md)

**Tests**: MANDATORY. Constitution Principle I is non-negotiable — a failing test exists before the
implementation that makes it pass, and the task list is generated as pairs. Any implementation task
whose preceding test task is not red must be stopped, not "caught up later".

**Organization**: Grouped by user story so each ships independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4, mapping to the user stories in spec.md
- Exact file paths are given; a task without one is not actionable

## Path Conventions

Web application, per plan.md:

- Backend: `backend/src/MoizPos/` (layers as folders), tests in `backend/tests/`
- Frontend: `frontend/src/`, tests in `frontend/tests/`

---

## Phase 1: Setup

**Purpose**: Confirm the ground is what the plan assumed.

- [x] T001 Confirm no schema work is pending by running `dotnet run --project backend/src/MoizPos -- migrate --whatif`; expect "No pending scripts". This feature adds **no migration** (data-model.md) — if scripts are pending, stop and resolve that first
- [x] T002 [P] Create the frontend feature folder `frontend/src/features/invoices/` and the test folders `frontend/tests/features/documents/` and `frontend/tests/features/invoices/`
- [x] T003 [P] Create the backend test folder `backend/tests/MoizPos.IntegrationTests/Documents/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Two things must be true before sharing is reachable from any screen: a PDF can
actually be fetched through the client, and what the public endpoint serves is provably safe.

**⚠️ CRITICAL**: No user story may begin until this phase is complete. Exposing sharing before the
confidentiality guards exist would put the shop's only customer-facing surface into use unproven.

### Confidentiality guards — prove the existing surface is safe first

- [x] T004 [P] Write a failing integration test in `backend/tests/MoizPos.IntegrationTests/Documents/PublicDocumentContentTests.cs` asserting that a document served by `GET /api/public/documents/{token}` contains no purchase cost and no profit figure, for an invoice whose cost differs clearly from its price (FR-113)
- [x] T005 [P] Write a failing integration test in the same file asserting a shared document exposes no supplier name and no other customer's details (FR-113, FR-115)
- [x] T006 [P] Write a failing integration test in the same file asserting that an invoice carrying a `payment_proof_path` serves a document with no proof image and no route to one — proofs live in `content/payment-proofs` precisely so no rule meant for receipts can serve one (FR-114)
- [x] T007 Make T004–T006 pass. **Outcome**: all three passed on first run — the document models never carried cost, supplier or proof data. Kept as regression guards in `backend/tests/MoizPos.ArchitectureTests/SharedDocumentExposureTests.cs`, and **verified by injecting a `UnitCostPrice` property on `InvoiceDocument`, which reddened the guard**; reverted. A guard that cannot fail is worthless
- [x] T008 [P] Write a failing integration test in `backend/tests/MoizPos.IntegrationTests/Documents/PublicDocumentProbingTests.cs` asserting that an unknown token, an expired token and a revoked token all return an identical 404 with identical bodies (FR-118)
- [x] T009 Make T008 pass. **Outcome**: status codes already matched; the bodies differed only by the per-request `traceId`, which is issued before the token is examined and so leaks nothing. The assertion was too strict, not the code — the test now normalises `traceId` and compares everything else exactly
- [x] T010 Verify `backend/tests/MoizPos.ArchitectureTests` still passes unchanged — a test fails the build if a second anonymous endpoint appears, and this feature must reuse the one that exists

### Binary responses through the API client

- [x] T011 Write a failing test in `frontend/tests/api/binaryResponse.test.ts` asserting the shared client returns raw bytes for a PDF response and does not attempt to unwrap the `ApiResponse` envelope
- [x] T012 Implement blob handling in `frontend/src/api/client.ts`. **Outcome**: the error path needed the work, not the success path — a failed document request returns the ordinary JSON envelope *as a Blob*, and the response interceptor was converting it to a generic "unexpected error" before anything could read it. Handled centrally in the interceptor (already async), so `fetchBlob` stays trivial and every consumer gets the server's real reason. so a request made with `responseType: 'blob'` skips envelope unwrapping. This is the mirror image of the documented `FormData` trap, where the JSON `Content-Type` header had to be deleted or multipart uploads lost their boundary and the server answered 415 (research R4)
- [x] T013 [P] Write a failing test in `frontend/tests/features/documents/documentApi.test.ts` covering: fetching an invoice PDF, fetching a payment receipt PDF, and creating a share link
- [x] T014 Implement `frontend/src/features/documents/documentApi.ts` with `invoicePdf(id)`, `paymentReceiptPdf(id)` and `createShareLink(documentType, referenceId, mobileNumber?)`, per [contracts/documents.md](./contracts/documents.md)

**Checkpoint**: Documents can be fetched, and what a customer would receive is proven to contain
nothing it must not. User stories may now begin.

---

## Phase 3: User Story 1 — Give the customer their bill at the counter (Priority: P1) 🎯 MVP

**Goal**: The salesman can print, download or send the bill — by WhatsApp or SMS, to a known
customer or a walk-in who types their number — in the moment the customer is standing there, and
the counter is ready for the next sale immediately.

**Independent Test**: Complete a sale twice — once to a known customer, once as a walk-in — then
print, download and send by both WhatsApp and SMS without leaving the counter. Delivers the whole
"customer walks away with a receipt" outcome on its own.

### Tests for User Story 1 ⚠️ write first, confirm red

- [x] T015 [P] [US1] Write a failing test in `frontend/tests/features/documents/ShareButtons.test.tsx` asserting Print, Download PDF and Send on WhatsApp are all offered when a customer has a mobile number
- [x] T016 [P] [US1] Write a failing test in the same file asserting that for a sale WITH a customer but no usable number, the send actions are **disabled with a stated reason pointing at the customer record**, while Print and Download stay available (FR-121)
- [x] T016a [P] [US1] Write a failing test in the same file asserting Send by SMS is offered alongside WhatsApp and uses the returned `smsUrl` (FR-125)
- [x] T016b [P] [US1] Write a failing test in the same file asserting that for a **walk-in** — no customer at all — a number field is offered, and sending proceeds to the typed number (FR-123)
- [x] T016c [P] [US1] Write a failing test in the same file asserting the number field is **not** offered when a customer number is already on file, so a stored number can never be silently overridden (FR-124)
- [x] T017 [P] [US1] Write a failing test in the same file asserting Print opens the fetched PDF rather than rendering a separate print view; a second rendering path is a second document and the two drift (research R3)
- [x] T018 [P] [US1] Write a failing test in `frontend/tests/features/pos/PosReceipt.test.tsx` asserting that after a sale is saved the counter resets **instantly** — cart cleared, search ready — with no click required
- [x] T019 [P] [US1] Write a failing test in the same file asserting a transient "Sale saved" confirmation appears naming the invoice number, and disappears on its own for a **cash** sale
- [x] T020 [P] [US1] Write a failing test in the same file asserting that for a **non-cash** sale the receipt banner persists rather than auto-dismissing, because it carries the payment-proof upload and the sharing actions — an auto-dismissing banner would take an unfinished action away with it
- [x] T021 [P] [US1] Write a failing test in the same file asserting the persisting banner can be dismissed explicitly, and that dismissing it does not disturb a sale already in progress
- [x] T022 [P] [US1] Write a failing test in the same file asserting a failure to produce a document leaves the sale saved and says so plainly (FR-122)

### Backend for User Story 1 — the share-link contract change

- [x] T022a [P] [US1] Write a failing integration test in `backend/tests/MoizPos.IntegrationTests/Documents/ShareLinkChannelTests.cs` asserting `POST /api/documents/share-link` returns a non-null `smsUrl` in the form `sms:<number>?body=<link>` when a usable number exists (FR-125)
- [x] T022b [P] [US1] Write a failing integration test in the same file asserting that for a walk-in invoice a supplied `mobileNumber` produces both `whatsAppUrl` and `smsUrl` (FR-123)
- [x] T022c [P] [US1] Write a failing integration test in the same file asserting a supplied `mobileNumber` is **ignored** when the document's customer already has one on file — the stored number wins (FR-124)
- [x] T022d [P] [US1] Write a failing integration test in the same file asserting a supplied `mobileNumber` creates **no customer record** and is **not stored** against the invoice (FR-123)
- [x] T022e [P] [US1] Write a failing integration test in the same file asserting an unusable supplied number (letters, too short) yields null for both send URLs rather than a malformed link
- [x] T022f [US1] Add `SmsUrl` to `ShareLinkResult` and build the `sms:` deep link in `backend/src/MoizPos/Application/Services/DocumentService.cs`, beside the existing `wa.me` link and from the same normalised number
- [x] T022g [US1] Accept an optional `MobileNumber` on `ShareLinkRequest` in `backend/src/MoizPos/Api/Controllers/DocumentsController.cs` and thread it into `CreateShareLinkAsync`; bound its length in the validator
- [x] T022h [US1] In `DocumentService.CreateShareLinkAsync`, use the supplied number **only when the document has no customer number on file**, and never store it

### Implementation for User Story 1

- [x] T023 [US1] Add a Print action to `frontend/src/features/documents/ShareButtons.tsx` that opens the fetched PDF and invokes the browser's print, reusing the same bytes the customer receives
- [x] T024 [US1] **Outcome**: the cart already reset on save — what blocked the next customer was the explicit **New sale** button added earlier to fix the frozen screen. Removed; a cash confirmation now fades on its own (`confirmationVisibleMs`, injectable so a test need not wait six seconds), and only a non-cash banner stays, with a Dismiss. Changed `frontend/src/features/pos/PosScreen.tsx` so a saved sale resets the counter immediately (cart, discount, search, focus) instead of waiting for a click, replacing the current blocking **New sale** step
- [x] T025 [US1] Add a transient confirmation to `frontend/src/features/pos/PosScreen.tsx` naming the invoice number and total, auto-dismissing for a cash sale
- [x] T026 [US1] Keep the receipt banner persistent for a non-cash sale in `frontend/src/features/pos/PosScreen.tsx`, carrying the payment-proof upload and the sharing actions, with an explicit dismiss
- [x] T023a [US1] Add a Send by SMS action to `frontend/src/features/documents/ShareButtons.tsx` opening the returned `smsUrl`, alongside the existing WhatsApp action
- [x] T023b [US1] Add an optional number field to `frontend/src/features/documents/ShareButtons.tsx`, shown **only** when no number is on file, whose value is passed as `mobileNumber` when creating the share link — never shown when a stored number exists (FR-124)
- [x] T027 [US1] Mount `ShareButtons` in the receipt block of `frontend/src/features/pos/PosScreen.tsx`, passing `documentType="Invoice"`, the saved invoice id and the customer's stored mobile number (absent for a walk-in, which is what reveals the number field)
- [x] T028 [US1] Wire `frontend/src/features/pos/PosPage.tsx` to `documentApi` for download and share-link creation, leaving `PosScreen` a function of its props as the existing counter tests require
- [x] T029 [US1] Update the existing counter tests in `frontend/tests/features/pos/PosScreen.test.tsx` and `CartProvider.test.tsx` that assert the old **New sale** button, so they drive the new reset behaviour while keeping every business assertion they already make

**Checkpoint**: A customer can leave the counter with their bill, and the counter is never a dead
end. This is the MVP — stop and validate here.

---

## Phase 4: User Story 2 — Reproduce a bill that was never sent (Priority: P2)

**Goal**: Any past invoice, including a walk-in sale that belongs to no customer, can be found and
produced.

**Independent Test**: Open an invoice from an earlier day — one with a customer and one walk-in —
and produce each PDF.

### Tests for User Story 2 ⚠️ write first, confirm red

- [x] T030 [P] [US2] Write a failing test in `frontend/tests/features/invoices/InvoicesPage.test.tsx` asserting the screen lists invoices newest first with number, date, customer name (or "Walk-in"), and total
- [x] T031 [P] [US2] Write a failing test in the same file asserting a **walk-in** invoice appears and can be produced — this is the case the customer ledger cannot cover, and the reason this screen exists (research R2)
- [x] T032 [P] [US2] Write a failing test in the same file asserting the list can be narrowed by date range and by customer, using the filters `GET /api/invoices` already accepts
- [x] T033 [P] [US2] Write a failing test in the same file asserting sharing actions are offered per invoice row
- [x] T034 [P] [US2] Write a failing test in `frontend/tests/features/customers/CustomerLedgerSharing.test.tsx` asserting an `Invoice` ledger row offers sharing addressed to that row's `referenceId`
- [x] T035 [P] [US2] **Outcome**: already true — `net_amount` is what the document renders, and it is reduced by every return. Kept as a regression guard. Write a failing integration test in `backend/tests/MoizPos.IntegrationTests/Documents/InvoiceDocumentTests.cs` asserting that an invoice against which goods were later returned renders at its **current** recorded value, not the original

### Implementation for User Story 2

- [x] T036 [P] [US2] **Also needed a backend change**: `GET /api/invoices` returned the raw invoice with no customer name, so a walk-in was indistinguishable from a sale to someone. Added `InvoiceListRow` with a joined `customerName`, left NULL for a walk-in rather than an invented label — wording "nobody" is the screen's job. Create `frontend/src/features/invoices/invoiceApi.ts` wrapping `GET /api/invoices` with optional `customerId`, `from`, `to`, `page`, `pageSize`
- [x] T037 [US2] Create `frontend/src/features/invoices/InvoicesPage.tsx` listing invoices with date and customer filters, showing "Walk-in" where `customerId` is null
- [x] T038 [US2] Mount `ShareButtons` per row in `frontend/src/features/invoices/InvoicesPage.tsx`
- [x] T039 [US2] Add the `/invoices` route to `frontend/src/routes/AppRoutes.tsx` and a nav entry to `frontend/src/components/AppShell.tsx`, placed with the daily counter screens rather than the back-office ones
- [x] T040 [US2] Mount `ShareButtons` on `Invoice` rows in `frontend/src/features/customers/CustomerLedger.tsx`, mapping `entryType` → `documentType` and `referenceId` → the document — both already on the row, so no lookup is needed
- [x] T041 [US2] Make T035 pass if it fails; if it already passes, keep it as a regression guard

**Checkpoint**: Every past sale is reachable, whether or not it has a customer.

---

## Phase 5: User Story 3 — Acknowledge a payment against udhaar (Priority: P2)

**Goal**: A customer who pays down their balance gets proof of what they handed over and what is
left.

**Independent Test**: Record a ledger payment, then produce and send its receipt.

### Tests for User Story 3 ⚠️ write first, confirm red

- [x] T042 [P] [US3] Write a failing test in `frontend/tests/features/customers/CustomerLedgerSharing.test.tsx` asserting a `Payment` ledger row offers sharing with `documentType="PaymentReceipt"` and that row's `referenceId`
- [x] T043 [P] [US3] Write a failing integration test in `backend/tests/MoizPos.IntegrationTests/Documents/PaymentReceiptTests.cs` asserting the rendered receipt states the amount paid and the balance remaining after it
- [x] T044 [P] [US3] Write a failing test in `frontend/tests/features/customers/CustomerLedgerSharing.test.tsx` asserting a customer with no mobile number gets Print and Download but a disabled send with a reason

### Message body tests for User Story 3 ⚠️ write first, confirm red

- [x] T044a [P] [US3] Write a failing integration test in `backend/tests/MoizPos.IntegrationTests/Documents/PaymentMessageTests.cs` asserting the share-link message body for a `PaymentReceipt` names the customer, the amount received and the balance remaining (FR-126)
- [x] T044b [P] [US3] **Found a real bug**: the message read `customers.outstanding_balance` — the customer's balance TODAY, not the one recorded at that payment. Re-sharing an old receipt after the customer bought again restated it and contradicted the ledger. Now reads `ledger_entries.balance_after` for that payment. Write a failing integration test in the same file asserting those figures come from the **recorded ledger entry** — record a payment, then assert the message states the ledger's `balance_after`, not any figure supplied by the caller (FR-127)
- [x] T044c [P] [US3] Write a failing integration test in the same file asserting the message contains no cost, no profit and no other customer's details (FR-129)
- [x] T044d [P] [US3] Write a failing integration test in the same file asserting the message for an invoice that left a balance states what was billed, paid and remains (FR-128)
- [x] T044e [P] [US3] Write a failing unit test in `backend/tests/MoizPos.UnitTests/Documents/MessageComposerTests.cs` covering the pure composition: a full payment says nothing remains; a part payment states both figures; a zero balance is phrased as settled, not as "remaining Rs 0.00"

### Implementation for User Story 3

- [x] T045 [US3] Extend the ledger row sharing in `frontend/src/features/customers/CustomerLedger.tsx` to handle `Payment` rows, passing the customer's stored mobile number through
- [x] T046 [US3] Offer sharing immediately after a payment is recorded in `frontend/src/features/customers/ReceivePaymentModal.tsx`, so the acknowledgement happens while the customer is present rather than requiring them to be found again in the ledger
- [x] T046a [US3] **Moved rather than added**: the wording lived on `WhatsAppLinkBuilder`, which stopped being its owner the moment SMS needed the same words. Now `DocumentMessages`, pure and shared by both channels. Add a pure message composer in `backend/src/MoizPos/Application/Documents/` that turns a document plus its recorded figures into a message body, with no I/O — keeping it testable and out of the service, per the constitution's preference for pure calculation
- [x] T046b [US3] Use that composer in `DocumentService.CreateShareLinkAsync` so `whatsAppUrl` and `smsUrl` carry the figures in their prefilled text, reading the amounts from the ledger entry rather than from the request
- [x] T046c [US3] Keep the SMS body shortest — figures first, link last — since every 160 characters is a separately charged message (FR-130)
- [x] T047 [US3] Make T043 pass if it fails; if it already passes, keep it as a regression guard

**Checkpoint**: The most-disputed event in the shop now leaves the customer holding proof.

---

## Phase 6: User Story 4 — Withdraw a link that should not have gone out (Priority: P3)

**Goal**: The owner can see which links are outstanding for a document and stop one working.

**Independent Test**: Create a link, open it, revoke it, confirm it no longer opens, then share
again and confirm the new link works.

### Tests for User Story 4 ⚠️ write first, confirm red

- [x] T048 [P] [US4] Write a failing integration test in `backend/tests/MoizPos.IntegrationTests/Documents/ShareLinkRevocationTests.cs` asserting `GET /api/documents/share-links?documentType=&referenceId=` returns each link's `id`, `createdAtUtc`, `createdByUserName`, `expiresAtUtc`, `revokedAtUtc`, `lastAccessedAtUtc` and `isUsable`
- [x] T049 [P] [US4] Write a failing integration test in the same file asserting the listing **never returns the token itself** — only the hash is stored, and re-exposing a live link through an authenticated screen would turn a bystander into a link holder (contracts)
- [x] T050 [P] [US4] Write a failing integration test in the same file asserting `lastAccessedAtUtc` is populated after the link has been opened once, and null before (FR-119)
- [x] T051 [P] [US4] Write a failing integration test in the same file asserting `POST /api/documents/share-links/{id}/revoke` stops the link resolving, and that the public endpoint then returns the same 404 as an unknown token
- [x] T052 [P] [US4] **Already idempotent by construction**: `RevokeAsync` writes only `WHERE revoked_at_utc IS NULL`, so a second call cannot move the original timestamp. Kept as a regression guard. Write a failing integration test in the same file asserting revocation is **idempotent**: a second revoke returns the same success and leaves the original `revokedAtUtc` unchanged, because that timestamp is evidence of when the withdrawal actually happened
- [x] T053 [P] [US4] Write a failing integration test in the same file asserting revoking one link does not affect another link for the same document (FR-120)
- [x] T054 [P] [US4] Write a failing integration test in the same file asserting both endpoints are refused to a Staff principal and allowed to Admin (FR-111)
- [x] T055 [P] [US4] Write a failing test in `frontend/tests/features/documents/ShareLinksPanel.test.tsx` asserting the panel lists outstanding links with expiry and last-opened, offers revoke, and is shown only to an Admin

### Implementation for User Story 4

- [x] T056 [US4] Add `ListForDocumentAsync(documentType, referenceId)` to `IDocumentTokenRepository` in `backend/src/MoizPos/Application/Abstractions/IDocumentAbstractions.cs`, returning a row type carrying id, created at, created-by user name, expiry, revoked at and last accessed — `RevokeAsync` already exists and needs no change
- [x] T057 [US4] Implement that query in `backend/src/MoizPos/Infrastructure/Repositories/DocumentTokenRepository.cs`, joining `users` for the creator's name, ordered newest first
- [x] T058 [US4] Add `ListShareLinksAsync` and `RevokeShareLinkAsync` to `IDocumentService` and `DocumentService` in `backend/src/MoizPos/Application/Services/DocumentService.cs`, with revocation returning the existing `revokedAtUtc` untouched when the link is already revoked
- [x] T059 [US4] Add `GET /api/documents/share-links` and `POST /api/documents/share-links/{id}/revoke` to `backend/src/MoizPos/Api/Controllers/DocumentsController.cs`, both declared `[Authorize(Policy = Policies.AdminOnly)]` — authorization on the endpoint, never by hiding a control
- [x] T060 [US4] Extend `frontend/src/features/documents/documentApi.ts` with `listShareLinks` and `revokeShareLink`
- [x] T061 [US4] Create `frontend/src/features/documents/ShareLinksPanel.tsx` listing outstanding links for a document with expiry and last-opened, and a revoke action, rendered only for an Admin
- [x] T062 [US4] Mount `ShareLinksPanel` alongside the sharing actions on `frontend/src/features/invoices/InvoicesPage.tsx` and `frontend/src/features/customers/CustomerLedger.tsx`

**Checkpoint**: The containment that makes an unauthenticated link acceptable is now reachable by
the owner, not only by editing the database.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T063 [P] Show the returned `expiresAtUtc` wherever a link is created or listed, never a hardcoded "30 days" — the lifetime is configurable via `ShareLinkExpiryDays` and the two would diverge silently the moment it changes (research R8)
- [x] T064 [P] Add styles for the sharing actions, the transient confirmation and the links panel in `frontend/src/index.css`
- [x] T064a [P] Record in `CLAUDE.md` that both send channels are **deep links, never server-side sends** — `wa.me` and `sms:` prepare a message in the counter device's own app, so the shop holds no messaging account and pays nothing per message; and that a walk-in's typed number is used once, never stored and never allowed to override a stored one
- [x] T065 [P] Record the durable rules in `CLAUDE.md`: PDF responses bypass the JSON envelope; print uses the same bytes the customer receives; a share-link listing returns ids and never tokens; revocation is idempotent and does not move the original timestamp; a walk-in sale appears in no ledger, which is why the Invoices screen exists
- [x] T063a **Fixed after first use**: every Print and Download returned 404 — the client used a `/documents/` prefix the PDF routes never had. Client and contract corrected to the live routes (changing a deployed route for tidiness would be risk for no gain), and `ClientDocumentRouteTests` added to hold the literal paths the browser sends.
- [x] T063b **Fixed after first use**: the Invoices table rendered four buttons, a hint, a number field and a links panel against every row, and an error from one row pushed the others around. A row is now one **Give to customer** button opening a dialog that names the sale.
- [ ] T066 Run every scenario in [quickstart.md](./quickstart.md), including the probing checks in 5b and the anonymous-surface check in 5c
- [x] T067 Run the full suites — `cd backend && dotnet test`, `cd frontend && npm run test && npx tsc --noEmit` — and confirm nothing previously green broke (Constitution Principle VI)

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: depends on Setup — **blocks every user story**
- **US1 (Phase 3)**: depends on Foundational
- **US2 (Phase 4)**: depends on Foundational; independent of US1
- **US3 (Phase 5)**: depends on Foundational; shares the ledger file with US2, so T045 follows T040
- **US4 (Phase 6)**: depends on Foundational; its panel mounts on screens built in US2, so T062 follows T037
- **Polish (Phase 7)**: depends on the stories being delivered

### Why Foundational genuinely blocks

Not ceremony. T004–T010 assert that what a customer receives contains no cost, no proof and no
route to another record. Mounting a share button before those pass would put the shop's only
customer-facing surface into use unproven — and a leak there is not recoverable by a later fix,
because the link has already been sent.

### Within each story

- The test task is written and **failing** before its implementation task
- Backend: repository → service → controller
- Frontend: api module → component → mount → route

### Parallel opportunities

- T002 and T003 (different folders)
- T004, T005, T006 (different assertions, same new file — coordinate or write sequentially)
- T015–T022 (US1 tests across two test files)
- T030–T035 (US2 tests)
- T048–T055 (US4 tests, mostly one new file)
- T036 with T030–T035 (api module is a different file from the tests)
- US2, US3 and US4 can proceed in parallel once Foundational is done, if staffed

---

## Parallel Example: User Story 1

```bash
# Write the counter tests together, confirm all red:
Task: "ShareButtons offers Print, Download and Send — frontend/tests/features/documents/ShareButtons.test.tsx"
Task: "Send is disabled with a reason when no mobile number — same file"
Task: "Counter resets instantly after a sale — frontend/tests/features/pos/PosReceipt.test.tsx"
Task: "Cash sale shows a transient confirmation — same file"
Task: "Non-cash sale keeps a persistent banner — same file"
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 Setup
2. Phase 2 Foundational — **critical, blocks everything**
3. Phase 3 User Story 1
4. **Stop and validate**: a customer leaves the counter with their bill, and the counter is ready
   for the next one instantly
5. Deploy

That alone fixes the complaint that the counter forces a click through an image picker before the
next sale, and it is the highest-value slice.

### Incremental delivery

1. Setup + Foundational → the surface is proven safe
2. US1 → the counter moment → deploy (MVP)
3. US2 → past bills, including walk-ins → deploy
4. US3 → payment acknowledgements → deploy
5. US4 → revocation → deploy

Each adds value without breaking what came before.

---

## Notes

- `[P]` means different files and no dependency on an incomplete task
- Tests are not optional here — the constitution makes them the gate, and the task list is paired
  deliberately so an implementation task without a red test preceding it is visibly out of order
- Commit after each task or logical group
- Any checkpoint is a safe place to stop
- **SMS is out of scope.** WhatsApp carries a link because `wa.me` cannot attach a file; SMS would
  need a paid gateway and a registered sender, which is a separate decision
