# Feature Specification: Customer Sale-Type Filtering

**Feature Branch**: `004-customer-sale-type`

**Created**: 2026-09-18

**Status**: Draft

**Input**: User description: "move retail vs wholesale from reports and add these in customer module, main purpose is that, admin will easily find the customer sale type, add a dropdown with next of search bar, add 2 button one for retail and one for wholesale so admin will click on anyone and find exactly customer and easily manage the account"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find wholesale customers in one click (Priority: P1)

The owner is managing accounts and wants to see just his wholesale parties — the other shopkeepers
he sells to in bulk — without scrolling the whole customer list or working out who they are from
Reports. He opens Customers, clicks **Wholesale**, and the list narrows to those customers. He
clicks **Retail** to see counter customers instead, and **All** to go back.

**Why this priority**: This is the whole point of the request. Wholesale parties are the accounts
that carry the money — larger balances, regular credit — and the owner needs them in front of him
as a group before he can manage them.

**Independent Test**: With a mix of customers, click **Wholesale**: only wholesale customers are
listed. Click **Retail**: only retail customers. Click **All**: everyone returns.

**Acceptance Scenarios**:

1. **Given** the Customers screen, **When** the owner clicks **Wholesale**, **Then** only wholesale
   customers are listed and the Wholesale button is visibly the active choice.
2. **Given** Wholesale is selected, **When** the owner clicks **Retail**, **Then** only retail
   customers are listed instead — the two are alternatives, not additive.
3. **Given** either is selected, **When** the owner clicks **All**, **Then** every customer returns.
4. **Given** a sale type is selected, **When** the owner also types in the search box, **Then** only
   customers matching both the type and the search are listed.
5. **Given** a sale type is selected, **When** the owner also ticks the existing "only those who owe
   money" filter, **Then** all three conditions apply together.
6. **Given** a selection that matches nobody, **When** the list is shown, **Then** the screen says no
   customers match, rather than showing an empty list with no explanation.
7. **Given** the owner opens a customer from a filtered list, **When** he goes back, **Then** the
   filter is still applied so he can work through the accounts one by one.

---

### User Story 2 - See at a glance which kind of customer this is (Priority: P2)

Looking at the customer list or at one customer's account, the owner can see whether that customer
is a retail or wholesale party without opening their sales history.

**Why this priority**: The filter answers "show me the wholesale parties"; this answers "what is
this one?" while he is already looking at an account. Useful, but the list is usable without it.

**Independent Test**: Open Customers with no filter — each row shows its type. Open one customer —
the same type is shown on their account.

**Acceptance Scenarios**:

1. **Given** the customer list, **When** it is shown, **Then** each customer's sale type is visible
   on their row.
2. **Given** a customer's account is open, **When** it is shown, **Then** their sale type is visible
   there too.
3. **Given** a customer who has never been sold to, **When** they are listed, **Then** their type is
   shown honestly rather than guessed.

---

### User Story 3 - Keep the day's money view intact (Priority: P3)

At the end of the day the owner still needs to know how much money the shop took, split into retail
and wholesale, and to open either total to see the sales behind it.

**Why this priority**: This is existing behaviour the owner already asked for and uses. It is listed
here so that "moving retail vs wholesale into Customers" does not silently remove it — the two
answer different questions and both are needed. It is P3 because nothing new is built for it.

**Independent Test**: After this feature ships, the day's retail and wholesale totals are still
reachable, and clicking a total still lists that day's sales with customer and salesman.

**Acceptance Scenarios**:

1. **Given** the shop has sold today, **When** the owner looks for the day's takings, **Then** he can
   still see them split into retail and wholesale.
2. **Given** that split, **When** he opens either half, **Then** he still sees the individual sales
   with the invoice, time, customer and who sold it.

---

### Edge Cases

- **A customer with no sales at all.** Newly added, or only ever carried an opening balance. They
  must still be findable and manageable, and their type must not be invented.
- **A customer with both retail and wholesale sales.** Nothing in the system prevents this — sale
  type is recorded per invoice. The screen must not imply a customer is only ever one kind when
  their history says otherwise.
- **A walk-in sale with no customer record.** Not a customer, so it never appears in this list; it
  still appears in the day's takings.
- **A customer whose type changes.** A retail customer who starts buying in bulk must be able to
  become a wholesale party without losing their ledger, balance or history.
- **The filter and an emptied search box.** Clearing the search must leave the sale-type selection
  alone, and vice versa — they are separate controls.
- **A salesman using the Customers screen.** The salesman uses this screen to take payments. What
  he may see and do there must not change.

## Requirements *(mandatory)*

### Functional Requirements

#### Finding customers by sale type (US1)

- **FR-091**: The Customers screen MUST offer a sale-type selection beside the search box with three
  choices — All, Retail, Wholesale — where All is the state on arrival.
- **FR-092**: Choosing Retail or Wholesale MUST list only the customers of that type; the two MUST
  be alternatives, never both at once.
- **FR-093**: The current selection MUST be visibly distinguishable from the choices not selected.
- **FR-094**: The sale-type selection MUST combine with the search box and with the existing "owes
  money" filter, so a customer is listed only when every active condition holds.
- **FR-095**: When no customer matches the active conditions, the screen MUST say so explicitly.
- **FR-096**: Returning from a customer's account to the list MUST preserve the sale-type selection,
  the search text and the "owes money" filter.
- **FR-097**: Changing the sale-type selection MUST NOT clear the search box, and clearing the search
  box MUST NOT change the sale-type selection.

#### Seeing a customer's type (US2)

- **FR-098**: Each customer's sale type MUST be visible on their row in the customer list.
- **FR-099**: A customer's sale type MUST be visible on their account alongside their balance.
- **FR-100**: A customer who has never been sold to MUST have their type shown honestly rather than
  guessed from nothing.

#### What a customer's sale type means

- **FR-101**: A customer's sale type is
  [NEEDS CLARIFICATION: How is a customer's sale type decided? (a) the owner marks each customer as
  Retail or Wholesale when adding or editing them, defaulting to Retail — a standing label the owner
  controls; (b) it is worked out from that customer's actual sales — a customer with any wholesale
  sale counts as wholesale; (c) the owner marks it, and the screen also shows what their sales
  actually say so a mismatch is visible.]
- **FR-102**: A customer who has both retail and wholesale sales MUST be handled without
  misrepresenting them, in a way consistent with FR-101.
- **FR-103**: Changing a customer's sale type MUST NOT alter their balance, ledger, opening balance
  or any past invoice.

#### Who may use it

- **FR-104**: Filtering and seeing a customer's sale type MUST be available to whoever can already
  use the Customers screen, and MUST NOT change which customer details any role may see.
- **FR-105**: If a customer's sale type is something the owner sets, only the owner (Admin) MUST be
  able to set it.

#### The existing day-end report

- **FR-106**: The day's takings split into retail and wholesale, and the drill-down listing that
  day's sales, MUST remain available after this feature ships.
- **FR-107**: Where that day-end split lives is
  [NEEDS CLARIFICATION: The owner asked to "move retail vs wholesale from Reports". The Reports
  entry answers "how much money did the shop take today, split by type" and drills into the day's
  sales — a different question from "which customers are wholesale". Options: (a) keep it in Reports
  unchanged and only add the new customer filtering; (b) remove it from Reports, accepting that the
  day's takings can no longer be split by type; (c) keep the figures but move them onto the
  Dashboard, leaving Reports for the detailed lists.]

### Key Entities

- **Customer**: A person or shop that buys from the shop. Carries a name, contact details, an
  outstanding balance, an opening balance, and — depending on FR-101 — a sale type.
- **Invoice**: One sale. Already records whether it was Retail or Wholesale, and which customer it
  was for. This is where sale type exists today.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-028**: From the Customers screen, the owner can list only wholesale customers in **one
  click**, with no typing and without leaving the screen.
- **SC-029**: Switching between Retail, Wholesale and All updates the list in under one second with
  the shop's full customer list.
- **SC-030**: 100% of customers listed under Wholesale are wholesale parties, and none of the
  customers listed under Retail are.
- **SC-031**: A customer's sale type is readable from the list without opening the customer.
- **SC-032**: Every combination of sale type, search text and the "owes money" filter that matches
  nobody produces an explicit message — never an unexplained empty list.
- **SC-033**: Working through wholesale accounts one at a time — open a customer, take a payment, go
  back — keeps the filter in place, so the owner never re-applies it mid-task.
- **SC-034**: The day's takings can still be split into retail and wholesale after this feature
  ships, and either half still opens the sales behind it.

## Assumptions

- **This extends the existing Customers screen** rather than adding a new one. The screen already
  has a search box and an "owes money" filter; the sale-type control joins them.
- **"Dropdown plus two buttons" is read as one control with three states** — All, Retail, Wholesale.
  The owner described both a dropdown and two buttons; offering both for the same choice would let
  them disagree with each other. Two buttons plus an All escape covers everything he described, and
  the planning phase will confirm the exact shape.
- **Retail is the ordinary case.** Where a default is needed, a customer is Retail — counter trade is
  the shop's normal business, and wholesale parties are the smaller, deliberate group.
- **Sale type on an invoice is unchanged.** This feature does not alter how a sale is recorded, only
  how customers are found and described.
- **The salesman keeps the access he has today.** He uses the Customers screen to take payments;
  this feature adds a way to narrow the list, not a new permission boundary.
- **Filters are not remembered between visits to the screen**, only while moving between the list and
  a customer's account during one session (FR-096). A filter silently still applied tomorrow would
  hide customers from whoever opens the screen next.
