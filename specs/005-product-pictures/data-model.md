# Phase 1 Data Model: Product Pictures

No new tables and no new columns. `products.image_path` (added in migration `0004`) already
holds everything needed.

## Product (existing entity, no schema change)

| Field | Type | Notes |
|---|---|---|
| `image_path` | `VARCHAR`, nullable | Existing. Relative path to the full-size image, e.g. `content/products/{guid}.jpg`. `NULL` = no picture, renders as placeholder (FR-001). |

## Derived value: thumbnail path

Not stored. Computed from `image_path` by convention wherever a thumbnail is needed:

```
thumbnail_path = insert "_thumb" before the file extension of image_path
  content/products/3fa1...c2.jpg  →  content/products/3fa1...c2_thumb.jpg
```

`NULL` `image_path` ⇒ no thumbnail ⇒ placeholder, same as the full image (FR-002, FR-015).

## State transitions

| Transition | Effect |
|---|---|
| Create product, no image | `image_path = NULL`. No files written. (FR-001) |
| Create/update product, image uploaded | New full image + thumbnail written under generated GUID names; `image_path` set. (FR-002) |
| Re-upload image on existing product | New full image + thumbnail written; `image_path` updated; **previous** full image + thumbnail files deleted. (FR-003) |
| Product retired (existing feature, unchanged) | Image files are left in place — a retired product's history (including its picture, if the detail view is ever reached via an old invoice) is preserved, consistent with the existing "never hard-delete" rule for products. |

## Validation rules (unchanged, already enforced by `ImageStorageService`)

- Content type must be `image/jpeg`, `image/png`, or `image/webp`.
- Size must not exceed 2 MB (`Storage:MaxImageBytes`).
- Stored filename is always server-generated (GUID), never the client's filename — prevents
  path traversal.

## Backup scope addition

No entity change — an operational addition: `Storage:ProductImageRoot` directory is included
in the nightly backup archive alongside the database dump (FR-017). No new configuration key;
reuses `Backup:Directory` / `Backup:RetainDays`.
