-- NFR-04 RESTORE-DRILL VALIDATION (read-only — modifies nothing).
--
-- Run this against a database that has just been restored from a backup / pg_dump, either:
--   * a local scratch database populated by scripts/../docs/runbooks/restore-drill.md, or
--   * a throwaway Supabase branch/project during the 6-monthly production restore exercise.
--
-- Purpose: prove the restored database is structurally sound, tenant-isolated, and that
-- deletion obligations can be reapplied. Record each section's result in the drill results
-- template in docs/runbooks/restore-drill.md.
--
-- Every query prints a PASS/FAIL (or a value to eyeball). Nothing here writes data.
-- Adjust the tenant table list in section 3 if module schemas have been added since this was written.

\echo '================ NFR-04 RESTORE-DRILL VALIDATION ================'

-- ---------------------------------------------------------------------------
-- 1. Schema-per-module present
-- ---------------------------------------------------------------------------
\echo '--- 1. Module schemas present ---'
WITH expected(schema_name) AS (
    VALUES ('companies'),('employees'),('recruitment'),('tasks'),('support'),
           ('offboarding'),('sickness'),('documents'),('identity'),('audit')
)
SELECT e.schema_name,
       CASE WHEN n.nspname IS NULL THEN 'FAIL - missing' ELSE 'PASS' END AS status
FROM expected e
LEFT JOIN pg_namespace n ON n.nspname = e.schema_name
ORDER BY e.schema_name;

-- ---------------------------------------------------------------------------
-- 2. EF migration history restored for every module schema
-- ---------------------------------------------------------------------------
\echo '--- 2. Migration history per schema (compare last id to the deployed release) ---'
DO $$
DECLARE
    r record;
    cnt bigint;
    last_id text;
BEGIN
    FOR r IN
        SELECT table_schema
        FROM information_schema.tables
        WHERE table_name = '__ef_migrations_history'
        ORDER BY table_schema
    LOOP
        EXECUTE format('SELECT count(*), max(migration_id) FROM %I.__ef_migrations_history', r.table_schema)
            INTO cnt, last_id;
        RAISE NOTICE '  schema % : % migrations applied, latest = %', r.table_schema, cnt, last_id;
        IF cnt = 0 THEN
            RAISE WARNING '  FAIL - schema % has an empty migration history', r.table_schema;
        END IF;
    END LOOP;
END $$;

-- ---------------------------------------------------------------------------
-- 3. Row counts sane (no unexpectedly empty core tables)
-- ---------------------------------------------------------------------------
\echo '--- 3. Core table row counts (eyeball against expected production volumes) ---'
DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
        'companies.companies',
        'companies.customer_subscriptions',
        'employees.employees',
        'identity.users'
    ];
    cnt bigint;
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        BEGIN
            EXECUTE format('SELECT count(*) FROM %s', tbl) INTO cnt;
            RAISE NOTICE '  % : % rows', tbl, cnt;
            IF cnt = 0 THEN
                RAISE WARNING '  CHECK - % is empty (expected non-zero in production)', tbl;
            END IF;
        EXCEPTION WHEN undefined_table THEN
            RAISE WARNING '  SKIP - table % not found (name drift?)', tbl;
        END;
    END LOOP;
END $$;

-- ---------------------------------------------------------------------------
-- 4. Tenant isolation - every tenant-owned table has a non-null company_id
-- ---------------------------------------------------------------------------
\echo '--- 4. Tenant isolation: company_id NOT NULL on every tenant table ---'
DO $$
DECLARE
    r record;
    bad bigint;
    total_bad bigint := 0;
BEGIN
    FOR r IN
        SELECT c.table_schema, c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE c.column_name = 'company_id'
          AND t.table_type = 'BASE TABLE'
          AND c.table_schema NOT IN ('pg_catalog','information_schema')
    LOOP
        EXECUTE format('SELECT count(*) FROM %I.%I WHERE company_id IS NULL', r.table_schema, r.table_name)
            INTO bad;
        IF bad > 0 THEN
            total_bad := total_bad + bad;
            RAISE WARNING '  FAIL - %.% has % rows with NULL company_id', r.table_schema, r.table_name, bad;
        END IF;
    END LOOP;
    IF total_bad = 0 THEN
        RAISE NOTICE '  PASS - no NULL company_id on any tenant table';
    END IF;
END $$;

-- ---------------------------------------------------------------------------
-- 5. Tenant isolation - no obvious cross-tenant foreign references
--    (employees whose company_id does not exist in companies.companies)
-- ---------------------------------------------------------------------------
\echo '--- 5. Cross-tenant orphans: rows referencing a non-existent company ---'
DO $$
DECLARE
    r record;
    orphans bigint;
    total bigint := 0;
BEGIN
    FOR r IN
        SELECT c.table_schema, c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON t.table_schema = c.table_schema AND t.table_name = c.table_name
        WHERE c.column_name = 'company_id'
          AND t.table_type = 'BASE TABLE'
          AND c.table_schema NOT IN ('pg_catalog','information_schema')
    LOOP
        EXECUTE format(
            'SELECT count(*) FROM %I.%I x WHERE x.company_id IS NOT NULL
             AND NOT EXISTS (SELECT 1 FROM companies.companies co WHERE co.id = x.company_id)',
            r.table_schema, r.table_name) INTO orphans;
        IF orphans > 0 THEN
            total := total + orphans;
            RAISE WARNING '  FAIL - %.% has % rows pointing at a missing company', r.table_schema, r.table_name, orphans;
        END IF;
    END LOOP;
    IF total = 0 THEN
        RAISE NOTICE '  PASS - no rows reference a missing company';
    END IF;
END $$;

-- ---------------------------------------------------------------------------
-- 6. DB <-> file reference integrity (run after Storage restore)
--    Employee/candidate document rows should reference a storage key.
--    This lists rows with a NULL/empty storage key - potential restore damage.
--    The "file exists" half must be checked by the operator against the bucket
--    listing (see restore-drill.md); SQL cannot see Storage.
-- ---------------------------------------------------------------------------
\echo '--- 6. Document rows missing a storage key ---'
DO $$
DECLARE
    r record;
    bad bigint;
BEGIN
    FOR r IN
        SELECT table_schema, table_name, column_name
        FROM information_schema.columns
        WHERE column_name ~* '(storage_key|storage_path|object_key|file_path|blob_path)'
          AND table_schema NOT IN ('pg_catalog','information_schema')
    LOOP
        EXECUTE format('SELECT count(*) FROM %I.%I WHERE %I IS NULL OR btrim(%I::text) = %L',
                       r.table_schema, r.table_name, r.column_name, r.column_name, '')
            INTO bad;
        RAISE NOTICE '  %.%(%) : % rows with empty key', r.table_schema, r.table_name, r.column_name, bad;
        IF bad > 0 THEN
            RAISE WARNING '  CHECK - %.% has document rows with no storage key', r.table_schema, r.table_name;
        END IF;
    END LOOP;
END $$;

-- ---------------------------------------------------------------------------
-- 7. Pending / executed deletion obligations to reapply after restore
--    (DPA clause 10 / data-protection-operations.md step 6)
-- ---------------------------------------------------------------------------
\echo '--- 7. Deletion obligations present in the restored data ---'
SELECT
    company_id,
    deletion_scheduled_at,
    deletion_scheduled_by,
    deletion_cancelled_at,
    deletion_executed_at,
    CASE
        WHEN deletion_executed_at IS NOT NULL THEN 'EXECUTED - re-stamp + re-verify per-store hard delete'
        WHEN deletion_scheduled_at IS NOT NULL
             AND deletion_cancelled_at IS NULL
             AND deletion_executed_at IS NULL THEN 'PENDING - confirm countdown, re-evaluate date'
        ELSE 'none'
    END AS reapplication_action
FROM companies.customer_subscriptions
WHERE deletion_scheduled_at IS NOT NULL
   OR deletion_executed_at IS NOT NULL
ORDER BY deletion_executed_at NULLS LAST, deletion_scheduled_at;

\echo '  ^ Cross-check this list against the out-of-band deletion obligations register.'
\echo '    Any obligation in the register but NOT above must be re-created before go-live.'

\echo '================ END VALIDATION - record results in restore-drill.md ================'
