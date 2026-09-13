# Deployment

For Moiz Mobile & Corporation, Danwran Lodhran. Written for whoever installs and looks after the
system — not necessarily the person who built it.

---

## What you need

| Thing | Version | Notes |
|---|---|---|
| MySQL | 8.0+ | With `mysql` and `mysqldump` available — see [Backups](#backups) |
| .NET runtime | ASP.NET Core 8 | The API. `dotnet --list-runtimes` to check |
| A machine that stays on | — | The shop's counter PC or a small VPS |

The frontend builds to static files and needs no runtime of its own.

---

## First install

### 1. Create the databases

Run [create-databases.sql](create-databases.sql) — in MySQL Workbench (File → Open SQL Script →
Execute All), or:

```bash
mysql -u root -p < docs/create-databases.sql
```

This creates `moizpos` (live) and `moizpos_test` (for the automated tests). Both are empty.

### 2. Create a database user for the application

**Do not run the shop's software as `root`.** Uncomment the block at the bottom of
`create-databases.sql`, put in a real password, and run it. That creates `moizpos_app`, which can
read and write these two schemas and nothing else — it cannot drop databases or manage users.

### 3. Configure secrets

Never in a committed file. On the server, use environment variables:

```bash
ConnectionStrings__Default="Server=localhost;Database=moizpos;Uid=moizpos_app;Pwd=<password>;"
Jwt__Key="<at least 32 random characters — generate, do not invent>"
```

In development, use user-secrets instead:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "..." --project backend/src/MoizPos.Api
```

If `Jwt:Key` is missing or too short the API refuses to start, rather than running insecurely.

### 4. Create the tables

```bash
dotnet run --project backend/src/MoizPos.Migrator
```

Safe to re-run — already-applied scripts are skipped. Run this after **every** update; it is how
schema changes reach the database.

### 5. Start it

```bash
dotnet run --project backend/src/MoizPos.Api
cd frontend && npm run build      # produces frontend/dist for your web server
```

On first run the API creates an administrator and writes a warning to the log:

```
admin / Admin@123
```

**Change that password before the shop uses the system.**

---

## Settings

All of these live in `appsettings.json` and can be overridden by environment variables using
`Section__Key` (double underscore).

| Setting | Default | What it does |
|---|---|---|
| `Shop:Name` / `Shop:Location` | Moiz Mobile & Corporation / Danwran Lodhran | Printed on every receipt |
| `Shop:ContactNumber` | *(empty)* | Printed under the shop name |
| `Backup:Directory` | `backups` | **Put this on a different drive** — see below |
| `Backup:RetainDays` | 30 | How long dumps are kept |
| `Backup:RunAtLocalHour` | 2 | Shop-local hour for the daily backup |
| `Backup:ToolsDirectory` | *(empty)* | Where `mysqldump` lives, if not on `PATH` |
| `Documents:PublicBaseUrl` | *(empty)* | Base URL customers can reach — required for WhatsApp receipts |
| `Documents:ShareLinkExpiryDays` | 30 | How long a receipt link works |
| `Storage:ProductImageRoot` | `content/products` | Product photos — back this up too |
| `Cors:AllowedOrigins` | *(empty)* | Where the counter app is served from |

---

## Backups

### Make sure `mysqldump` can be found

The backup uses MySQL's own `mysqldump`. If it is not on the system `PATH`, point at it:

```
Backup__ToolsDirectory=C:\Program Files\MySQL\MySQL Server 8.0\bin
```

Without this, backups fail with a clear message — but they still fail, so check it on day one.

### Put the backups somewhere else

`Backup:Directory` should be on **a different physical drive** from the database, or a synced
folder. A backup on the same disk protects you from a mistake but not from the disk dying, which
is the failure that actually loses a shop its books.

### Understand what a backup is worth

The daily backup is a **logical dump taken once a day**. If the machine is lost, you lose
everything sold since that dump — up to 24 hours of sales, customer payments and stock movements.

That is a deliberate trade-off, not an oversight: it keeps the system to one moving part a
shopkeeper can maintain. If 24 hours is too much to lose, the upgrade is MySQL binary logging
for point-in-time recovery, which needs someone comfortable administering MySQL.

### Test a restore before you need one

An untested backup is a guess. Once a quarter:

1. Create a scratch database
2. Restore the most recent dump into it
3. Confirm the row counts look right

A restore **replaces every record** in the target database. The UI makes you type the file name
back before it will proceed.

---

## Updating

```bash
git pull
cd backend  && dotnet test          # both suites must be green before deploying
cd frontend && npm run test && npm run build
dotnet run --project backend/src/MoizPos.Migrator
# restart the API
```

Take a backup first. Migrations are forward-only — there is no automatic rollback.

---

## Security checklist

- [ ] `root` has a password
- [ ] The application connects as `moizpos_app`, not `root`
- [ ] `Jwt:Key` is at least 32 random characters and not shared with anything else
- [ ] The seeded `admin` password has been changed
- [ ] The backup directory is on a different drive
- [ ] MySQL is not reachable from the internet
- [ ] If `Documents:PublicBaseUrl` is public, it is HTTPS

That last one matters: receipt links are the one part of this system a stranger can reach. Each
is a 256-bit token that expires in 30 days and shows exactly one receipt, but they should still
travel over HTTPS.

---

## When something goes wrong

| Symptom | Likely cause |
|---|---|
| API will not start, complains about `Jwt:Key` | Secret missing or under 32 characters |
| API will not start, complains about `ConnectionStrings:Default` | Connection string not set for this environment |
| Backups never appear | `mysqldump` not found — set `Backup:ToolsDirectory` |
| "Send on WhatsApp" is greyed out | The customer has no mobile number, or it is not a valid Pakistani number |
| Receipt link says "not found" | Expired (30 days), revoked, or mistyped |
| Counter shows "Could not reach the server" | API stopped, or `VITE_API_BASE_URL` points at the wrong address |

Logs are in `logs/moizpos-*.log`, one file per day, kept 30 days. Every error carries a
`traceId` — quote it when reporting a problem, and the log line will have the detail.
