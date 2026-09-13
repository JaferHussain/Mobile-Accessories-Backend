# Phase 0 Research: Credit Controls & Customer Opening Balances

**Feature**: 002-credit-controls-opening-balances
**Date**: 2026-09-11

The three requirements differ sharply in how much is actually new. That is the first thing worth
establishing, because it decides where the effort goes.

| Requirement | Current state | Work needed |
|---|---|---|
| Admin-only credit sales (US1) | Any signed-in user can complete a sale with an amount outstanding | New enforcement, new tests |
| Partial payment on recovery (US2) | Already works: any amount > 0, repeatable, overpayment gated | Verification only |
| Customer opening balances (US3) | No concept exists | New column, new ledger entry type, new endpoint, new screen |

---

## R1. Where the Admin-only credit rule is enforced

**Decision**: In `InvoiceService.CreateAsync`, immediately after the server recomputes totals and
before anything is written — not as an `[Authorize]` policy on the controller.

**Rationale**: Whether a sale is a credit sale is not a property of the request; it is a property
of the *computed result*. `AmountRemaining` is only known after the server has re-priced every
line from the locked product rows and applied discounts. Constitution Principle IV requires
exactly that recomputation and forbids trusting client totals, so a client-declared "this is a
cash sale" cannot be the basis of the decision. A controller-level policy would have to read the
client's own `amountPaid`, which is the number an attacker controls.

Placing it after `InvoiceCalculator.Calculate` and before `InsertInvoiceAsync` also satisfies
FR-055 for free: the whole method runs inside one unit of work, so a refusal at that point leaves
no invoice, no stock movement and no balance change.

**The role must therefore reach the service.** `CreateAsync` currently takes `long userId`. It
gains the caller's role alongside it rather than reading any ambient context, keeping the service
testable without an HTTP principal — which is how every other service in this codebase is built.

**Alternatives considered**:

- *`[Authorize(Policy = AdminOnly)]` on the invoices endpoint.* Rejected outright: it would block
  the salesman from making ordinary fully-paid sales, which is the bulk of the shop's trade.
- *A separate `/api/invoices/credit` endpoint, Admin-only.* Rejected. It would let a Staff caller
  post a part-paid sale to the ordinary endpoint and have it succeed, because the ordinary
  endpoint would still accept whatever `amountPaid` arrived. The rule has to be about the money,
  not about which URL was used.
- *Validating in FluentValidation.* Rejected. The validator sees the request, not the recomputed
  total, so a client understating the price would slip through.

## R2. What counts as "unpaid"

**Decision**: `AmountRemaining > 0` after server-side recomputation, regardless of
`PaymentMethod`.

**Rationale**: FR-052 makes a part-paid sale credit just as much as a wholly unpaid one, so a
single numeric test covers both and there is no list of payment methods to keep in step. The
existing `PaymentMethod.Credit` and `PaymentMethod.Partial` values become descriptive labels
rather than the thing being policed — which matters, because a sale marked `Cash` with
`amountPaid` below the total is still money that left the shop.

Rounding: `AmountRemaining` is already `decimal` rounded to two places by `InvoiceCalculator`, so
`> 0` is exact and a half-paisa artefact cannot make a fully-paid sale look like credit.

## R3. Telling the salesman before they start (FR-056)

**Decision**: The POS hides the `Credit` and `Partial` payment options and locks the amount-paid
field to the full total when the signed-in user is not an Admin. The server rule stands
regardless (FR-057).

**Rationale**: A refusal only at the moment of saving wastes a scanned cart and looks like a bug
to the salesman. Hiding the options is a usability measure, and the codebase already treats
UI-level role checks that way — `AppShell` hides admin links while the server independently
refuses Staff on those endpoints, and an architecture test enforces the server half.

## R4. Storing a customer's carried-forward amount

**Decision**: A nullable `opening_balance DECIMAL(12,2)` column on `customers`, **plus** a ledger
entry of a new type `OpeningBalance`. The customer's `outstanding_balance` includes it.

**Rationale**: The two serve different questions. The column answers "what was carried forward
for this customer" directly, which FR-070 needs in order to treat a second recording as a
correction rather than a second debt — without it, distinguishing "correcting the opening figure"
from "adding another old debt" would mean scanning the ledger. The ledger entry answers "why is
the balance what it is", which FR-067 requires and which the existing ledger screen renders with
no special-casing.

`entry_type` is a MySQL `ENUM` in migration 0009, so adding a value requires an explicit
`ALTER TABLE ... MODIFY COLUMN`. This is a migration, not a code change, and must go through DbUp
as a new numbered script per the constitution's schema rule.

**Alternatives considered**:

- *Ledger entry only, no column.* Rejected for the correction problem above.
- *Column only, no ledger entry.* Rejected: the customer's balance would then contain money with
  nothing in the ledger explaining it, and the ledger's own invariant — that reading it top to
  bottom reproduces the balance (SC-020) — would break.
- *A synthetic opening invoice.* Rejected. It would appear in sales reports and profit figures as
  a sale that never happened, corrupting exactly the numbers feature 001 was careful about.

## R5. Where the opening entry sits in the ledger

**Decision**: The opening entry is written when the opening balance is recorded, and carries the
timestamp of that moment. It is the earliest entry for any customer whose opening balance is
recorded before their first transaction — which is the flow FR-067 describes and the one the shop
will actually use, since a customer is entered from the paper register with what they already
owe. For a customer who already has entries, the opening entry is appended in true chronological
order rather than back-dated.

**Rationale**: `ledger_entries.balance_after` is persisted, deliberately, so the ledger screen is
one indexed read rather than a replay (migration 0009's own comment). Inserting an entry *before*
existing entries would invalidate every `balance_after` after it, requiring a rewrite of rows the
system treats as an append-only record of what happened. Rewriting history to make a display
order nicer is a worse trade than an entry that is honest about when it was recorded.

This is a conscious, narrow deviation from a literal reading of FR-067, and it is recorded in the
Complexity Tracking table of `plan.md`. In the intended flow there is no deviation at all.

## R6. Correcting a carried-forward amount (FR-070, FR-071)

**Decision**: Re-recording sets `opening_balance` to the new figure, adjusts
`outstanding_balance` by the **difference**, and appends an `Adjustment` ledger entry carrying
the old and new figures and a reason. The original `OpeningBalance` entry is untouched.

**Rationale**: Adjusting by the difference is what makes FR-070 hold — correcting 12,000 to
10,000 must reduce the balance by 2,000, never add 10,000. Recording the correction as an
`Adjustment` rather than mutating the original entry keeps the ledger append-only, which is how
returns and every other correction already behave in this system.

`ledger_entries` has no `note` column today, so one is added in the same migration to carry the
reason. FR-071 requires the reason to be visible, and putting it in the audit trail alone would
hide it from the person reading the customer's ledger.

**A reason is mandatory on a correction** and optional on the first recording: the first is a
statement of fact from the paper register, the second is someone changing a figure about money
and should have to say why.

## R7. Recomputing the balance safely

**Decision**: Recording or correcting an opening balance runs in one unit of work that locks the
customer row with `SELECT ... FOR UPDATE` before reading `outstanding_balance`, exactly as
`CustomerLedgerService.ReceivePaymentAsync` already does.

**Rationale**: Constitution Principle IV. Without the lock, a payment received at the counter at
the same moment the owner is fixing an opening figure would produce a lost update — the two
would read the same starting balance and one would overwrite the other's result.

## R8. Verifying partial payment on recovery (US2)

**Decision**: No production code changes. Add integration tests that prove the rules already
hold: repeated part payments, the running balance after each, refusal of zero and negative
amounts, and the overpayment confirmation gate.

**Rationale**: `CustomerLedgerService.ReceivePaymentAsync` already accepts any amount above zero
and gates overpayment behind `ConfirmOverpayment`; `ReceiveCustomerPaymentValidator` already
enforces `GreaterThan(0)`. Building something new here would duplicate working, tested behaviour.
The gap is that no test currently proves the *sequence* — three payments in a row leaving the
right balance each time — which is precisely what the shop does and what SC-018 measures.

**One real gap found**: FR-054 requires a salesman to be able to record recovery payments. That
works today, and the Admin-only credit rule must not accidentally break it — the rule is scoped
to invoice creation, and a test asserts a Staff user can still receive a payment.

## R9. Frontend approach for opening balances

**Decision**: A field on the customer create/edit form for the first recording, and a separate
"Correct opening balance" action requiring a reason. Both Admin-only.

**Rationale**: Separating them matches the difference in R6: entering what the paper register
says is part of setting the customer up, while changing that figure later is a deliberate
correction and should not be a quiet edit in a form the owner opened for another reason.

## Open questions

None. All decisions above are resolvable from the existing codebase and the specification's
stated assumptions.
