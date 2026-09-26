# API Contracts: Invoice and Receipt Sharing

**Feature**: 009-invoice-sharing · **Date**: 2026-09-23

Responses use the project's standard envelope (`{ success, data, error }`) except where the body
is a PDF, which is returned as raw bytes with no envelope — noted explicitly below because it is
the one place a client must not try to unwrap a response.

---

## Existing — unchanged

These are already built and tested. Listed so the plan is complete and so nobody "adds" them.

> **Corrected.** The two PDF routes were first written here under a `/documents/` prefix they have
> never had. The client was then built from this document rather than from the routes, so every
> Print and Download returned 404 while the whole test suite stayed green. The paths below are the
> ones the server actually serves; `ClientDocumentRouteTests` now asserts them by literal string.

### `GET /api/invoices/{id}/pdf`

Authenticated, any role. Returns `application/pdf` (raw bytes, no envelope).

### `GET /api/customer-payments/{id}/pdf`

Authenticated, any role. Returns `application/pdf` (raw bytes, no envelope).

### `POST /api/documents/share-link` — **changed by this feature**

Authenticated, any role (FR-110).

```jsonc
// request
{
  "documentType": "Invoice",
  "referenceId": 1234,
  "mobileNumber": "03001234567"   // NEW, optional — see below
}

// 201
{
  "success": true,
  "data": {
    "shareUrl": "https://shop.example/api/public/documents/<token>",
    "whatsAppUrl": "https://wa.me/923001234567?text=...",  // null when no usable number
    "smsUrl": "sms:+923001234567?body=...",                // NEW, null on the same condition
    "expiresAtUtc": "2026-10-23T12:00:00Z"
  }
}
```

**`mobileNumber` (new, optional)** is for a **walk-in** — a sale with no customer, which is most
counter sales. Without it those customers could never receive their own bill (research R7a).

- It is used **only when the document has no customer number on file**. A stored number is the
  shop's record of where a bill was sent, and a supplied one must never silently override it
  (FR-124). When a number is on file, this field is ignored.
- It is **not stored** — not against the sale, and not as a customer. A one-off buyer is not a
  customer relationship (FR-123).

**`smsUrl` (new)** carries the same link through the device's own SMS app. Both send routes are
deep links: nothing is dispatched by the server, so there is no messaging account and no
per-message cost (FR-125). Both are `null` under the same condition — no usable number — and the
client then disables sending with a reason rather than composing a message to nobody.

An SMS is 160 characters and a share link is long, so a send will likely split into two or three
messages. Accepted: SMS is the fallback for a customer without WhatsApp, not the default.

### `GET /api/public/documents/{token}`

**Anonymous.** The only unauthenticated data endpoint in the system. Rate-limited, access-logged.

- `200` — `application/pdf`, `Content-Disposition: inline` so it opens in a phone browser
- `404` — unknown, expired **and** revoked, all identical (FR-118)

---

## New — required by this feature

### `GET /api/documents/share-links`

Lists the share links issued for one document, so the owner can see what is outstanding before
deciding what to withdraw. Revocation without this is not an action the owner can take.

**Authorization**: Admin only (FR-111).

```
GET /api/documents/share-links?documentType=Invoice&referenceId=1234
```

```jsonc
// 200
{
  "success": true,
  "data": [
    {
      "id": 88,
      "createdAtUtc": "2026-09-23T10:15:00Z",
      "createdByUserName": "Shop Owner",
      "expiresAtUtc": "2026-10-23T10:15:00Z",
      "revokedAtUtc": null,
      "lastAccessedAtUtc": "2026-09-23T10:41:22Z",  // null if never opened
      "isUsable": true
    }
  ]
}
```

**The token itself is never returned.** Only its hash is stored, and re-exposing a live link
through an authenticated listing would turn a screen-reading bystander into a link holder. The
owner revokes by `id`, never by URL.

`lastAccessedAtUtc` is what answers "was this ever opened" (FR-119).

### `POST /api/documents/share-links/{id}/revoke`

Withdraws one link before it expires.

**Authorization**: Admin only (FR-111).

```jsonc
// 200
{ "success": true, "data": { "id": 88, "revokedAtUtc": "2026-09-23T11:02:00Z" } }
```

- Revoking an already-revoked link is **idempotent** — same 200, the original `revokedAtUtc`
  unchanged. A second click must not be an error, and must not move the timestamp.
- `404` for an id that does not exist.
- Revoking one link does not affect any other link for the same document (FR-120).

---

## Client-side contract notes

### PDF responses bypass the envelope

The shared client sets `Content-Type: application/json` and its response interceptor unwraps
`ApiResponse`. A PDF is raw bytes with no envelope, so document fetches **must** pass
`responseType: 'blob'` and skip unwrapping.

This is the mirror image of an existing documented trap: posting `FormData` required deleting the
JSON `Content-Type` header or multipart uploads lost their boundary and the server answered 415.
Binary *responses* need the same care in the other direction.

### Printing uses the same bytes

Print opens the fetched PDF, rather than rendering a print-styled HTML view. A second rendering
path is a second document, and the two drift — the failure being a printed bill that disagrees
with the sent one (research R3).

---

## Authorization summary

| Action | Staff | Admin | Anonymous |
|---|---|---|---|
| Download / print invoice PDF | yes | yes | no |
| Download / print payment receipt PDF | yes | yes | no |
| Create a share link, send on WhatsApp or SMS | yes | yes | no |
| Supply a number for a walk-in send | yes | yes | no |
| List share links for a document | no | yes | no |
| Revoke a share link | no | yes | no |
| Open a shared document by token | — | — | **yes**, token only |

Sharing is open to Staff because the salesman is who serves the customer, and a bill the customer
is entitled to is not privileged. Listing and revoking are the owner's, being containment over
something already released.
