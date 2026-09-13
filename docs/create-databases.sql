-- =====================================================================
--  Moiz Mobile POS — one-time database bootstrap
-- =====================================================================
--  Run this ONCE on the machine that hosts MySQL.
--
--  In MySQL Workbench:
--    1. Open your local connection (localhost:3306)
--    2. File > Open SQL Script...  and choose this file
--    3. Click the lightning bolt (Execute All, Ctrl+Shift+Enter)
--    4. Right-click SCHEMAS in the left panel > Refresh All
--
--  This creates EMPTY schemas only. Tables are created by the migration
--  tool, never by hand:
--      dotnet run --project backend/src/MoizPos.Migrator
--
--  Safe to re-run: CREATE DATABASE IF NOT EXISTS touches nothing that
--  already exists, and no data is dropped anywhere in this script.
-- =====================================================================

-- The live shop database.
CREATE DATABASE IF NOT EXISTS moizpos
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

-- The test database. Integration tests create and DROP tables in here
-- freely, so it must never point at real shop data.
CREATE DATABASE IF NOT EXISTS moizpos_test
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

-- ---------------------------------------------------------------------
--  Verify
-- ---------------------------------------------------------------------
SELECT
    SCHEMA_NAME                 AS `database`,
    DEFAULT_CHARACTER_SET_NAME  AS `charset`,
    DEFAULT_COLLATION_NAME      AS `collation`
FROM information_schema.SCHEMATA
WHERE SCHEMA_NAME IN ('moizpos', 'moizpos_test');

-- Expect two rows, both utf8mb4 / utf8mb4_0900_ai_ci.


-- =====================================================================
--  OPTIONAL — dedicated application user (recommended)
-- =====================================================================
--  The app should not connect as root. Uncomment the block below, put a
--  real password in place of the placeholder, run it, and then use that
--  user in the connection string instead of root.
--
--  This user can read and write shop data but cannot create or drop
--  databases, manage other users, or reach any other schema.
-- ---------------------------------------------------------------------

-- CREATE USER IF NOT EXISTS 'moizpos_app'@'localhost'
--   IDENTIFIED BY 'REPLACE-WITH-A-REAL-PASSWORD';
--
-- GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES
--   ON moizpos.*      TO 'moizpos_app'@'localhost';
--
-- GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, DROP, REFERENCES
--   ON moizpos_test.* TO 'moizpos_app'@'localhost';
--
-- FLUSH PRIVILEGES;
--
-- -- Confirm what was granted:
-- SHOW GRANTS FOR 'moizpos_app'@'localhost';

-- Note: CREATE/ALTER/DROP are granted because the migration tool creates
-- tables, and the integration tests drop their own. They are scoped to
-- these two schemas only.
