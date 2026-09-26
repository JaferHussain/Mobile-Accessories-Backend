# Feature 008: Payment proof on a non-cash sale

**Created**: 2026-09-22 | **Status**: Draft

## The problem

Cash is its own proof — the money is in the drawer. A bank transfer, JazzCash, EasyPaisa or Raast
payment is a claim: the customer says it was sent, the counter takes their word, and the only
record is a screenshot on somebody's phone. When the money does not arrive, there is nothing in
the system to look at.

## Goal

Keep the screenshot with the invoice it belongs to.

## Requirements

- **FR-001**: An invoice whose payment method is **not Cash** MUST be able to carry one picture as
  payment proof.
- **FR-002**: The picture is **optional**. A sale MUST never be blocked or refused for want of
  one — the counter cannot be stopped because a customer's screenshot is slow to arrive.
- **FR-003**: A **Cash** sale MUST NOT offer to attach proof. There is nothing to prove, and an
  unused control on the busiest screen is noise.
- **FR-004**: The proof MUST be attachable after the sale is saved, from the invoice, so a
  screenshot that arrives a minute later still reaches the right sale.
- **FR-005**: Replacing a proof MUST remove the previous file, leaving no orphans.
- **FR-006**: Accepted formats and size limit MUST match product pictures (JPEG/PNG/WebP, 2 MB);
  a file that is not a readable image MUST be refused.
- **FR-007**: Any signed-in user may attach and view a proof for now. (Stated explicitly: a
  salesman takes the payment, so a salesman must be able to record its proof. The owner may
  narrow this later.)
- **FR-008**: A proof MUST NOT be exposed through the public receipt link. That link is an
  unauthenticated, customer-facing document; a payment screenshot is internal evidence and has no
  business being reachable by anyone holding a receipt URL.
- **FR-009**: No thumbnail is generated. A proof is opened full-size when a dispute arises, or not
  at all — it never appears in a list.

## Success criteria

- **SC-001**: For any non-cash sale, the owner can open the invoice and see the payment
  screenshot that was attached to it.
- **SC-002**: A sale completes at the same speed whether or not a proof is attached.
- **SC-003**: No payment proof is reachable from a customer's receipt link.

## Notes for planning

Mirrors feature 005's storage design: file on disk, path in `invoices.payment_proof_path`, a
second-step upload addressed to an invoice that already exists. `ImageStorageService` already
does the validation, safe naming and decode-check; the thumbnail step is the only part that does
not apply. `PaymentMethod` already distinguishes Cash from the rest, so "is this non-cash?" is an
existing domain fact, not a new one.
