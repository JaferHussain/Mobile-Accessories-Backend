# API Conventions

**Feature**: `001-pos-inventory-ledger` · Applies to every endpoint in [openapi.yaml](./openapi.yaml).

## Base

- Base path `/api`. JSON only (`application/json`), UTF-8.
- All timestamps in payloads are **ISO-8601 UTC** with `Z` (e.g. `2026-09-09T13:45:00Z`).
  Reporting period boundaries are resolved server-side against `Asia/Karachi` (UTC+05:00).
- All money is a JSON **number with 2 decimal places**, serialized from `decimal`. Clients must
  not perform money arithmetic for anything they send back — the server recomputes totals from
  line items and ignores client-supplied totals (FR-013).

## Response envelope

Every response, success or failure, uses one shape.

```jsonc
// Success
{ "success": true, "data": { /* payload */ }, "error": null }

// Failure
{
  "success": false,
  "data": null,
  "error": {
    "code": "INSUFFICIENT_STOCK",
    "message": "Not enough stock for 'Type-C Braided 2m'. Available: 3, requested: 5.",
    "details": [ { "field": "items[1].quantity", "message": "Exceeds available stock (3)." } ],
    "traceId": "0HN7GK2K9J1P4:00000003"
  }
}
```

`details` is present only for validation failures. `traceId` correlates to the server log.

## Error codes

| HTTP | `code` | Meaning |
|---|---|---|
| 400 | `VALIDATION_FAILED` | FluentValidation rejected the request; see `details` |
| 400 | `INSUFFICIENT_STOCK` | A sale or purchase return would drive stock negative |
| 400 | `DISCOUNT_EXCEEDS_TOTAL` | Line or order discount larger than the amount it applies to |
| 400 | `RETURN_EXCEEDS_ORIGINAL` | More units returned than sold or purchased |
| 400 | `CUSTOMER_REQUIRED` | Unpaid balance on a sale with no customer attached |
| 400 | `OVERPAYMENT_NOT_CONFIRMED` | Payment exceeds the balance without `confirmOverpayment: true` |
| 401 | `UNAUTHENTICATED` | Missing, malformed or expired access token |
| 403 | `FORBIDDEN` | Authenticated but the role may not access this resource |
| 404 | `NOT_FOUND` | Resource does not exist, is inactive, or the token is invalid/expired |
| 409 | `CONCURRENCY_CONFLICT` | Row lock timeout; the caller may retry |
| 422 | `BUSINESS_RULE_VIOLATION` | Domain invariant breached, message explains |
| 500 | `INTERNAL_ERROR` | Unexpected; message is generic, `traceId` identifies the log entry |

## Pagination

List endpoints accept `?page=1&pageSize=25&sort=name&direction=asc` (`pageSize` max 100,
default 25) and return:

```jsonc
{
  "success": true,
  "data": {
    "items": [ /* ... */ ],
    "page": 1, "pageSize": 25, "totalItems": 137, "totalPages": 6
  },
  "error": null
}
```

## Authentication

- `Authorization: Bearer <access token>`; access tokens live 60 minutes.
- `POST /api/auth/refresh` exchanges a refresh token for a new pair and rotates the refresh token.
- **One endpoint is deliberately unauthenticated**: `GET /api/public/documents/{token}`. See the
  justified exception in [plan.md](../plan.md) and R2 in [research.md](../research.md).

## Role-based access

Two roles: `Admin` and `Staff`. Enforcement is server-side on every route (FR-040), never by
hiding UI.

**Staff may never receive cost or profit data.** This is enforced by serving different response
types, not by nulling fields:

| Endpoint group | Staff | Admin |
|---|---|---|
| `/api/products` (read) | `ProductStaffDto` — no `costPrice` | `ProductAdminDto` — includes `costPrice` |
| `/api/products` (write) | ❌ 403 | ✅ |
| `/api/invoices` (create, read own shop sales) | ✅ without cost/profit fields | ✅ full |
| `/api/customers`, `/api/customers/{id}/ledger` | ✅ | ✅ |
| `/api/customers/{id}/payments` (receive) | ✅ | ✅ |
| `/api/suppliers`, `/api/purchases`, `/api/purchase-returns` | ❌ 403 | ✅ |
| `/api/expenses` | ❌ 403 | ✅ |
| `/api/reports/*`, `/api/dashboard` | ❌ 403 | ✅ |
| `/api/admin/*` (backup, restore, users, audit) | ❌ 403 | ✅ |

`ProductStaffDto` and `InvoiceStaffDto` have **no cost or profit property at all** — the field is
absent from the type, so it cannot leak through a forgotten conditional. An architecture test
asserts this (R10).

## Idempotency

`POST /api/invoices` accepts an optional `Idempotency-Key` header. Replaying a key within 24
hours returns the original invoice rather than creating a duplicate — protection against a
double-tap or a retried request at a busy counter.

## Concurrency

Sale, purchase and return endpoints lock the affected product rows for the duration of their
transaction (R4). If a lock cannot be acquired within the timeout the call returns 409
`CONCURRENCY_CONFLICT` and is safe to retry — nothing was written.
