# API Contracts: Product Pictures

All contracts below are additive or behavior-only changes to existing endpoints — no new
routes except the thumbnail read path.

## `POST /api/products/{id}/image` (existing endpoint — behavior extended)

Unchanged request shape (`multipart/form-data`, `file`), unchanged `AdminOnly` policy,
unchanged 4 MB request cap / 2 MB `ImageStorageService` cap, unchanged accepted content
types.

**New behavior**: on save, a thumbnail (~150×150) is generated from the uploaded image and
stored at the convention path (see `data-model.md`). On replacing an existing image, both the
old full image **and** old thumbnail are deleted (today only the full image is deleted).

Response body unchanged: `{ productId, imagePath }`. No `thumbnailPath` field is added to the
response — the frontend derives it from `imagePath` by the same convention the backend uses,
so the two can never disagree.

## `GET /api/products`, `GET /api/products/{id}`, `GET /api/products/by-barcode/{barcode}`

No contract change. `ImagePath` already exists on `ProductStaffDto` (and is inherited by
`ProductAdminDto`) and is already returned to both roles. The thumbnail is derived
client-side from this same field — no new field needed on any DTO.

## `GET /api/products?search=...&saleType=...&pageSize=8` (existing endpoint — new caller)

No contract change. POS starts requesting `pageSize=8` instead of `pageSize=1`; the endpoint
already supports arbitrary page sizes for the Products screen. Existing shared search rules
(word-splitting, brand/category/local filters) apply unchanged.

## Static file serving of images/thumbnails

No new controller route. Product images are already served as static files from
`Storage:ProductImageRoot` (mounted under `wwwroot`/static file middleware per existing
`Program.cs` setup). The thumbnail file sits in the same directory under its derived name and
is served by the same static file middleware with no additional wiring.

## Backup job (internal, not a public contract)

No API surface. The existing backup routine gains one additional source directory
(`Storage:ProductImageRoot`) to archive; `Admin → Backups` UI and its trigger endpoint are
unchanged in shape — only the resulting archive's contents grow.
