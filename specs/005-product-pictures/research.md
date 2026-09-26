# Phase 0 Research: Product Pictures

## Thumbnail generation library

**Decision**: `SixLabors.ImageSharp` (pure-managed, no native deps — matters on IIS/Windows
deployment where installing native image libraries is extra friction).

**Rationale**: Already fits the stack (Infrastructure-layer-only dependency, resize-on-upload
is a one-shot `Image.Load` → `Mutate(x => x.Resize(...))` → `SaveAsJpeg`), and needs no OS
package. Runs once per upload, not per read, so its performance profile doesn't matter beyond
"fast enough for an admin uploading one photo."

**Licensing note**: ImageSharp's license (Six Labors Split License) is free for organisations
under $1M annual gross revenue; a single shop's internal tool clears this easily. Flagged in
case the business ever changes shape — not a blocker here.

**Alternatives considered**: `System.Drawing.Common` — already an indirect package reference
in this project (via a dependency chain, per publish output), but Microsoft has marked it
Windows-only and unsupported for cross-platform server workloads since .NET 6; using it
deliberately here would fight the framework's own guidance. `SkiaSharp` — heavier native
binary footprint for a single-operation need. ImageSharp wins on "smallest new surface area."

## Thumbnail storage: column vs. naming convention

**Decision**: Naming convention, no new column. The thumbnail is stored at the same relative
path as `image_path` with `_thumb` inserted before the extension
(`content/products/{guid}.jpg` → `content/products/{guid}_thumb.jpg`).

**Rationale**: `image_path` already fully determines the thumbnail's location; a second
column would only ever hold a derived value, which is duplicated state that could drift out
of sync (e.g. a data-fix script updates one column and forgets the other). `ImageStorageService`
already owns the naming scheme and is the only place that reads or writes it — one method
change, no migration, no drift possible by construction.

**Alternatives considered**: A `products.image_thumbnail_path` column — rejected as
redundant derived state for a value with no independent meaning.

## POS lookup contract change

**Decision**: `IProductQuery`/repository's existing search method (already used by
Products/Purchases per the shared search rules) is reused unchanged, called with a page size
of 8 instead of the POS's current page size of 1. No backend contract changes.

**Rationale**: The search engine, ranking and filters are explicitly shared across
Products/POS/Purchases (constitution: product search). Raising the requested page count is
the only backend-facing change; everything else is presentation.

The **frontend** contract does change: `PosScreen`'s `onFindProduct` prop currently resolves
to a single `Product | null` and the caller (`PosPage`) auto-adds it. This becomes a prop that
resolves to `Product[]` (0–8 items), and `PosScreen` renders cards instead of auto-adding.
Barcode lookup (`by-barcode/{barcode}`) is a separate, untouched code path per spec FR-013.

**Alternatives considered**: Keep `onFindProduct` singular and add a second
`onSearchProducts` prop for the multi-result path — rejected as unnecessary duplication;
barcode lookup already has its own separate call site in `PosPage`, so there is no case where
the same call needs both shapes.

## Backup scope for images

**Decision**: Extend the existing nightly backup job (`Backup:Directory`,
`BackupService`/equivalent) to archive `Storage:ProductImageRoot` alongside the database dump,
retained under the same `RetainDays` policy.

**Rationale**: FR-017 requires this explicitly; the two are already configured as sibling
settings (`Backup` and `Storage` sections), so extending the same job avoids a second
scheduler or configuration surface.

**Alternatives considered**: A separate image-backup job — rejected as needless operational
surface for a single shop's single server.

## Grid view state persistence

**Decision**: `localStorage`, one key, per browser — matches spec Assumption ("per-browser
preference, not synced across devices").

**Rationale**: Lowest-cost implementation for a preference with explicitly no cross-device
requirement; consistent with how other per-browser conveniences in this frontend are handled.

**Alternatives considered**: A per-user server-side preference — rejected as over-engineering
for a UI toggle the spec explicitly scoped to "per browser."
