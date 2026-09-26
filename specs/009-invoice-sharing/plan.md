# Implementation Plan: Invoice and Receipt Sharing

**Branch**: `009-invoice-sharing` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-invoice-sharing/spec.md`

## Summary

Make the shop's already-built document capability reachable. PDF rendering, share-link minting,
the `wa.me` deep link and the public token endpoint all exist and are tested; `ShareButtons.tsx`
exists and is mounted on no screen. A customer therefore leaves the counter with nothing, and the
owner cannot reproduce a bill when one is disputed.

The work is mostly surfacing, with three genuine gaps:

1. **An Invoices screen.** A walk-in sale belongs to no customer and appears in no ledger, so
   without one, most counter sales become unreachable the moment the counter resets.
2. **Revocation endpoints.** `RevokeAsync` and `revoked_at_utc` exist, but nothing calls them —
   revocation is currently reachable only by editing the database.
3. **Binary response handling in the API client**, which forces JSON and unwraps an envelope a
   PDF does not have.

Because this creates the shop's only customer-facing surface, the containment requirements
(FR-113 to FR-119) are enforced by integration and architecture tests rather than by the UI.

## Technical Context

**Language/Version**: C# / .NET 8 (backend), TypeScript 5 / React 18 (frontend)

**Primary Dependencies**: ASP.NET Core 8, Dapper, QuestPDF (already in use for rendering),
FluentValidation, React Router, TanStack Query, axios

**Storage**: MySQL 8. **No schema change** — `document_tokens` (migration 0012) already carries
everything this feature needs.

**Testing**: xUnit + FluentAssertions (backend unit, integration, architecture); Vitest + React
Testing Library (frontend)

**Target Platform**: Windows server on the shop's premises; counter browser on desktop and tablet;
the customer's own phone browser for a shared link

**Project Type**: Web application — one backend project with internal layers, one React frontend

**Performance Goals**: A bill reaches the customer within 15 seconds of saving a sale (SC-001);
any invoice from the last 12 months reproducible in under 30 seconds (SC-002)

**Constraints**: The public endpoint stays rate-limited, access-logged, and resolves to exactly one
document. No second anonymous endpoint may appear — an architecture test fails the build if one
does. Cost, profit and payment proofs must be unreachable through any shared document.

**Scale/Scope**: One shop, two users, a few hundred invoices a month. Scope is three surfaces
(counter receipt, invoices list, customer ledger row), two new endpoints, and one new screen.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment | Verdict |
|---|---|---|
| **I. TDD (non-negotiable)** | Every item is testable before it exists: revocation idempotency, the identical-404 property, "no cost in a shared document", the disabled send button with no mobile number, blob download handling. Failing test precedes implementation in every case. | **PASS** |
| **II. Layered backend** | Revocation follows the existing path: controller → `IDocumentService` → `IDocumentTokenRepository`. No SQL enters a service; `LayeringTests` continues to enforce it. New authorization is declared on the endpoint (`AdminOnly`), never by hiding a button. | **PASS** |
| **III. Frontend logic is tested** | The stateful pieces — share actions, the disabled-send rule, the revoke list — get component tests. `ShareButtons` already has the branching logic and gains tests for the screens that mount it. | **PASS** |
| **IV. Transactional & server-authoritative money** | This feature performs **no money mutation**. Documents are read-only renderings of already-recorded facts. Revocation writes one timestamp on one token row — no balance, stock or invoice is touched. | **PASS (not applicable)** |
| **V. Complete vertical slices** | No migration is needed and that is a deliberate finding, not an omission (see data-model.md). Each story ships repository → service → controller with integration tests → screen with component tests. | **PASS** |
| **VI. Definition of done** | Both suites green, nothing previously green broken. The architecture test asserting a single anonymous endpoint must stay green — it is the gate that makes this feature acceptable at all. | **PASS** |
| **Cost confidentiality** | Strengthened, not weakened: FR-113/114 extend "cost never reaches Staff" to "cost never reaches a customer", asserted against the served bytes. | **PASS** |
| **Auditability** | Share creation records who and when; each open updates `last_accessed_at_utc`; revocation records when. | **PASS** |

**No violations. Complexity Tracking is therefore empty and has been removed.**

One point deserves stating plainly rather than being buried: the unauthenticated endpoint is **not
new**. It was justified in feature 001's Complexity Tracking and is reused unchanged. This feature
adds no anonymous surface — it makes the existing one reachable from inside the app.

## Project Structure

### Documentation (this feature)

```text
specs/009-invoice-sharing/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — decisions, with alternatives rejected
├── data-model.md        # Phase 1 — no schema change, and what may never cross the boundary
├── quickstart.md        # Phase 1 — how to prove it works
├── contracts/
│   └── documents.md     # Phase 1 — existing endpoints plus the two new ones
└── tasks.md             # Phase 2 — created by /speckit-tasks, NOT by this command
```

### Source code (repository root)

```text
backend/src/MoizPos/
├── Application/
│   ├── Abstractions/IDocumentAbstractions.cs   # + list/revoke on the token repository
│   └── Services/DocumentService.cs             # + ListShareLinksAsync, RevokeShareLinkAsync
├── Infrastructure/
│   └── Repositories/DocumentTokenRepository.cs # + the list query; RevokeAsync already exists
└── Api/
    └── Controllers/DocumentsController.cs      # + GET share-links, POST share-links/{id}/revoke

backend/tests/
├── MoizPos.IntegrationTests/Documents/         # revocation, idempotency, identical 404s,
│                                               #   and "a shared document reveals no cost"
└── MoizPos.ArchitectureTests/                  # unchanged — must stay green

frontend/src/
├── api/client.ts                               # binary responses bypass the JSON envelope
├── features/documents/
│   ├── ShareButtons.tsx                        # + Print; already written, mounted nowhere
│   ├── documentApi.ts                          # NEW — pdf fetch, share link, list, revoke
│   └── ShareLinksPanel.tsx                     # NEW — Admin: what is outstanding, revoke
├── features/invoices/                          # NEW — the screen that makes walk-ins reachable
│   ├── InvoicesPage.tsx
│   └── invoiceApi.ts
├── features/pos/PosScreen.tsx                  # mount sharing in the existing receipt block
├── features/customers/CustomerLedger.tsx       # mount sharing per ledger row
└── routes/AppRoutes.tsx                        # + /invoices

frontend/tests/features/
├── documents/                                  # share actions, no-mobile rule, revoke panel
└── invoices/                                   # the new screen
```

**Structure Decision**: The existing web-application layout is used unchanged — one backend
project with `Domain` / `Application` / `Infrastructure` / `Api` folders enforced by
`LayeringTests`, and a feature-foldered React frontend. The only new frontend feature folder is
`features/invoices/`; documents already has a home.

## Phase 0 — Research

Complete. See [research.md](./research.md). Decisions taken:

| # | Decision |
|---|---|
| R1 | Treat existing rendering, tokenisation and containment as fixed |
| R2 | Two surfaces: customer ledger rows **and** a new Invoices screen — walk-ins belong to no ledger |
| R3 | Print the same PDF the customer receives; no second rendering path |
| R4 | Fetch documents as blobs; the shared client's JSON envelope does not apply |
| R5 | Revocation needs a list endpoint too — you cannot revoke what you cannot see |
| R6 | Enforce "no cost, no proof" against the served bytes, not the UI |
| R7 | No usable mobile number → disabled with a reason, print and download unaffected |
| R8 | The counter receipt already outlives the cart; no change needed |

No `NEEDS CLARIFICATION` items remain.

## Phase 1 — Design & Contracts

Complete.

- [data-model.md](./data-model.md) — no schema change; `document_tokens` already holds everything.
  Documents the link lifecycle and, importantly, **what may never cross the boundary**.
- [contracts/documents.md](./contracts/documents.md) — four existing endpoints unchanged, two new
  Admin-only ones, plus the client-side rule that PDF responses bypass the envelope.
- [quickstart.md](./quickstart.md) — six scenarios including the probing and confidentiality
  checks.

### Post-design constitution re-check

Re-evaluated after the design above. **Still no violations.**

Two things the design made sharper rather than looser:

1. **Listing share links returns ids, never tokens.** Only the hash is stored, and re-exposing a
   live link through an authenticated listing would turn anyone reading the screen over the
   owner's shoulder into a link holder. The owner revokes by id.
2. **Revocation is idempotent and does not move the original timestamp.** A second click is not an
   error, and must not rewrite when the withdrawal actually happened — that timestamp is evidence.

## Implementation order

Each step is independently shippable and leaves the system working.

| # | Step | Delivers |
|---|---|---|
| 1 | Blob handling in the client + `documentApi` | Downloads work at all; prerequisite for everything |
| 2 | Mount sharing on the POS receipt | **US1** — the counter moment, the highest-value slice |
| 3 | Mount sharing on customer ledger rows | **US2/US3** for customers who have a record |
| 4 | Invoices screen + route | **US2** for walk-ins — the gap the ledger cannot close |
| 5 | List + revoke endpoints, and the Admin panel | **US4** |
| 6 | Confidentiality tests against the served bytes | FR-113/114 proven, not assumed |

Step 6 is listed last but its tests are written **first** within each step that could breach it —
TDD applies per step, not per plan.

## Risks

1. **This is the shop's only customer-facing surface.** Every containment requirement exists for
   that reason. Relaxing one changes the system's exposure, not just this feature's.
2. **A bill sent to a wrong number cannot be unsent**, only revoked. FR-117 bounds the window; it
   does not close the mistake.
3. **Link lifetime is configurable** (`ShareLinkExpiryDays`). The UI must show the returned
   `expiresAtUtc`, never a hardcoded "30 days", or the two diverge silently when the setting
   changes.
