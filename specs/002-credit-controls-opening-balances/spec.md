# Feature Specification: Credit Controls & Customer Opening Balances

**Feature Branch**: `002-credit-controls-opening-balances`

**Created**: 2026-09-10

**Status**: Draft

**Input**: User description: "just the admin user can make sale on credit(udhar) / partial payment should be allowed during recovery of the saled amount / any customer's previous record that have some pending amount should be recorded"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Only the owner may let goods leave on credit (Priority: P1)

The shop owner decides who is trusted with udhaar. Today anyone signed in can complete a sale
that leaves money unpaid, which means the salesman can extend the shop's credit without the
owner's knowledge. From now on, a sale that would leave any amount outstanding can only be
completed by the owner. The salesman continues to sell normally for full payment, and continues
to collect money owed from customers who come in to pay.

**Why this priority**: This is the money-at-risk item. Every credit sale the owner did not
sanction is stock that has left the shop against a debt the owner did not agree to, and it is
the one thing here that cannot be corrected after the fact — the goods are already gone.

**Independent Test**: Sign in as the salesman, build a sale, and try to complete it with less
than the full amount paid. The sale must be refused with a clear reason and no stock must move.
Sign in as the owner and complete the same sale successfully.

**Acceptance Scenarios**:

1. **Given** a salesman is signed in and has a sale worth Rs 5,000 with a customer selected,
   **When** they try to complete it having taken nothing, **Then** the sale is refused, they are
   told that only the owner can approve udhaar, the stock is unchanged and no invoice exists.
2. **Given** a salesman is signed in with a sale worth Rs 5,000, **When** they try to complete it
   having taken Rs 3,000, **Then** the sale is refused for the same reason — a part-paid sale
   still leaves the shop's money with the customer.
3. **Given** a salesman is signed in with a sale worth Rs 5,000, **When** they take the full
   Rs 5,000, **Then** the sale completes normally.
4. **Given** the owner is signed in with a sale worth Rs 5,000 and a customer selected,
   **When** they complete it having taken Rs 2,000, **Then** the sale completes, the customer's
   balance rises by Rs 3,000, and the ledger records it.
5. **Given** a customer owes Rs 4,000, **When** the salesman receives Rs 1,000 from them,
   **Then** the payment is accepted — collecting money owed is not extending credit.
6. **Given** a salesman is signed in, **When** the point-of-sale screen is shown, **Then** the
   credit and part-payment options are not offered, so the refusal is not a surprise at the end.

---

### User Story 2 - Recovering an outstanding amount in instalments (Priority: P2)

A customer who owes money rarely clears it in one visit. They pay what they can, whenever they
can, and the shop's record of what is still owed must be right after every one of those
payments.

**Why this priority**: This is how the udhaar register is actually settled day to day, and it is
the customer-facing half of the same money as User Story 1. It is P2 rather than P1 only because
the shop is already able to do it — this story exists to state the rule and prove it holds.

**Independent Test**: Take a customer who owes Rs 3,000, receive three separate payments of
Rs 1,000, and confirm the balance reads 2,000 then 1,000 then 0, with all three payments visible
in their ledger.

**Acceptance Scenarios**:

1. **Given** a customer owes Rs 3,000, **When** Rs 1,000 is received, **Then** their outstanding
   balance becomes Rs 2,000 and the payment appears in their ledger with the date and who took it.
2. **Given** that same customer now owes Rs 2,000, **When** a further Rs 1,500 is received,
   **Then** their balance becomes Rs 500.
3. **Given** a customer owes Rs 500, **When** Rs 500 is received, **Then** their balance becomes
   zero and they no longer appear as money owed to the shop.
4. **Given** a customer owes Rs 500, **When** someone tries to record a payment of Rs 800,
   **Then** the shop is warned that this is more than the customer owes and must confirm before
   it is accepted.
5. **Given** a customer owes Rs 500, **When** someone tries to record a payment of zero or a
   negative amount, **Then** it is refused.
6. **Given** a customer has made several part payments, **When** their ledger is opened,
   **Then** every payment is listed separately with a running balance, and none have been merged
   or overwritten.

---

### User Story 3 - Carrying forward what a customer already owed (Priority: P2)

The shop has been keeping udhaar on paper for years. When a customer is entered into the system,
whatever they already owed from the paper register must be recorded against them, so the balance
the system shows is the real balance and not just what has been sold through the software.

**Why this priority**: Without it, every long-standing customer appears to owe nothing, the
receivables total understates what the shop is owed, and the owner cannot rely on the system for
the one number that matters most. It is P2 only because it is a one-time exercise per customer,
where User Story 1 is a risk on every sale.

**Independent Test**: Record a customer with Rs 12,000 already owed from the paper register, then
sell them Rs 3,000 on credit and receive Rs 5,000. Their balance must read Rs 10,000, and their
ledger must show the carried-forward amount as its first line.

**Acceptance Scenarios**:

1. **Given** a customer who owed Rs 12,000 on paper, **When** the owner records that as their
   carried-forward amount, **Then** their outstanding balance is Rs 12,000 and it appears as the
   opening line of their ledger, marked as brought forward rather than as a sale.
2. **Given** that customer, **When** the receivables report is opened, **Then** the Rs 12,000 is
   included in the total the shop is owed.
3. **Given** that customer with Rs 12,000 carried forward, **When** they are sold Rs 3,000 on
   credit, **Then** their balance is Rs 15,000.
4. **Given** that customer, **When** they pay Rs 5,000, **Then** their balance is Rs 10,000 and
   the payment sits after the carried-forward line in the ledger.
5. **Given** a salesman is signed in, **When** they try to record or change a carried-forward
   amount, **Then** they are refused — this is a statement about money owed, not a sale.
6. **Given** a customer whose carried-forward amount was entered wrongly, **When** the owner
   corrects it, **Then** the correction is recorded as its own visible entry with a reason, the
   original entry is not erased, and the balance reflects the corrected figure.
7. **Given** a customer with no history, **When** they are created without a carried-forward
   amount, **Then** their balance is zero and no opening line appears in their ledger.

---

### Edge Cases

- **A salesman starts a credit sale that the owner must finish.** The refusal happens before
  anything is written, so the cart is still on screen; the owner signs in and completes it. No
  half-finished sale is left behind and no stock is deducted in the meantime.
- **The owner's own account is used by the salesman.** Out of scope for this feature — it is an
  account-sharing problem, not a rules problem. The audit trail records who was signed in.
- **A sale is fully paid but the customer also owes money from before.** The sale is not a credit
  sale and the salesman may complete it. The customer's existing balance is untouched.
- **Rounding on a part payment.** A payment that leaves a balance of a fraction of a rupee must
  not leave a customer permanently owing a rounding artefact; balances are held to two decimals.
- **A carried-forward amount of zero or a negative number.** Zero means "nothing owed" and is the
  same as not recording one at all. A negative figure would mean the shop owes the customer, which
  is a different thing entirely and is refused.
- **A carried-forward amount recorded twice for the same customer.** The second recording is a
  correction, not a second debt, and must not silently double what the customer owes.
- **A customer created inline during a sale.** They start with no carried-forward amount; the
  owner records one afterwards if the paper register shows one.
- **Recovery when the customer owes nothing.** Accepting money from a customer with a zero
  balance is an overpayment and requires the same explicit confirmation as any other.

## Requirements *(mandatory)*

### Functional Requirements

#### Credit authority

- **FR-051**: The system MUST refuse to complete a sale that leaves any amount unpaid unless the
  person completing it is the shop owner (Admin).
- **FR-052**: The refusal in FR-051 MUST apply equally to a wholly unpaid sale and to a partly
  paid one — any outstanding amount is credit.
- **FR-053**: The system MUST allow a salesman (Staff) to complete a sale where the full amount
  is taken at the counter, by any payment method.
- **FR-054**: The system MUST continue to allow a salesman to record payments received against
  amounts already owed, because collecting a debt does not create one.
- **FR-055**: When a sale is refused under FR-051, the system MUST leave stock, customer balances
  and invoice records completely unchanged, and MUST explain that only the owner may approve
  udhaar.
- **FR-056**: The point-of-sale screen MUST NOT offer credit or part-payment options to a
  salesman, so the restriction is visible before the sale is attempted rather than only at the end.
- **FR-057**: The restriction MUST be enforced by the system itself and not only by hiding the
  option, so it holds however the request is made.

#### Recovering what is owed

- **FR-058**: The system MUST accept a payment of any amount greater than zero against a
  customer's outstanding balance, including an amount smaller than the balance.
- **FR-059**: The system MUST allow any number of separate payments against the same balance,
  each recorded as its own entry.
- **FR-060**: The system MUST reduce the customer's outstanding balance by the payment amount
  immediately, and the new balance MUST be visible to the person who took the payment.
- **FR-061**: The system MUST refuse a payment of zero or a negative amount.
- **FR-062**: The system MUST require explicit confirmation before accepting a payment larger
  than the amount the customer owes.
- **FR-063**: Each payment MUST record the amount, the date and time, how it was paid, and who
  received it.
- **FR-064**: The customer's ledger MUST show every payment separately, in order, with the
  balance after each one.

#### Amounts carried forward

- **FR-065**: The system MUST allow the owner to record, against a customer, an amount that
  customer already owed before the software was in use.
- **FR-066**: A carried-forward amount MUST be included in the customer's outstanding balance and
  in the total the shop is owed.
- **FR-067**: A carried-forward amount MUST appear in the customer's ledger as its own entry,
  distinguishable from a sale, a payment or a return, and MUST be the earliest entry shown.
- **FR-068**: Only the owner MUST be able to record or change a carried-forward amount.
- **FR-069**: The system MUST refuse a negative carried-forward amount.
- **FR-070**: Recording a carried-forward amount for a customer who already has one MUST be
  treated as a correction of that amount, never as an additional debt.
- **FR-071**: A correction under FR-070 MUST be recorded as its own visible entry showing the old
  and new figures and a reason, and MUST NOT erase or alter the original entry.
- **FR-072**: Recording or correcting a carried-forward amount MUST be written to the audit trail
  with who did it and when.
- **FR-073**: A customer created without a carried-forward amount MUST have a zero balance and no
  opening entry.

### Key Entities

- **Customer**: A person or shop that buys, possibly on credit. Carries a running outstanding
  balance and, optionally, an amount brought forward from the shop's paper records.
- **Ledger Entry**: One movement in a customer's account — a sale, a payment, a return, an
  adjustment, or now an amount brought forward. Append-only: entries explain how the balance
  reached its current figure and are never rewritten.
- **Customer Payment**: Money received against what a customer owes. Records the amount, method,
  time and the person who took it.
- **Invoice**: A sale. Records what was paid at the counter and what, if anything, was left
  outstanding — the amount that makes it a credit sale.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-015**: 100% of attempts by a salesman to complete a sale with any amount unpaid are
  refused, with no stock movement and no invoice created.
- **SC-016**: 100% of sales completed by a salesman have nothing outstanding.
- **SC-017**: A salesman can still complete a fully paid sale and record a customer payment, with
  no additional steps compared with before this change.
- **SC-018**: A customer owing any amount can settle it in any number of part payments, and the
  balance shown after each payment equals the previous balance less that payment, to the rupee.
- **SC-019**: The owner can record a customer's carried-forward amount and see it reflected in
  that customer's balance and in the shop's total receivables within one minute of entering it.
- **SC-020**: For every customer, the outstanding balance equals the amount brought forward, plus
  everything sold on credit, less everything paid and returned — verifiable by reading their
  ledger from top to bottom.
- **SC-021**: Every carried-forward amount and every correction to one can be traced to the person
  who entered it and the time they did so.

## Assumptions

- **A salesman meeting a credit customer is blocked, not queued.** The feature description says a
  salesman "must not be able to" make a credit sale, so the sale is refused outright with an
  explanation. No approval request, hold, or pending-sale queue is created — the owner completes
  the sale themselves. A request-and-approve workflow would be a larger feature and is not implied
  by the description.
- **Existing roles are reused.** The shop has exactly two roles, Admin (owner) and Staff
  (salesman); "the admin user" in the description means the existing Admin role, and no new
  permission level is introduced.
- **Part payment during recovery already works and is being confirmed, not built.** The shop can
  already receive a payment of any amount against a balance. User Story 2 states the rule
  explicitly and proves it holds, including across repeated payments; it is expected to add
  verification rather than new behaviour.
- **A carried-forward amount is money the customer owes the shop.** Credit balances in the
  customer's favour are out of scope, which is why a negative figure is refused.
- **Corrections are entries, not edits.** The ledger is a record of what happened, so fixing a
  mistyped carried-forward amount adds a visible correcting entry rather than changing history.
  This follows the existing treatment of the udhaar register.
- **A single carried-forward figure per customer.** The shop's paper register gives one number
  per customer, not an itemised history, so the system records one opening figure rather than
  reconstructing old invoices.
- **Amounts are in Pakistani rupees, held to two decimal places**, consistent with the rest of
  the system.
- **Wholesale and retail sales are treated identically** by the credit rule: what matters is
  whether money is outstanding, not how the sale was priced.
