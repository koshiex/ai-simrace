# SQLite migrations — how they run in this project

`SimCoach.Storage.Database.DatabaseMigrator` applies embedded SQL migrations. Non-obvious mechanics that
constrain how you write a migration file (`src/SimCoach.Storage/Database/Schema/NNN_name.sql`).

## Discovery + versioning rules

- Migration files are **embedded resources**, discovered by the marker `.Database.Schema.` in the resource
  name and a `.sql` suffix (`DatabaseMigrator.cs` `ResourceMarker`). A new `.sql` under `Database/Schema/`
  must be picked up as an `EmbeddedResource` by the csproj glob, or it is silently invisible.
- The version is the **leading digits** of the filename (`001_initial.sql` → 1). A file that doesn't start
  with a digit throws.
- Versions must be a **contiguous `1..N` run** — `AssertContiguous` fails fast on any gap, duplicate, or a set
  not starting at 1. So the next migration is always `max+1`; you cannot skip or reserve numbers.
- Applied when `version > PRAGMA user_version`; the migrator stamps `user_version = version` after each. A
  second run with no new files is a no-op (idempotent at the file level, NOT within a single script — write
  idempotent SQL yourself if a script may partially exist).
- `PRAGMA user_version` cannot be a bound parameter — the migrator interpolates the int directly (safe: it is
  parsed from a resource filename, never user input).

## Each migration runs inside ONE transaction — the FK-rebuild trap

`Migrate()` wraps every migration script in a single `connection.BeginTransaction()` (`DatabaseMigrator.cs:33`),
commits after stamping `user_version`. The migrator owns the transaction, so **migration `.sql` files must NOT
contain their own `BEGIN`/`COMMIT`.**

Consequence for any migration that must **rebuild a table** — SQLite cannot `ALTER` a `UNIQUE` constraint or
drop/re-key columns, so changing e.g. `UNIQUE(a,b,c)` → `UNIQUE(a,b,c,d)` forces the 12-step rebuild (create
new table, `INSERT INTO new SELECT ... FROM old`, drop old, rename new):

- **`PRAGMA foreign_keys = OFF` is a NO-OP mid-transaction** (SQLite only honours it outside an active
  transaction). The official FK-safe rebuild dance (`foreign_keys=OFF` … rebuild … `foreign_keys=ON`) is
  therefore **unavailable** inside the migrator.
- **`PRAGMA foreign_key_check` DOES work as a query mid-transaction.** Use it at the end of the migration to
  assert integrity, and fail the migration if it returns rows.
- A rebuild is only safe when nothing FK-references the table's PK and its outgoing FKs are recreated in the
  new table definition. Verify this before rebuilding. When you rebuild, explicitly `SELECT`-copy every
  existing column (preserve `id`, `pinned`, `created_at`, etc.) rather than relying on column order.

## Tests

`DatabaseMigratorTests` is the pattern for migration coverage: assert a fresh create reaches the target
`user_version`, and assert an upgrade from the previous version preserves existing rows with identity intact.
